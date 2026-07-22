using Microsoft.Win32;
using ModernWpf;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using YoableWPF.Managers;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace YoableWPF
{
    public partial class MainWindow : Window
    {
        // Managers (now public so ProjectManager can access them)
        public ImageManager imageManager;
        public LabelManager labelManager;
        public ProjectManager projectManager;
        private UIStateManager uiStateManager;
        private PropagationManager propagationManager;
        private readonly RoiCropManager roiCropManager = new();
        private readonly DatasetExportManager datasetExportManager = new();
        private readonly DatasetStatisticsManager datasetStatisticsManager = new();
        private readonly AugmentationManager augmentationManager = new();

        // Class management
        private List<LabelClass> projectClasses = new List<LabelClass>();
        public IReadOnlyList<LabelClass> ProjectClasses => projectClasses;
        // Suppresses class-filter re-application while several checkboxes are toggled at once.
        private bool suppressClassFilterApply;

        // External Managers/Handlers (unchanged)
        public YoloAI yoloAI;
        public OverlayManager overlayManager;
        private YoutubeDownloader youtubeDownloader;
        private HotkeyManager hotkeyManager;

        public OverlayManager OverlayManager => overlayManager;

        public MainWindow()
        {
            InitializeComponent();

            // Find DrawingCanvas from XAML (pass Listbox)
            drawingCanvas = (DrawingCanvas)FindName("drawingCanvas");
            drawingCanvas.LabelListBox = LabelListBox;

            // Subscribe to the LabelsChanged event to detect any label modifications
            drawingCanvas.LabelsChanged += (sender, e) =>
            {
                // Refresh the label list and status immediately
                RefreshLabelListFromCanvas();
                
                if (projectManager != null && projectManager.IsProjectOpen)
                {
                    MarkProjectDirty();
                }
            };

            // Load saved settings
            bool isDarkTheme = Properties.Settings.Default.DarkTheme;
            ThemeManager.Current.ApplicationTheme = isDarkTheme ? ApplicationTheme.Dark : ApplicationTheme.Light;

            string FormAccentHex = Properties.Settings.Default.FormAccent;
            ThemeManager.Current.AccentColor = (Color)ColorConverter.ConvertFromString(FormAccentHex);

            // Initialize managers
            imageManager = new ImageManager();
            labelManager = new LabelManager();
            uiStateManager = new UIStateManager(this);
            propagationManager = new PropagationManager(labelManager, imageManager);
            DuplicateImageReview.Initialize(
                imageManager,
                labelManager,
                RemoveDuplicateImageFromProject,
                OpenDuplicateImageForEditing,
                () => ProjectClasses);
            RoiCropReview.Initialize(
                GetRoiCropPreviewAsync,
                ApplyCenterRoiCropAsync,
                FindImagesSmallerThan,
                RemoveSmallImagesFromProject);
            DatasetStats.Initialize(
                () => datasetStatisticsManager.Compute(imageManager, labelManager, projectClasses));

            // Apply batch size settings from user preferences
            imageManager.BatchSize = Properties.Settings.Default.ProcessingBatchSize;
            labelManager.LabelLoadBatchSize = Properties.Settings.Default.LabelLoadBatchSize;

            yoloAI = new YoloAI();
            overlayManager = new OverlayManager(this);
            youtubeDownloader = new YoutubeDownloader(this, overlayManager);
            hotkeyManager = new HotkeyManager();
            
            // Load hotkey settings
            LoadHotkeys();

            // Subscribe to class changes from DrawingCanvas
            drawingCanvas.CurrentClassChanged += DrawingCanvas_CurrentClassChanged;

            // Initialize with default class if no project loaded
            InitializeDefaultClass();

            // Note: projectManager will be set by StartupWindow or initialized here if continuing without project
            // Check for updates from Github
            if (Properties.Settings.Default.CheckUpdatesOnLaunch)
            {
                var autoUpdater = new UpdateManager(this, overlayManager, VersionInfo.CurrentVersion);
                autoUpdater.CheckForUpdatesAsync();
            }

            // Subscribe to language changes
            LanguageManager.Instance.LanguageChanged += LanguageManager_LanguageChanged;
        }

        private async void MainWorkspaceTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.Source == MainWorkspaceTabs && DuplicateImagesTab.IsSelected)
            {
                if (!string.IsNullOrWhiteSpace(imageManager.CurrentImagePath))
                {
                    labelManager.SaveLabels(
                        imageManager.CurrentImagePath,
                        drawingCanvas.Labels);
                }
                await DuplicateImageReview.EnsureScannedAsync();
            }
            else if (e.Source == MainWorkspaceTabs && RoiCropTab.IsSelected)
                await RoiCropReview.RefreshPreviewAsync();
            else if (e.Source == MainWorkspaceTabs && DatasetStatsTab.IsSelected)
            {
                if (!string.IsNullOrWhiteSpace(imageManager.CurrentImagePath))
                {
                    labelManager.SaveLabels(
                        imageManager.CurrentImagePath,
                        drawingCanvas.Labels);
                }
                DatasetStats.Refresh();
            }
        }

        private bool RemoveDuplicateImageFromProject(string fileName)
        {
            return RemoveImagesFromProject(new[] { fileName }) == 1;
        }

        private bool OpenDuplicateImageForEditing(string fileName)
        {
            ImageListItem? targetItem = ImageListBox.Items
                .OfType<ImageListItem>()
                .FirstOrDefault(item => string.Equals(
                    item.FileName,
                    fileName,
                    StringComparison.OrdinalIgnoreCase));
            if (targetItem == null)
                return false;

            MainWorkspaceTabs.SelectedItem = ImageAnnotationTab;
            if (!ReferenceEquals(ImageListBox.SelectedItem, targetItem) ||
                !string.Equals(
                    imageManager.CurrentImagePath,
                    fileName,
                    StringComparison.OrdinalIgnoreCase))
            {
                ImageListBox.SelectedItem = null;
                ImageListBox.SelectedItem = targetItem;
            }

            ImageListBox.ScrollIntoView(targetItem);
            ImageListBox.Focus();
            return true;
        }

        private IReadOnlyList<string> FindImagesSmallerThan(int minimumWidth, int minimumHeight)
        {
            return imageManager.ImagePathMap
                .Where(pair =>
                    pair.Value.OriginalDimensions.Width < minimumWidth ||
                    pair.Value.OriginalDimensions.Height < minimumHeight)
                .Select(pair => pair.Key)
                .OrderBy(fileName => fileName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private int RemoveSmallImagesFromProject(IReadOnlyCollection<string> fileNames)
        {
            int removedCount = RemoveImagesFromProject(fileNames);
            if (removedCount > 0)
                DuplicateImageReview.InvalidateResults();
            return removedCount;
        }

        private int RemoveImagesFromProject(IReadOnlyCollection<string> fileNames)
        {
            if (fileNames.Count == 0)
                return 0;

            var requestedNames = new HashSet<string>(
                fileNames,
                StringComparer.OrdinalIgnoreCase);
            string currentFileName = imageManager.CurrentImagePath;
            string? selectedFileName = (ImageListBox.SelectedItem as ImageListItem)?.FileName;
            int selectedIndex = ImageListBox.SelectedIndex;
            var removedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string fileName in requestedNames)
            {
                if (!imageManager.RemoveImage(fileName))
                    continue;

                removedNames.Add(fileName);
                labelManager.RemoveLabels(fileName);
                uiStateManager.RemoveImage(fileName);
            }

            if (removedNames.Count == 0)
                return 0;

            ImageListBox.SelectionChanged -= ImageListBox_SelectionChanged;
            try
            {
                for (int index = ImageListBox.Items.Count - 1; index >= 0; index--)
                {
                    if (ImageListBox.Items[index] is ImageListItem item &&
                        removedNames.Contains(item.FileName))
                    {
                        ImageListBox.Items.RemoveAt(index);
                    }
                }
            }
            finally
            {
                ImageListBox.SelectionChanged += ImageListBox_SelectionChanged;
            }

            if (projectManager?.CurrentProject != null)
            {
                var project = projectManager.CurrentProject;
                project.Images.RemoveAll(image => removedNames.Contains(image.FileName));
                foreach (string fileName in removedNames)
                {
                    project.ImageStatuses.Remove(fileName);
                    project.AppCreatedLabels.Remove(fileName);
                    project.ImportedLabelPaths.Remove(fileName);
                    project.SuggestedLabels.Remove(fileName);
                }
            }

            bool removedDisplayedImage = removedNames.Contains(currentFileName) ||
                                         (!string.IsNullOrWhiteSpace(selectedFileName) &&
                                          removedNames.Contains(selectedFileName));
            if (removedDisplayedImage)
            {
                drawingCanvas.Image = null!;
                drawingCanvas.Labels.Clear();
                drawingCanvas.SuggestedLabels.Clear();
                LabelListBox.ItemsSource = null;

                if (ImageListBox.Items.Count > 0)
                {
                    int nextIndex = Math.Min(Math.Max(selectedIndex, 0), ImageListBox.Items.Count - 1);
                    ImageListBox.SelectedIndex = nextIndex;
                }
            }

            uiStateManager.UpdateStatusCounts();
            MarkProjectDirty();
            return removedNames.Count;
        }

        // Right-clicking an image should target that image, so select the item under the cursor
        // before its context menu opens. Right-clicking empty space suppresses the menu.
        private void ImageListBox_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            DependencyObject? source = e.OriginalSource as DependencyObject;
            while (source != null && source is not ListBoxItem)
                source = VisualTreeHelper.GetParent(source);

            if (source is ListBoxItem container)
                container.IsSelected = true;
            else
                e.Handled = true;
        }

        // Safety net: never show the image context menu when there is no image to act on.
        private void ImageListContextMenu_Opened(object sender, RoutedEventArgs e)
        {
            if (ImageListBox.SelectedItem is not ImageListItem)
                ImageListContextMenu.IsOpen = false;
        }

        private ImageListItem? GetSelectedImageItem()
        {
            return ImageListBox.SelectedItem as ImageListItem;
        }

        private void OpenImageLocation_Click(object sender, RoutedEventArgs e)
        {
            ImageListItem? item = GetSelectedImageItem();
            if (item == null)
                return;

            if (!imageManager.ImagePathMap.TryGetValue(item.FileName, out var info))
                return;

            RevealInExplorer(info.Path);
        }

        private void OpenLabelLocation_Click(object sender, RoutedEventArgs e)
        {
            ImageListItem? item = GetSelectedImageItem();
            if (item == null)
                return;

            string? labelPath = ResolveLabelFilePath(item.FileName);
            if (labelPath == null)
            {
                CustomMessageBox.Show(
                    LanguageManager.Instance.GetString("Main_NoLabelFileOnDisk") ??
                        "No label file exists on disk yet for this image. Save or export the project first.",
                    LanguageManager.Instance.GetString("Main_Information") ?? "Information",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            RevealInExplorer(labelPath);
        }

        private void DeleteImage_Click(object sender, RoutedEventArgs e)
        {
            ImageListItem? item = GetSelectedImageItem();
            if (item == null)
                return;

            var confirm = CustomMessageBox.Show(
                string.Format(
                    LanguageManager.Instance.GetString("Main_ConfirmDeleteImage") ??
                        "Move '{0}' and its label file to the Recycle Bin and remove it from the project?",
                    item.FileName),
                LanguageManager.Instance.GetString("Msg_ConfirmDeletion") ?? "Confirm Deletion",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes)
                return;

            // Resolve the on-disk paths before removing the image clears its project entries.
            string? imagePath = imageManager.ImagePathMap.TryGetValue(item.FileName, out var info)
                ? info.Path
                : null;
            string? labelPath = ResolveLabelFilePath(item.FileName);

            // Removing from the project also releases the cached bitmap so the file is not locked.
            RemoveImagesFromProject(new[] { item.FileName });

            var errors = new List<string>();
            TryRecycleFile(imagePath, errors);
            TryRecycleFile(labelPath, errors);

            if (errors.Count > 0)
            {
                CustomMessageBox.Show(
                    string.Format(
                        LanguageManager.Instance.GetString("Main_DeleteImageFailed") ??
                            "The image was removed from the project, but some files could not be deleted:\n\n{0}",
                        string.Join("\n", errors)),
                    LanguageManager.Instance.GetString("Main_Error") ?? "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        // Resolves the label .txt file on disk for an image, or null if none exists yet.
        private string? ResolveLabelFilePath(string fileName)
        {
            var project = projectManager?.CurrentProject;
            if (project != null)
            {
                if (project.ImportedLabelPaths.TryGetValue(fileName, out var importedPath) &&
                    File.Exists(importedPath))
                {
                    return importedPath;
                }

                if (!string.IsNullOrEmpty(project.ProjectFolder))
                {
                    if (project.AppCreatedLabels.TryGetValue(fileName, out var relativePath))
                    {
                        string appLabelPath = Path.Combine(project.ProjectFolder, relativePath);
                        if (File.Exists(appLabelPath))
                            return appLabelPath;
                    }

                    string conventionalPath = Path.Combine(
                        project.ProjectFolder,
                        "labels",
                        Path.GetFileNameWithoutExtension(fileName) + ".txt");
                    if (File.Exists(conventionalPath))
                        return conventionalPath;
                }
            }

            // Fall back to a label file sitting next to the image (common for imported datasets).
            if (imageManager.ImagePathMap.TryGetValue(fileName, out var info))
            {
                string? imageDir = Path.GetDirectoryName(info.Path);
                if (!string.IsNullOrEmpty(imageDir))
                {
                    string siblingPath = Path.Combine(
                        imageDir,
                        Path.GetFileNameWithoutExtension(fileName) + ".txt");
                    if (File.Exists(siblingPath))
                        return siblingPath;
                }
            }

            return null;
        }

        // Opens Windows Explorer with the given file selected, or its folder when the file is missing.
        private void RevealInExplorer(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"/select,\"{path}\"",
                        UseShellExecute = true
                    });
                    return;
                }

                string? directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = directory,
                        UseShellExecute = true
                    });
                    return;
                }

                CustomMessageBox.Show(
                    string.Format(
                        LanguageManager.Instance.GetString("Msg_FileNotFound") ?? "File not found: {0}",
                        path),
                    LanguageManager.Instance.GetString("Main_Information") ?? "Information",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show(
                    string.Format(
                        LanguageManager.Instance.GetString("Msg_ErrorOccurred") ?? "An error occurred: {0}",
                        ex.Message),
                    LanguageManager.Instance.GetString("Main_Error") ?? "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private static void TryRecycleFile(string? path, List<string> errors)
        {
            if (string.IsNullOrEmpty(path))
                return;

            try
            {
                if (File.Exists(path))
                {
                    Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                        path,
                        Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                        Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                }
            }
            catch (Exception ex)
            {
                errors.Add($"{Path.GetFileName(path)}: {ex.Message}");
            }
        }

        private async Task<RoiCropPreviewData?> GetRoiCropPreviewAsync()
        {
            string? fileName = (ImageListBox.SelectedItem as ImageListItem)?.FileName;
            if (string.IsNullOrWhiteSpace(fileName))
                fileName = imageManager.CurrentImagePath;
            if (string.IsNullOrWhiteSpace(fileName))
                fileName = imageManager.ImagePathMap.Keys.FirstOrDefault();

            if (string.IsNullOrWhiteSpace(fileName) ||
                !imageManager.ImagePathMap.TryGetValue(fileName, out var imageInfo))
            {
                return null;
            }

            var bitmap = imageManager.Cache.TryGet(imageInfo.Path) ??
                         await Task.Run(() => imageManager.Cache.GetOrLoad(imageInfo.Path));
            if (bitmap == null)
                return null;

            return new RoiCropPreviewData(
                bitmap,
                imageInfo.OriginalDimensions,
                fileName);
        }

        private async Task<RoiCropBatchResult> ApplyCenterRoiCropAsync(
            int cropWidth,
            int cropHeight,
            RoiCropScope scope,
            IProgress<(int current, int total, string fileName)> progress,
            CancellationToken cancellationToken)
        {
            if (projectManager?.IsProjectOpen != true ||
                string.IsNullOrWhiteSpace(projectManager.CurrentProject?.ProjectFolder))
            {
                throw new InvalidOperationException(
                    LanguageManager.Instance.GetString("Roi_ProjectRequired") ??
                    "Open or create a project before cropping images.");
            }

            if (!string.IsNullOrWhiteSpace(imageManager.CurrentImagePath))
                labelManager.SaveLabels(imageManager.CurrentImagePath, drawingCanvas.Labels);

            IEnumerable<KeyValuePair<string, ImageManager.ImageInfo>> selectedImages =
                imageManager.ImagePathMap.ToArray();
            if (scope == RoiCropScope.CurrentImage)
            {
                string? currentFileName = (ImageListBox.SelectedItem as ImageListItem)?.FileName;
                if (string.IsNullOrWhiteSpace(currentFileName))
                    currentFileName = imageManager.CurrentImagePath;

                selectedImages = imageManager.ImagePathMap
                    .Where(pair => string.Equals(
                        pair.Key,
                        currentFileName,
                        StringComparison.OrdinalIgnoreCase))
                    .ToArray();
            }

            var sourceItems = selectedImages
                .Select(pair => new RoiCropSourceItem(
                    pair.Key,
                    pair.Value.Path,
                    labelManager.GetLabels(pair.Key),
                    labelManager.GetSuggestions(pair.Key)))
                .ToArray();

            if (sourceItems.Length == 0)
                throw new InvalidOperationException(
                    LanguageManager.Instance.GetString("Roi_NoImages") ??
                    "No images are available to crop.");

            string outputDirectory = Path.Combine(
                projectManager.CurrentProject.ProjectFolder,
                "roi_images");
            var result = await roiCropManager.CropBatchAsync(
                sourceItems,
                outputDirectory,
                cropWidth,
                cropHeight,
                progress,
                cancellationToken);

            foreach (var processed in result.ProcessedItems)
            {
                imageManager.ReplaceImage(
                    processed.FileName,
                    processed.OutputPath,
                    processed.OutputSize);
                labelManager.SaveLabels(processed.FileName, processed.Labels);
                labelManager.SaveSuggestions(processed.FileName, processed.Suggestions);

                var imageReference = projectManager.CurrentProject.Images.FirstOrDefault(image =>
                    string.Equals(
                        image.FileName,
                        processed.FileName,
                        StringComparison.OrdinalIgnoreCase));
                if (imageReference != null)
                {
                    imageReference.FullPath = processed.OutputPath;
                    imageReference.Width = processed.OutputSize.Width;
                    imageReference.Height = processed.OutputSize.Height;
                }

                // The transformed labels now belong to the project, not the original
                // external Roboflow label file.
                projectManager.CurrentProject.ImportedLabelPaths.Remove(processed.FileName);

                ImageStatus status = DetermineImageStatus(processed.FileName);
                imageManager.UpdateImageStatusValue(processed.FileName, status);
                if (uiStateManager.TryGetFromCache(processed.FileName, out var imageItem))
                    imageItem.Status = status;
            }

            if (result.ProcessedItems.Count > 0)
            {
                uiStateManager.UpdateStatusCounts();
                DuplicateImageReview.InvalidateResults();

                string? selectedFileName = (ImageListBox.SelectedItem as ImageListItem)?.FileName;
                bool refreshSelectedImage = result.ProcessedItems.Any(item => string.Equals(
                    item.FileName,
                    selectedFileName,
                    StringComparison.OrdinalIgnoreCase));
                if (refreshSelectedImage && ImageListBox.SelectedItem is ImageListItem selectedItem)
                {
                    imageManager.CurrentImagePath = "";
                    ImageListBox.SelectedItem = null;
                    ImageListBox.SelectedItem = selectedItem;
                }

                MarkProjectDirty();
            }

            return result;
        }

        private void LanguageManager_LanguageChanged(object sender, EventArgs e)
        {
            // Reload window resources when language changes
            Dispatcher.Invoke(() =>
            {
                ReloadWindowResources();
            });
        }

        private void ReloadWindowResources()
        {
            try
            {
                // Force all DynamicResource bindings to re-evaluate
                LanguageManager.ReloadWindowResources(this);

                // Manually update window title and dynamic text
                this.Title = LanguageManager.Instance.GetString("MainWindow_Title");

                // Update project name (if project is open)
                if (projectManager != null && projectManager.IsProjectOpen)
                {
                    ProjectNameText.Text = projectManager.CurrentProject.ProjectName;
                }
                else
                {
                    ProjectNameText.Text = LanguageManager.Instance.GetString("Status_NoProject");
                }

                // Update project UI (including save status)
                UpdateProjectUI();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to reload language resources in MainWindow: {ex.Message}");
            }
        }

        #region Helper Methods

        /// <summary>
        /// Prompts user to save changes if there are unsaved changes.
        /// Returns true if it's safe to proceed (changes saved or discarded).
        /// Returns false if user cancelled.
        /// </summary>
        private bool PromptToSaveChanges(string actionDescription = "continue")
        {
            if (projectManager?.IsProjectOpen != true || !projectManager.HasUnsavedChanges)
                return true; // No unsaved changes, safe to proceed

            var result = CustomMessageBox.Show(
                $"Save changes to the current project before {actionDescription}?",
                "Unsaved Changes",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Cancel)
                return false; // User cancelled

            if (result == MessageBoxResult.Yes)
            {
                SaveProject_Click(null, null);
            }

            return true; // User chose No or Yes (and save completed)
        }

        /// <summary>
        /// Creates a progress reporter for overlay updates
        /// </summary>
        private IProgress<(int current, int total, string message)> CreateProgressReporter()
        {
            return new Progress<(int current, int total, string message)>(report =>
            {
                Dispatcher.Invoke(() =>
                {
                    int percentage = report.total > 0
                        ? (report.current * 100) / report.total
                        : 0;
                    overlayManager.UpdateProgress(percentage);
                    overlayManager.UpdateMessage(report.message);
                });
            });
        }

        /// <summary>
        /// Fixes labels that reference non-existent class IDs by reassigning them to the default class
        /// </summary>
        private void FixOrphanedLabels(List<LabelData> labels)
        {
            if (projectClasses == null || projectClasses.Count == 0 || labels == null || labels.Count == 0)
                return;

            // Get all valid class IDs
            var validClassIds = new HashSet<int>(projectClasses.Select(c => c.ClassId));

            // Get the default class (class with ID 0, or first class)
            var defaultClass = projectClasses.FirstOrDefault(c => c.ClassId == 0) ?? projectClasses.First();

            // Fix any labels with invalid ClassIds
            foreach (var label in labels)
            {
                if (!validClassIds.Contains(label.ClassId))
                {
                    label.ClassId = defaultClass.ClassId;
                }
            }
        }

        #endregion



        #region Project Methods

        private void NewProject_Click(object sender, RoutedEventArgs e)
        {
            if (!PromptToSaveChanges("creating a new project"))
                return;

            var newProjectDialog = new NewProjectDialog();
            newProjectDialog.Owner = this;

            if (newProjectDialog.ShowDialog() == true)
            {
                string projectName = newProjectDialog.ProjectName;
                string projectLocation = newProjectDialog.ProjectLocation;

                // Close current project if any
                if (projectManager != null)
                {
                    projectManager.CloseProject(false);
                }
                else
                {
                    projectManager = new ProjectManager(this);
                }

                if (projectManager.CreateNewProject(projectName, projectLocation))
                {
                    ProjectNameText.Text = projectName;
                    UpdateProjectUI();
                    projectManager.StartAutoSave();
                    propagationManager.SetProjectFolder(projectManager.CurrentProject?.ProjectFolder);
                }
            }
        }

        private async void OpenProject_Click(object sender, RoutedEventArgs e)
        {
            if (!PromptToSaveChanges("opening another project"))
                return;

            var openFileDialog = new OpenFileDialog
            {
                Filter = "Yoable Project Files (*.yoable)|*.yoable|All Files (*.*)|*.*",
                Title = "Open Project"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                // Close current project if any
                if (projectManager != null)
                {
                    projectManager.CloseProject(false);
                }
                else
                {
                    projectManager = new ProjectManager(this);
                }

                // Use async loading with progress
                await LoadProjectWithProgressAsync(openFileDialog.FileName);
            }
        }

        /// <summary>
        /// Loads a project asynchronously with progress overlay
        /// </summary>
        private async Task LoadProjectWithProgressAsync(string projectPath)
        {
            try
            {
                // Create cancellation token for the loading process
                var loadCancellationToken = new CancellationTokenSource();

                // Show loading overlay
                overlayManager.ShowOverlayWithProgress("Loading project...", loadCancellationToken);

                // Disable window during loading
                this.IsEnabled = false;

                // Create progress reporter
                var progress = CreateProgressReporter();

                // Load the project with progress feedback
                bool loaded = await projectManager.LoadProjectAsync(projectPath, progress);

                if (!loaded)
                {
                    overlayManager.HideOverlay();
                    this.IsEnabled = true;
                    return;
                }

                // Update message for import phase
                overlayManager.UpdateMessage("Importing project data...");

                // Import project data into main window with progress
                await projectManager.ImportProjectDataAsync(progress, loadCancellationToken.Token);

                // Update all image statuses based on loaded labels
                overlayManager.UpdateMessage("Updating image statuses...");
                await UpdateAllImageStatusesAsync();

                // Update UI to reflect loaded data
                await Dispatcher.InvokeAsync(() =>
                {
                    RefreshUIAfterProjectLoadAsync();
                    ProjectNameText.Text = projectManager.CurrentProject.ProjectName;
                    UpdateProjectUI();
                });

                // Start auto-save
                projectManager.StartAutoSave();

                propagationManager.SetProjectFolder(projectManager.CurrentProject?.ProjectFolder);

                // Hide overlay and enable window
                overlayManager.HideOverlay();
                this.IsEnabled = true;
            }
            catch (OperationCanceledException)
            {
                overlayManager.HideOverlay();
                this.IsEnabled = true;
                CustomMessageBox.Show(LanguageManager.Instance.GetString("Msg_ProjectLoadCanceled") ?? "Project loading was canceled.", LanguageManager.Instance.GetString("Msg_Canceled") ?? "Canceled", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                overlayManager.HideOverlay();
                this.IsEnabled = true;
                CustomMessageBox.Show(string.Format(LanguageManager.Instance.GetString("Msg_FailedToLoadProject") ?? "Failed to load project:\n\n{0}", ex.Message), LanguageManager.Instance.GetString("Msg_Error") ?? "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void SaveProject_Click(object sender, RoutedEventArgs e)
        {
            if (projectManager == null || !projectManager.IsProjectOpen)
            {
                // No project open - directly open project creation dialog
                NewProject_Click(sender, e);
                return;
            }

            // Prevent concurrent saves
            if (projectManager.IsSaving)
            {
                return; // Already saving, ignore this request
            }

            // Save classes before saving project
            if (projectManager?.CurrentProject != null)
            {
                projectManager.CurrentProject.Classes = projectClasses;
            }

            // Use async save with progress
            bool success = await projectManager.SaveProjectAsync();

            if (success)
            {
                // Update UI to reflect saved state
                UpdateProjectUI();
            }
        }

        private async void SaveProjectAs_Click(object sender, RoutedEventArgs e)
        {
            if (projectManager == null || !projectManager.IsProjectOpen)
            {
                CustomMessageBox.Show(
                    LanguageManager.Instance.GetString("Msg_NoProjectOpen") ?? "No project is currently open.",
                    LanguageManager.Instance.GetString("Msg_NoProject") ?? "No Project",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var saveFileDialog = new SaveFileDialog
            {
                Filter = "Yoable Project Files (*.yoable)|*.yoable",
                DefaultExt = ".yoable",
                AddExtension = true,
                Title = "Save Project As",
                FileName = projectManager.CurrentProject.ProjectName
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                // Show overlay for save operation
                overlayManager.ShowOverlay("Saving project as...");

                try
                {
                    // Save classes before saving project
                    if (projectManager?.CurrentProject != null)
                    {
                        projectManager.CurrentProject.Classes = projectClasses;
                    }

                    // Save to new location
                    if (await projectManager.SaveProjectAsAsync(saveFileDialog.FileName))
                    {
                        ProjectNameText.Text = projectManager.CurrentProject.ProjectName;
                        UpdateProjectUI();
                        propagationManager.SetProjectFolder(projectManager.CurrentProject?.ProjectFolder);
                    }
                }
                finally
                {
                    overlayManager.HideOverlay();
                }
            }
        }

        private void CloseProject_Click(object sender, RoutedEventArgs e)
        {
            if (projectManager == null || !projectManager.IsProjectOpen)
            {
                CustomMessageBox.Show(
                    LanguageManager.Instance.GetString("Msg_NoProjectOpen") ?? "No project is currently open.",
                    LanguageManager.Instance.GetString("Msg_NoProject") ?? "No Project",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            if (projectManager.CloseProject())
            {
                // Clear UI completely
                ProjectNameText.Text = LanguageManager.Instance.GetString("Status_NoProject");
                LastSaveText.Text = LanguageManager.Instance.GetString("Status_NotSaved");
                LastSaveTimeText.Text = "";

                // Clear all data
                ImageListBox.Items.Clear();
                LabelListBox.ItemsSource = null;
                drawingCanvas.Labels.Clear();
                drawingCanvas.SuggestedLabels.Clear();
                drawingCanvas.Image = null;
                drawingCanvas.InvalidateVisual();
                uiStateManager.ClearCache();
                uiStateManager.RefreshAllImagesList();

                // Update project UI
                UpdateProjectUI();

                // Update status counts
                uiStateManager.UpdateStatusCounts();
                UpdateSuggestionSummaryUI();
                propagationManager.SetProjectFolder(null);
            }
        }

        /// <summary>
        /// Updates the project UI indicators (save status, auto-save, etc.)
        /// </summary>
        public void UpdateProjectUI()
        {
            if (projectManager == null || !projectManager.IsProjectOpen)
            {
                // No project mode
                SaveStatusBorder.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x44, 0x9E, 0x9E, 0x9E));
                LastSaveText.Text = LanguageManager.Instance.GetString("Status_NoProject");
                LastSaveText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0xFF, 0x9E, 0x9E, 0x9E));
                LastSaveTimeText.Text = "";
                AutoSaveText.Text = LanguageManager.Instance.GetString("Status_AutoSaveDisabled");
                AutoSaveIndicator.Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0xFF, 0x9E, 0x9E, 0x9E));
                return;
            }

            // Update save status
            if (projectManager.HasUnsavedChanges)
            {
                SaveStatusBorder.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x44, 0xFF, 0xB7, 0x4D));
                LastSaveText.Text = LanguageManager.Instance.GetString("Status_UnsavedChanges");
                LastSaveText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0xFF, 0xFF, 0xB7, 0x4D));
            }
            else
            {
                SaveStatusBorder.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x44, 0x81, 0xC7, 0x84));
                LastSaveText.Text = LanguageManager.Instance.GetString("Status_AllChangesSaved");
                LastSaveText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0xFF, 0x81, 0xC7, 0x84));
            }

            // Update last save time
            if (projectManager.LastSaveTime != DateTime.MinValue)
            {
                TimeSpan timeSince = DateTime.Now - projectManager.LastSaveTime;
                if (timeSince.TotalSeconds < 5)
                    LastSaveTimeText.Text = LanguageManager.Instance.GetString("Status_SavedJustNow");
                else if (timeSince.TotalMinutes < 1)
                    LastSaveTimeText.Text = string.Format(LanguageManager.Instance.GetString("Status_SavedSecondsAgo"), (int)timeSince.TotalSeconds);
                else if (timeSince.TotalMinutes < 60)
                    LastSaveTimeText.Text = string.Format(LanguageManager.Instance.GetString("Status_SavedMinutesAgo"), (int)timeSince.TotalMinutes);
                else if (timeSince.TotalHours < 24)
                    LastSaveTimeText.Text = string.Format(LanguageManager.Instance.GetString("Status_SavedHoursAgo"), (int)timeSince.TotalHours);
                else
                    LastSaveTimeText.Text = string.Format(LanguageManager.Instance.GetString("Status_SavedOn"), projectManager.LastSaveTime.ToString("MMM dd"));
            }
            else
            {
                LastSaveTimeText.Text = LanguageManager.Instance.GetString("Status_NotSavedYet");
            }

            // Update auto-save indicator
            if (Properties.Settings.Default.EnableAutoSave)
            {
                AutoSaveText.Text = LanguageManager.Instance.GetString("Status_AutoSaveEnabled");
                AutoSaveIndicator.Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0xFF, 0x4C, 0xAF, 0x50));
            }
            else
            {
                AutoSaveText.Text = LanguageManager.Instance.GetString("Status_AutoSaveDisabled");
                AutoSaveIndicator.Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0xFF, 0x9E, 0x9E, 0x9E));
            }
        }

        /// <summary>
        /// Refreshes the UI after loading a project
        /// </summary>
        /// <summary>
        /// Refreshes the UI after loading a project - now with batching to prevent UI freeze
        /// </summary>
        /// <summary>
        /// OPTIMIZED: Refreshes the UI after project load with improved performance for large datasets
        /// Creates all items in memory first, then adds them all at once to minimize UI updates
        /// </summary>
        public async Task RefreshUIAfterProjectLoadAsync()
        {
            // Load classes from project
            if (projectManager.CurrentProject.HasClasses)
            {
                projectClasses = new List<LabelClass>(projectManager.CurrentProject.Classes);
            }
            else
            {
                // Migrate old project - add default class
                projectClasses = new List<LabelClass> 
                { 
                    new LabelClass("default", "#E57373", 0) 
                };
                projectManager.CurrentProject.Classes = projectClasses;
            }
            RefreshClassList();

            // Restore model class mappings from project
            if (projectManager?.CurrentProject?.ModelClassMappingSets != null && yoloAI != null)
            {
                foreach (var model in yoloAI.GetLoadedModels())
                {
                    if (projectManager.CurrentProject.ModelClassMappingSets.TryGetValue(model.ModelPath, out var savedMapping))
                    {
                        model.ClassMapping = YoloAI.CloneClassMapping(savedMapping);
                    }
                }
            }

            // Clear the list first
            ImageListBox.Items.Clear();

            // Get all images
            var allImages = imageManager.ImagePathMap.Keys.ToArray();

            if (allImages.Length == 0)
            {
                // Update status counts for empty project
                uiStateManager.UpdateStatusCounts();
                uiStateManager.RefreshAllImagesList();
                return;
            }

            // OPTIMIZATION: Create all items in memory first (off UI thread)
            // Memory allocation is cheap, UI updates are expensive
            var allItems = new List<ImageListItem>(allImages.Length);

            await Task.Run(() =>
            {
                foreach (var fileName in allImages)
                {
                    var status = imageManager.GetImageStatus(fileName);
                    allItems.Add(new ImageListItem(fileName, status));
                }
            });

            // OPTIMIZATION: Add all items to the listbox in batches
            // This is much faster than adding one at a time with delays
            int uiBatchSize = Properties.Settings.Default.UIBatchSize;
            if (uiBatchSize <= 0) uiBatchSize = 100; // Safe default

            for (int i = 0; i < allItems.Count; i += uiBatchSize)
            {
                var batch = allItems.Skip(i).Take(uiBatchSize);
                foreach (var item in batch)
                {
                    ImageListBox.Items.Add(item);
                }

                // Only yield to UI thread occasionally, not every item
                if (i % (uiBatchSize * 5) == 0 && i > 0)
                {
                    await Task.Delay(1);
                }
            }


            // Build the cache for O(1) lookups
            uiStateManager.BuildCache(ImageListBox.Items);
            // Update status counts (do this ONCE, not multiple times)
            uiStateManager.UpdateStatusCounts();

            // Refresh the all images list for filtering
            uiStateManager.RefreshAllImagesList();

            // Apply saved sort mode
            if (projectManager?.CurrentProject != null)
            {
                // Set the sort combobox to match saved mode
                if (SortComboBox != null)
                {
                    if (projectManager.CurrentProject.CurrentSortMode == "ByStatus")
                    {
                        SortComboBox.SelectedIndex = 1;
                        uiStateManager.SortImagesByStatus();
                    }
                    else
                    {
                        SortComboBox.SelectedIndex = 0;
                        uiStateManager.SortImagesByName();
                    }
                }
                else
                {
                    // Fallback if combobox not available
                    if (projectManager.CurrentProject.CurrentSortMode == "ByStatus")
                        uiStateManager.SortImagesByStatus();
                    else
                        uiStateManager.SortImagesByName();
                }

                // Apply saved filter mode (currently always "All", but prepared for future)
                if (projectManager.CurrentProject.CurrentFilterMode != "All")
                {
                    // Add filter logic here when implemented
                }
            }

            // Select the saved image index if any
            if (ImageListBox.Items.Count > 0)
            {
                int targetIndex = projectManager?.CurrentProject?.LastSelectedImageIndex ?? 0;
                if (targetIndex >= ImageListBox.Items.Count)
                    targetIndex = 0;

                // Defer scrolling to allow UI to render first
                await Task.Delay(50);
                ImageListBox.SelectedIndex = targetIndex;

                if (ImageListBox.SelectedItem != null)
                    ImageListBox.ScrollIntoView(ImageListBox.SelectedItem);
            }
        }

        /// <summary>
        /// Marks the project as having unsaved changes
        /// </summary>
        private void MarkProjectDirty()
        {
            if (projectManager != null && projectManager.IsProjectOpen)
            {
                projectManager.MarkDirty();
                UpdateProjectUI();
            }
        }

        private async void Window_Closing(object sender, CancelEventArgs e)
        {
            // Check for unsaved changes
            if (projectManager != null && projectManager.IsProjectOpen && projectManager.HasUnsavedChanges)
            {
                var result = CustomMessageBox.Show(
                    string.Format(LanguageManager.Instance.GetString("Msg_UnsavedChangesPrompt") ?? "Do you want to save changes to '{0}'?", projectManager.CurrentProject.ProjectName),
                    LanguageManager.Instance.GetString("Msg_UnsavedChanges") ?? "Unsaved Changes",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Cancel)
                {
                    e.Cancel = true;
                    return;
                }

                if (result == MessageBoxResult.Yes)
                {
                    // Cancel the close temporarily
                    e.Cancel = true;

                    // Save synchronously (blocking) since we're closing
                    projectManager.ExportProjectData();
                    bool saved = projectManager.SaveProjectSync();

                    if (saved)
                    {
                        // Now actually close
                        projectManager?.Dispose();
                        yoloAI?.Dispose();
                        Application.Current.Shutdown();
                    }
                    return;
                }
            }

            // Cleanup
            projectManager?.Dispose();
            yoloAI?.Dispose();
            
            // Unsubscribe from language changes
            if (LanguageManager.Instance != null)
            {
                LanguageManager.Instance.LanguageChanged -= LanguageManager_LanguageChanged;
            }
        }

        #endregion

        #region Existing Methods

        public void OnLabelsChanged()
        {
            if (string.IsNullOrEmpty(imageManager.CurrentImagePath)) return;

            // Get currently selected index
            int currentIndex = ImageListBox.SelectedIndex;
            if (currentIndex < 0 || currentIndex >= ImageListBox.Items.Count) return;

            // Ensure we're using just the filename, not full path
            string currentFileName = Path.GetFileName(imageManager.CurrentImagePath);

            // Determine status using helper
            ImageStatus newStatus = DetermineImageStatus(currentFileName);

            // Use the imageManager method to update status
            imageManager.UpdateImageStatusValue(currentFileName, newStatus);

            // Force UI update on the dispatcher thread
            Dispatcher.Invoke(() =>
            {
                // Use O(1) cache lookup instead of O(n) iteration
                if (uiStateManager.TryGetFromCache(currentFileName, out var imageItem))
                {
                    imageItem.Status = newStatus;
                    
                    // Force refresh of the ListBox item
                    var container = ImageListBox.ItemContainerGenerator.ContainerFromItem(imageItem) as ListBoxItem;
                    if (container != null)
                    {
                        container.UpdateLayout();
                    }
                    else
                    {
                        // If container is null (item not visible/virtualized), force refresh of the entire list
                        ImageListBox.Items.Refresh();
                    }
                }
            }, System.Windows.Threading.DispatcherPriority.Render);

            uiStateManager.UpdateStatusCounts();

            // Mark project as dirty
            MarkProjectDirty();
        }

        public async void ImageListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ImageListBox.SelectedItem is ImageListItem selected &&
                imageManager.ImagePathMap.TryGetValue(selected.FileName, out ImageManager.ImageInfo imageInfo))
            {
                // Decode off the UI thread on a cache miss; a newer selection abandons this one
                int loadGeneration = ++imageLoadGeneration;
                var bitmap = imageManager.Cache.TryGet(imageInfo.Path);
                if (bitmap == null)
                {
                    bitmap = await Task.Run(() => imageManager.Cache.GetOrLoad(imageInfo.Path));
                    if (loadGeneration != imageLoadGeneration) return;
                    if (bitmap == null) return; // File unreadable
                }

                // Save labels of the image currently in the canvas. Kept after the await so
                // CurrentImagePath always matches the canvas content, even when selections
                // change faster than images decode.
                if (!string.IsNullOrEmpty(imageManager.CurrentImagePath))
                {
                    labelManager.SaveLabels(imageManager.CurrentImagePath, drawingCanvas.Labels);
                    MarkProjectDirty();
                }

                imageManager.CurrentImagePath = selected.FileName;

                drawingCanvas.LoadImage(bitmap, imageInfo.OriginalDimensions);

                // Reset zoom on image change
                drawingCanvas.ResetZoom();

                // Load labels for this image if they exist
                if (labelManager.LabelStorage.ContainsKey(selected.FileName))
                {
                    var labels = labelManager.GetLabels(selected.FileName);
                    drawingCanvas.Labels = new System.Collections.Generic.List<LabelData>(labels);

                    // Fix any labels that reference non-existent classes
                    FixOrphanedLabels(drawingCanvas.Labels);

                    // Update status when viewing an image with AI or imported labels
                    if (labels.Any(l => l.Name.StartsWith("AI") || l.Name.StartsWith("Imported")))
                    {
                        // Only update to Verified if it was previously VerificationNeeded
                        var currentStatus = imageManager.GetImageStatus(selected.FileName);
                        if (currentStatus == ImageStatus.VerificationNeeded &&
                            (!labelManager.SuggestionStorage.TryGetValue(selected.FileName, out var suggestions) || suggestions.Count == 0))
                        {
                            imageManager.UpdateImageStatusValue(selected.FileName, ImageStatus.Verified);

                            // Update the status property directly on the existing item
                            selected.Status = ImageStatus.Verified;
                        }
                    }
                }
                else
                {
                    drawingCanvas.Labels.Clear();
                }

                drawingCanvas.SuggestedLabels = labelManager.GetSuggestions(selected.FileName);
                drawingCanvas.SelectedSuggestion = null;

                // Update UI
                uiStateManager.UpdateStatusCounts();
                uiStateManager.RefreshLabelList();
                UpdateSuggestionSummaryUI();

                // CRITICAL FIX: Force canvas redraw after loading labels
                drawingCanvas.InvalidateVisual();

                // Warm the cache with neighboring images for instant navigation
                PrefetchNeighboringImages();
            }
        }

        // Bumped on every image selection so stale async decodes can be abandoned
        private int imageLoadGeneration = 0;

        private void PrefetchNeighboringImages()
        {
            int index = ImageListBox.SelectedIndex;
            if (index < 0) return;

            // Favor forward navigation: prefetch more items ahead than behind
            const int aheadCount = 12;
            const int behindCount = 4;

            var paths = new List<string>(aheadCount + behindCount);
            void AddPath(int i)
            {
                if (i < 0 || i >= ImageListBox.Items.Count || i == index) return;
                if (ImageListBox.Items[i] is ImageListItem item &&
                    imageManager.ImagePathMap.TryGetValue(item.FileName, out var info))
                {
                    paths.Add(info.Path);
                }
            }

            for (int offset = 1; offset <= aheadCount; offset++) AddPath(index + offset);
            for (int offset = 1; offset <= behindCount; offset++) AddPath(index - offset);

            imageManager.Cache.Prefetch(paths);
        }

        private void LabelListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LabelListBox.SelectedItem is LabelListItemView selectedItem)
            {
                if (selectedItem.IsSuggestion && selectedItem.Suggestion != null)
                {
                    drawingCanvas.SelectedSuggestion = selectedItem.Suggestion;
                    drawingCanvas.SelectedLabel = null;
                    drawingCanvas.SelectedLabels.Clear();
                    drawingCanvas.InvalidateVisual();
                    Keyboard.Focus(drawingCanvas);
                    return;
                }

                var selectedLabel = selectedItem.Label;
                if (selectedLabel != null)
                {
                    drawingCanvas.SelectedSuggestion = null;
                    drawingCanvas.SelectedLabel = selectedLabel;
                    drawingCanvas.SelectedLabels.Clear();
                    drawingCanvas.SelectedLabels.Add(selectedLabel);
                    drawingCanvas.InvalidateVisual();
                    Keyboard.Focus(drawingCanvas); // Ensure canvas captures key events
                }
            }
            else
            {
                // No label selected, so immediately deselect in canvas
                drawingCanvas.SelectedLabel = null;
                drawingCanvas.SelectedLabels.Clear();
                drawingCanvas.SelectedSuggestion = null;
                drawingCanvas.InvalidateVisual();
            }
        }

        private void AcceptSuggestion_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is LabelListItemView item && item.Suggestion != null)
            {
                string currentFile = GetCurrentFileName();
                if (string.IsNullOrEmpty(currentFile))
                    return;

                labelManager.AcceptSuggestion(currentFile, item.Suggestion.Id);
                RefreshSuggestionsForCurrentImage(currentFile);
                MarkProjectDirty();
            }
        }

        private void RejectSuggestion_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is LabelListItemView item && item.Suggestion != null)
            {
                string currentFile = GetCurrentFileName();
                if (string.IsNullOrEmpty(currentFile))
                    return;

                labelManager.RejectSuggestion(currentFile, item.Suggestion.Id);
                RefreshSuggestionsForCurrentImage(currentFile);
                MarkProjectDirty();
            }
        }

        private void ChangeLabelClass_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not LabelListItemView item || item.Label == null)
                return;

            ShowChangeClassMenu(item.Label, button);
        }

        private void LabelListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (LabelListBox.SelectedItem is not LabelListItemView item || item.Label == null)
                return;

            // Open the class picker anchored on the clicked item container
            var placement = LabelListBox.ItemContainerGenerator.ContainerFromItem(item) as UIElement ?? LabelListBox;
            ShowChangeClassMenu(item.Label, placement);
            e.Handled = true;
        }

        private void ShowChangeClassMenu(LabelData label, UIElement placementTarget)
        {
            if (label == null || projectClasses == null || projectClasses.Count == 0)
                return;

            var menu = new ContextMenu();

            foreach (var cls in projectClasses)
            {
                var menuItem = new MenuItem
                {
                    Header = cls.DisplayText,
                    IsCheckable = true,
                    IsChecked = cls.ClassId == label.ClassId,
                    Icon = new System.Windows.Shapes.Rectangle
                    {
                        Width = 12,
                        Height = 12,
                        Fill = cls.ColorBrush
                    }
                };

                int targetClassId = cls.ClassId;
                menuItem.Click += (s, args) =>
                {
                    if (label.ClassId == targetClassId)
                        return;

                    label.ClassId = targetClassId;
                    uiStateManager.RefreshLabelList();
                    drawingCanvas.InvalidateVisual();
                    OnLabelsChanged();
                    MarkProjectDirty();
                };

                menu.Items.Add(menuItem);
            }

            menu.PlacementTarget = placementTarget;
            menu.IsOpen = true;
        }

        private void AcceptAllSuggestions_Click(object sender, RoutedEventArgs e)
        {
            string currentFile = GetCurrentFileName();
            if (string.IsNullOrEmpty(currentFile))
                return;

            labelManager.AcceptAllSuggestions(currentFile);
            RefreshSuggestionsForCurrentImage(currentFile);
            MarkProjectDirty();
        }

        private void RejectAllSuggestions_Click(object sender, RoutedEventArgs e)
        {
            string currentFile = GetCurrentFileName();
            if (string.IsNullOrEmpty(currentFile))
                return;

            labelManager.RejectAllSuggestions(currentFile);
            RefreshSuggestionsForCurrentImage(currentFile);
            MarkProjectDirty();
        }

        private async void ClearAllSuggestions_Click(object sender, RoutedEventArgs e)
        {
            int totalSuggestions = labelManager.GetTotalSuggestionCount();

            if (totalSuggestions == 0)
            {
                CustomMessageBox.Show(
                    LanguageManager.Instance.GetString("Msg_NoSuggestionsToClear") ?? "No suggestions to clear.",
                    LanguageManager.Instance.GetString("Main_Information") ?? "Information",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var result = CustomMessageBox.Show(
                string.Format(LanguageManager.Instance.GetString("Msg_ConfirmClearAllSuggestions") ?? "Are you sure you want to clear all {0} suggestions from all images?", totalSuggestions),
                LanguageManager.Instance.GetString("Menu_ClearAllSuggestions") ?? "Clear All Suggestions",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;

            int cleared = labelManager.ClearAllSuggestions();

            // Update all image statuses
            await UpdateAllImageStatusesAsync();

            // Update current image display
            string currentFile = GetCurrentFileName();
            if (!string.IsNullOrEmpty(currentFile))
            {
                drawingCanvas.SuggestedLabels = labelManager.GetSuggestions(currentFile);
                drawingCanvas.SelectedSuggestion = null;
            }

            uiStateManager.RefreshLabelList();
            uiStateManager.UpdateStatusCounts();
            UpdateSuggestionSummaryUI();
            drawingCanvas.InvalidateVisual();

            if (projectManager?.IsProjectOpen == true)
            {
                MarkProjectDirty();
            }

            CustomMessageBox.Show(
                string.Format(LanguageManager.Instance.GetString("Msg_SuggestionsCleared") ?? "Cleared {0} suggestions from all images.", cleared),
                LanguageManager.Instance.GetString("Main_Information") ?? "Information",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void UpdateSuggestionSummaryUI()
        {
            string currentFile = GetCurrentFileName();
            if (string.IsNullOrEmpty(currentFile))
            {
                SuggestionSummaryPanel.Visibility = Visibility.Collapsed;
                return;
            }

            var suggestions = labelManager.GetSuggestions(currentFile);
            if (suggestions.Count == 0)
            {
                SuggestionSummaryPanel.Visibility = Visibility.Collapsed;
                return;
            }

            SuggestionSummaryPanel.Visibility = Visibility.Visible;
            SuggestionCountText.Text = string.Format(
                LanguageManager.Instance.GetString("Main_SuggestionsCount") ?? "Suggestions: {0}",
                suggestions.Count);
        }

        private void RefreshSuggestionsForCurrentImage(string currentFile)
        {
            drawingCanvas.Labels = labelManager.GetLabels(currentFile);
            drawingCanvas.SuggestedLabels = labelManager.GetSuggestions(currentFile);
            drawingCanvas.SelectedSuggestion = null;
            uiStateManager.RefreshLabelList();
            UpdateImageStatus(currentFile);
            uiStateManager.UpdateStatusCounts();
            UpdateSuggestionSummaryUI();
            drawingCanvas.InvalidateVisual();
        }

        private string GetCurrentFileName()
        {
            string currentFileName = imageManager.CurrentImagePath;
            if (string.IsNullOrEmpty(currentFileName))
                return string.Empty;

            if (currentFileName.Contains("\\") || currentFileName.Contains("/"))
            {
                currentFileName = Path.GetFileName(currentFileName);
            }

            return currentFileName;
        }

        internal void ShowDuplicateImagesWarning(IEnumerable<string> duplicates)
        {
            var duplicateList = duplicates?
                .Where(d => !string.IsNullOrWhiteSpace(d))
                .Distinct()
                .ToList() ?? new List<string>();

            if (duplicateList.Count == 0)
                return;

            string message = $"Skipped {duplicateList.Count} duplicate image(s) because image file names must be unique within a project.\n\n";
            message += string.Join("\n", duplicateList.Take(5));

            if (duplicateList.Count > 5)
            {
                message += $"\n... and {duplicateList.Count - 5} more";
            }

            CustomMessageBox.Show(
                message,
                LanguageManager.Instance.GetString("Msg_DuplicateImageNames") ?? "Duplicate Image Names",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        /// <summary>
        /// Helper method to refresh the label list from canvas - called by DrawingCanvas
        /// This also updates image status and saves labels to storage
        /// </summary>
        public void RefreshLabelListFromCanvas()
        {
            // Get current image filename - CurrentImagePath should be just the filename
            string currentFileName = imageManager.CurrentImagePath;
            
            if (string.IsNullOrEmpty(currentFileName))
                return;
                
            // If it's a full path, extract just the filename
            if (currentFileName.Contains("\\") || currentFileName.Contains("/"))
            {
                currentFileName = Path.GetFileName(currentFileName);
            }
            
            // Save current labels to storage
            labelManager.SaveLabels(currentFileName, drawingCanvas.Labels);
            
            // Update the image status
            UpdateImageStatus(currentFileName);
            
            // Update status counts in UI
            uiStateManager.UpdateStatusCounts();
            
            // Refresh the label list UI
            uiStateManager.RefreshLabelList();
            UpdateSuggestionSummaryUI();
            
            // Force canvas to redraw to show updated labels
            drawingCanvas.InvalidateVisual();
        }

        /// <summary>
        /// Clears all labels (classes) from the current image
        /// </summary>
        private void ClearImageClasses_Click(object sender, RoutedEventArgs e)
        {
            // Check if there's a current image
            if (string.IsNullOrEmpty(imageManager.CurrentImagePath))
            {
                CustomMessageBox.Show(
                    LanguageManager.Instance.GetString("Main_NoImageSelected") ?? "No image selected.",
                    LanguageManager.Instance.GetString("Main_Error") ?? "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            // Check if there are any labels to clear
            if (drawingCanvas.Labels == null || drawingCanvas.Labels.Count == 0)
            {
                CustomMessageBox.Show(
                    LanguageManager.Instance.GetString("Main_NoLabelsToClear") ?? "No labels to clear.",
                    LanguageManager.Instance.GetString("Main_Information") ?? "Information",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            // Confirm with user
            string confirmMessage = LanguageManager.Instance.GetString("Main_ConfirmClearLabels");
            if (string.IsNullOrEmpty(confirmMessage))
            {
                confirmMessage = $"Are you sure you want to clear all {drawingCanvas.Labels.Count} label(s) from this image?";
            }
            else
            {
                confirmMessage = string.Format(confirmMessage, drawingCanvas.Labels.Count);
            }
            
            var result = CustomMessageBox.Show(
                confirmMessage,
                LanguageManager.Instance.GetString("Main_ConfirmClear") ?? "Confirm Clear",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;

            // Get current image filename
            string currentFileName = imageManager.CurrentImagePath;
            if (currentFileName.Contains("\\") || currentFileName.Contains("/"))
            {
                currentFileName = Path.GetFileName(currentFileName);
            }

            // Clear all labels from canvas
            drawingCanvas.Labels.Clear();
            drawingCanvas.SelectedLabel = null;
            drawingCanvas.SelectedLabels.Clear();

            // Clear labels from storage
            labelManager.SaveLabels(currentFileName, drawingCanvas.Labels);

            // Update image status
            UpdateImageStatus(currentFileName);

            // Update UI
            uiStateManager.UpdateStatusCounts();
            uiStateManager.RefreshLabelList();
            drawingCanvas.InvalidateVisual();

            // Mark project as modified
            MarkProjectDirty();
        }

        private async void ImportDirectory_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                ValidateNames = false,
                CheckFileExists = false,
                CheckPathExists = true,
                FileName = "Select Folder"
            };

            // Set initial directory to last used location
            if (!string.IsNullOrEmpty(Properties.Settings.Default.LastImageDirectory) &&
                Directory.Exists(Properties.Settings.Default.LastImageDirectory))
            {
                openFileDialog.InitialDirectory = Properties.Settings.Default.LastImageDirectory;
            }

            if (openFileDialog.ShowDialog() == true)
            {
                string folderPath = Path.GetDirectoryName(openFileDialog.FileName);

                // Save this directory for next time
                Properties.Settings.Default.LastImageDirectory = folderPath;
                Properties.Settings.Default.Save();

                await LoadImagesAsync(folderPath);
            }
        }

        private async void ImportLabelsAndImage_Click(object sender, RoutedEventArgs e)
        {
            // Show dialog for selecting folders
            var dialog = new ImportLabelsAndImageDialog();
            dialog.Owner = this;

            if (dialog.ShowDialog() != true)
            {
                return; // User cancelled
            }

            string imagesFolderPath = dialog.ImagesFolderPath;
            string labelsFolderPath = dialog.LabelsFolderPath;

            // Step 3: Load images first
            var tokenSource = new CancellationTokenSource();
            overlayManager.ShowOverlayWithProgress("Loading images...", tokenSource);

            try
            {
                var progress = CreateProgressReporter();

                // Load images asynchronously
                await imageManager.LoadImagesFromDirectoryAsync(
                    imagesFolderPath,
                    progress,
                    tokenSource.Token,
                    Properties.Settings.Default.EnableParallelProcessing);

                // Update UI in batches
                var loadedFiles = imageManager.ImagePathMap.Values.Select(image => image.Path).ToArray();
                await UpdateImageListInBatchesAsync(loadedFiles, tokenSource.Token);

                if (ImageListBox.Items.Count > 0 && ImageListBox.SelectedItem == null)
                {
                    ImageListBox.SelectedIndex = 0;
                }

                // Build the cache for O(1) lookups
                uiStateManager.BuildCache(ImageListBox.Items);
                uiStateManager.UpdateStatusCounts();
                uiStateManager.RefreshAllImagesList();

                // Step 4: Load labels after images are loaded
                overlayManager.UpdateMessage("Loading labels...");
                await LoadYOLOLabelsFromDirectory(labelsFolderPath);

                MarkProjectDirty();
            }
            catch (OperationCanceledException)
            {
                CustomMessageBox.Show(LanguageManager.Instance.GetString("Msg_LoadingCanceled") ?? "Loading cancelled.", LanguageManager.Instance.GetString("Msg_Canceled") ?? "Cancelled", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show(string.Format(LanguageManager.Instance.GetString("Msg_ErrorDuringLoading") ?? "Error occurred during loading: {0}", ex.Message), LanguageManager.Instance.GetString("Msg_Error") ?? "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                overlayManager.HideOverlay();
            }
        }

        private async void ImportImage_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new() { Filter = "Image Files|*.jpg;*.jpeg;*.png" };
            if (openFileDialog.ShowDialog() == true)
            {
                // Use the same batch loading infrastructure for consistency
                if (imageManager.AddImage(openFileDialog.FileName))
                {
                    string fileName = Path.GetFileName(openFileDialog.FileName);
                    await UpdateImageListInBatchesAsync(new[] { fileName }, CancellationToken.None);

                    if (ImageListBox.Items.Count == 1)
                    {
                        ImageListBox.SelectedIndex = 0;
                    }

                    uiStateManager.UpdateStatusCounts();
                    uiStateManager.RefreshAllImagesList();
                    MarkProjectDirty();
                }
                else
                {
                    ShowDuplicateImagesWarning(imageManager.ConsumeDuplicateImageFiles());
                }
            }
        }

        private async void ImportLabels_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                ValidateNames = false,
                CheckFileExists = false,
                CheckPathExists = true,
                FileName = "Select Folder"
            };

            // Set initial directory to last used location
            if (!string.IsNullOrEmpty(Properties.Settings.Default.LastLabelDirectory) &&
                Directory.Exists(Properties.Settings.Default.LastLabelDirectory))
            {
                openFileDialog.InitialDirectory = Properties.Settings.Default.LastLabelDirectory;
            }

            if (openFileDialog.ShowDialog() == true)
            {
                string folderPath = Path.GetDirectoryName(openFileDialog.FileName);
                if (!string.IsNullOrEmpty(folderPath))
                {
                    // Save this directory for next time
                    Properties.Settings.Default.LastLabelDirectory = folderPath;
                    Properties.Settings.Default.Save();

                    await LoadYOLOLabelsFromDirectory(folderPath);
                }
            }
        }

        private async void YTToImage_Click(object sender, RoutedEventArgs e)
        {
            var downloadWindow = new YoutubeDownloadWindow();
            downloadWindow.Owner = this;
            if (downloadWindow.ShowDialog() == true)
            {
                await youtubeDownloader.DownloadAndProcessVideo(downloadWindow.YoutubeUrl, downloadWindow.desiredFps, downloadWindow.FrameSize);
            }
        }

        private async void ExportLabels_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                ValidateNames = false,
                CheckFileExists = false,
                CheckPathExists = true,
                FileName = "Select Folder"
            };

            // Set initial directory to last used location
            if (!string.IsNullOrEmpty(Properties.Settings.Default.LastExportDirectory) &&
                Directory.Exists(Properties.Settings.Default.LastExportDirectory))
            {
                openFileDialog.InitialDirectory = Properties.Settings.Default.LastExportDirectory;
            }

            if (openFileDialog.ShowDialog() == true)
            {
                string folderPath = Path.GetDirectoryName(openFileDialog.FileName);
                if (!string.IsNullOrEmpty(folderPath))
                {
                    // Save this directory for next time
                    Properties.Settings.Default.LastExportDirectory = folderPath;
                    Properties.Settings.Default.Save();

                    await ExportLabelsToYoloAsync(folderPath);
                }
            }
        }

        private async void ExportTrainingDataset_Click(object sender, RoutedEventArgs e)
        {
            if (imageManager.ImagePathMap.IsEmpty)
            {
                CustomMessageBox.Show(
                    LanguageManager.Instance.GetString("Msg_NoImagesToExport") ?? "There are no images to export.",
                    LanguageManager.Instance.GetString("Msg_Error") ?? "Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (projectClasses == null || !projectClasses.Any(c => c.ClassId >= 0))
            {
                CustomMessageBox.Show(
                    LanguageManager.Instance.GetString("Msg_NoClassesToExport") ?? "Define at least one class before exporting a training dataset.",
                    LanguageManager.Instance.GetString("Msg_Error") ?? "Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var dialog = new TrainingExportDialog(
                Properties.Settings.Default.LastTrainingExportDirectory,
                (int)Math.Round(Properties.Settings.Default.TrainingSplitTrain),
                (int)Math.Round(Properties.Settings.Default.TrainingSplitVal),
                (int)Math.Round(Properties.Settings.Default.TrainingSplitTest),
                Properties.Settings.Default.TrainingSplitSeed)
            {
                Owner = this
            };

            if (dialog.ShowDialog() != true || dialog.Options == null)
                return;

            var options = dialog.Options;

            // Persist choices for next time.
            Properties.Settings.Default.LastTrainingExportDirectory = options.OutputDirectory;
            Properties.Settings.Default.TrainingSplitTrain = options.TrainRatio * 100;
            Properties.Settings.Default.TrainingSplitVal = options.ValRatio * 100;
            Properties.Settings.Default.TrainingSplitTest = options.TestRatio * 100;
            Properties.Settings.Default.TrainingSplitSeed = options.Seed;
            Properties.Settings.Default.Save();

            await RunTrainingExportAsync(options);
        }

        private async Task RunTrainingExportAsync(TrainingExportOptions options)
        {
            var tokenSource = new CancellationTokenSource();
            overlayManager.ShowOverlayWithProgress(
                LanguageManager.Instance.GetString("Msg_ExportingTrainingDataset") ?? "Exporting training dataset...",
                tokenSource);

            try
            {
                var progress = CreateProgressReporter();

                var result = await datasetExportManager.ExportAsync(
                    options,
                    imageManager,
                    labelManager,
                    projectClasses,
                    progress,
                    tokenSource.Token);

                overlayManager.HideOverlay();

                string template = LanguageManager.Instance.GetString("Msg_TrainingDatasetExported")
                    ?? "Training dataset exported.\nTrain: {0}  Val: {1}  Test: {2}\nSkipped: {3}";
                CustomMessageBox.Show(
                    string.Format(template, result.TrainCount, result.ValCount, result.TestCount, result.SkippedCount),
                    LanguageManager.Instance.GetString("Msg_ExportComplete") ?? "Export Complete",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (OperationCanceledException)
            {
                overlayManager.HideOverlay();
                CustomMessageBox.Show(
                    LanguageManager.Instance.GetString("Msg_ExportCanceled") ?? "Export canceled.",
                    LanguageManager.Instance.GetString("Msg_Canceled") ?? "Canceled",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                overlayManager.HideOverlay();
                CustomMessageBox.Show(
                    string.Format(LanguageManager.Instance.GetString("Msg_ExportFailed") ?? "Export failed: {0}", ex.Message),
                    LanguageManager.Instance.GetString("Msg_Error") ?? "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void DataAugmentation_Click(object sender, RoutedEventArgs e)
        {
            if (imageManager.ImagePathMap.IsEmpty)
            {
                CustomMessageBox.Show(
                    LanguageManager.Instance.GetString("Msg_NoImagesToExport") ?? "There are no images to export.",
                    LanguageManager.Instance.GetString("Msg_Error") ?? "Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string currentFile = GetCurrentFileName();
            string defaultOutputDirectory = Properties.Settings.Default.LastAugmentationDirectory;
            if (string.IsNullOrWhiteSpace(defaultOutputDirectory))
            {
                ImageManager.ImageInfo sourceInfo = null;
                if (!string.IsNullOrEmpty(currentFile))
                    imageManager.ImagePathMap.TryGetValue(currentFile, out sourceInfo);

                sourceInfo ??= imageManager.ImagePathMap.Values.FirstOrDefault();
                defaultOutputDirectory = sourceInfo == null
                    ? string.Empty
                    : Path.GetDirectoryName(sourceInfo.Path) ?? string.Empty;
            }

            var dialog = new AugmentationDialog(
                defaultOutputDirectory,
                Properties.Settings.Default.AugmentationVariants,
                Properties.Settings.Default.AugmentationSeed,
                !string.IsNullOrEmpty(currentFile))
            {
                Owner = this
            };

            if (dialog.ShowDialog() != true || dialog.Options == null)
                return;

            var options = dialog.Options;

            Properties.Settings.Default.LastAugmentationDirectory = options.OutputDirectory;
            Properties.Settings.Default.AugmentationVariants = options.VariantsPerImage;
            Properties.Settings.Default.AugmentationSeed = options.Seed;
            Properties.Settings.Default.Save();

            await RunAugmentationAsync(options, currentFile);
        }

        private async Task RunAugmentationAsync(AugmentationOptions options, string currentFile)
        {
            var tokenSource = new CancellationTokenSource();
            overlayManager.ShowOverlayWithProgress(
                LanguageManager.Instance.GetString("Msg_Augmenting") ?? "Augmenting images...",
                tokenSource);

            try
            {
                var progress = CreateProgressReporter();
                var result = await augmentationManager.AugmentAsync(
                    options, imageManager, labelManager, currentFile, progress, tokenSource.Token);

                overlayManager.HideOverlay();

                string template = LanguageManager.Instance.GetString("Msg_AugmentationComplete")
                    ?? "Augmentation complete.\nSource images: {0}\nGenerated images: {1}\nSkipped: {2}";
                CustomMessageBox.Show(
                    string.Format(template, result.SourceImages, result.GeneratedImages, result.SkippedImages),
                    LanguageManager.Instance.GetString("Msg_ExportComplete") ?? "Export Complete",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (OperationCanceledException)
            {
                overlayManager.HideOverlay();
                CustomMessageBox.Show(
                    LanguageManager.Instance.GetString("Msg_ExportCanceled") ?? "Export canceled.",
                    LanguageManager.Instance.GetString("Msg_Canceled") ?? "Canceled",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                overlayManager.HideOverlay();
                CustomMessageBox.Show(
                    string.Format(LanguageManager.Instance.GetString("Msg_AugmentationFailed") ?? "Augmentation failed: {0}", ex.Message),
                    LanguageManager.Instance.GetString("Msg_Error") ?? "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task UpdateAllImageStatusesAsync()
        {
            var cancellationToken = new CancellationTokenSource();
            overlayManager.ShowOverlayWithProgress("Updating image statuses...", cancellationToken);

            try
            {
                var statusUpdates = new ConcurrentDictionary<string, ImageStatus>();
                var allFiles = imageManager.ImagePathMap.Keys.ToArray();
                int totalFiles = allFiles.Length;

                await Task.Run(() =>
                {
                    bool enableParallel = Properties.Settings.Default.EnableParallelProcessing;

                    if (enableParallel)
                    {
                        // Parallel processing for maximum speed
                        Parallel.ForEach(allFiles,
                            new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
                            fileName =>
                            {
                                if (cancellationToken.Token.IsCancellationRequested)
                                    return;

                                ImageStatus newStatus = DetermineImageStatus(fileName);

                                // Update caches
                                imageManager.UpdateImageStatusValue(fileName, newStatus);
                                statusUpdates[fileName] = newStatus;
                            });
                    }
                    else
                    {
                        foreach (var fileName in allFiles)
                        {
                            if (cancellationToken.Token.IsCancellationRequested)
                                break;

                            ImageStatus newStatus = DetermineImageStatus(fileName);
                            imageManager.UpdateImageStatusValue(fileName, newStatus);
                            statusUpdates[fileName] = newStatus;
                        }
                    }
                }, cancellationToken.Token);

                // Update UI in one batch
                await Dispatcher.InvokeAsync(() =>
                {
                    overlayManager.UpdateMessage($"Updating display... ({statusUpdates.Count} items)");

                    // Batch update all items
                    foreach (var kvp in statusUpdates)
                    {
                        if (uiStateManager.TryGetFromCache(kvp.Key, out var imageItem))
                        {
                            imageItem.Status = kvp.Value;
                        }
                    }

                    uiStateManager.UpdateStatusCounts();
                });
            }
            finally
            {
                overlayManager.HideOverlay();
            }
        }

        private void ClearAll_Click(object sender, RoutedEventArgs e)
        {
            imageManager.ClearAll();
            labelManager.ClearAll();
            drawingCanvas.Labels.Clear(); // Clear labels inside DrawingCanvas
            drawingCanvas.SuggestedLabels.Clear();
            ImageListBox.Items.Clear();
            LabelListBox.ItemsSource = null;
            drawingCanvas.Image = null; // Clear the image in DrawingCanvas
            drawingCanvas.InvalidateVisual(); // Force a redraw
            uiStateManager.UpdateStatusCounts();
            uiStateManager.RefreshAllImagesList(); // Clear the filter cache
            uiStateManager.ClearCache();
            UpdateSuggestionSummaryUI();

            MarkProjectDirty();
        }

        /// <summary>
        /// Project team pairings as (body, head) class-id tuples for the inference engine,
        /// or null when none are configured.
        /// </summary>
        private IReadOnlyList<(int BodyClassId, int HeadClassId)> GetClassTeamPairs()
        {
            var pairs = projectManager?.CurrentProject?.ClassTeamPairs;
            if (pairs == null || pairs.Count == 0)
                return null;

            return pairs
                .Where(pair => pair.BodyClassId >= 0 && pair.HeadClassId >= 0)
                .Select(pair => (pair.BodyClassId, pair.HeadClassId))
                .ToList();
        }

        private void ManageModels_Click(object sender, RoutedEventArgs e)
        {
            var savedMappings = projectManager?.CurrentProject?.ModelClassMappingSets;
            yoloAI.OpenModelManager(projectClasses, savedMappings);
        }

        private async void AILabelCurrentImage_Click(object sender, RoutedEventArgs e)
        {
            if (yoloAI.GetLoadedModelsCount() == 0)
            {
                var loadResult = CustomMessageBox.Show(
                    LanguageManager.Instance.GetString("Msg_NoModelsLoaded") ??
                    "No models loaded. Would you like to load models now?",
                    LanguageManager.Instance.GetString("Msg_NoModels") ?? "No Models",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (loadResult == MessageBoxResult.Yes)
                    ManageModels_Click(sender, e);

                return;
            }

            string currentFileName = GetCurrentFileName();
            if (string.IsNullOrEmpty(currentFileName) ||
                !imageManager.ImagePathMap.TryGetValue(currentFileName, out var imageEntry) ||
                !File.Exists(imageEntry.Path))
            {
                CustomMessageBox.Show(
                    LanguageManager.Instance.GetString("Main_NoImageSelected") ??
                    "No image selected.",
                    LanguageManager.Instance.GetString("Main_Error") ?? "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            // Thresholds are configured up-front in Settings > AI, so labeling runs immediately.
            Dictionary<int, float> confidenceThresholds =
                projectManager?.CurrentProject?.AIClassConfidenceThresholds ??
                new Dictionary<int, float>();
            var teamPairs = GetClassTeamPairs();

            labelManager.SaveLabels(currentFileName, drawingCanvas.Labels);
            overlayManager.ShowOverlay(
                LanguageManager.Instance.GetString("AIClassConfidence_Running") ??
                "Running AI detection on the current image...");

            try
            {
                List<(Rectangle box, int classId)> detections = await Task.Run(() =>
                {
                    using Bitmap image = new Bitmap(imageEntry.Path);
                    return yoloAI.RunInferenceWithClasses(image, confidenceThresholds, teamPairs);
                });

                if (Properties.Settings.Default.AIReplaceExistingLabels)
                    labelManager.ReplaceAILabels(currentFileName, detections);
                else
                    labelManager.AddAILabels(currentFileName, detections);

                drawingCanvas.Labels = labelManager.GetLabels(currentFileName);
                drawingCanvas.SelectedLabel = null;
                drawingCanvas.SelectedLabels.Clear();
                uiStateManager.RefreshLabelList();
                drawingCanvas.InvalidateVisual();
                OnLabelsChanged();

                CustomMessageBox.Show(
                    string.Format(
                        LanguageManager.Instance.GetString("AIClassConfidence_Complete") ??
                        "Current image labeling complete. Added {0} detection(s).",
                        detections.Count),
                    LanguageManager.Instance.GetString("Msg_AILabels") ?? "AI Labels",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Current image AI labeling failed: {ex}");
                CustomMessageBox.Show(
                    string.Format(
                        LanguageManager.Instance.GetString("AIClassConfidence_Failed") ??
                        "Failed to label the current image: {0}",
                        ex.Message),
                    LanguageManager.Instance.GetString("Main_Error") ?? "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                overlayManager.HideOverlay();
            }
        }

        private async void AutoLabelImages_Click(object sender, RoutedEventArgs e)
        {
            // Check if any models are loaded
            int modelCount = yoloAI.GetLoadedModelsCount();
            if (modelCount == 0)
            {
                var result = CustomMessageBox.Show(LanguageManager.Instance.GetString("Msg_NoModelsLoaded") ?? "No models loaded. Would you like to load models now?",
                    LanguageManager.Instance.GetString("Msg_NoModels") ?? "No Models", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    yoloAI.OpenModelManager();
                }
                return;
            }

            // Show ensemble info if multiple models
            string processingMode = modelCount == 1 ?
                (LanguageManager.Instance.GetString("Msg_SingleModel") ?? "single model") :
                string.Format(LanguageManager.Instance.GetString("Msg_EnsembleModels") ?? "ensemble ({0} models)", modelCount);
            processingMode = $"{processingMode}, {yoloAI.GetExecutionProviderSummary()}";

            if (!string.IsNullOrWhiteSpace(imageManager.CurrentImagePath))
                labelManager.SaveLabels(imageManager.CurrentImagePath, drawingCanvas.Labels);

            bool onlyUnlabeled = Properties.Settings.Default.AIAutoLabelOnlyUnlabeled;
            var imagesToProcess = imageManager.ImagePathMap
                .Where(pair => !onlyUnlabeled || labelManager.GetLabels(pair.Key).Count == 0)
                .Select(pair => (FileName: pair.Key, ImagePath: pair.Value.Path))
                .ToArray();
            int skippedLabeledImages = imageManager.ImagePathMap.Count - imagesToProcess.Length;

            if (imagesToProcess.Length == 0)
            {
                string emptyMessage = onlyUnlabeled
                    ? LanguageManager.Instance.GetString("Msg_NoUnlabeledImages") ??
                      "There are no unlabeled images to process."
                    : LanguageManager.Instance.GetString("Main_NoImageSelected") ??
                      "No images loaded.";
                CustomMessageBox.Show(
                    emptyMessage,
                    LanguageManager.Instance.GetString("Msg_AILabels") ?? "AI Labels",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            string scopeMessage = onlyUnlabeled
                ? "\n\n" + string.Format(
                    LanguageManager.Instance.GetString("Msg_OnlyUnlabeledSummary") ??
                    "Only unlabeled images will be processed; {0} labeled image(s) will be skipped.",
                    skippedLabeledImages)
                : "";
            var continueResult = CustomMessageBox.Show(
                string.Format(LanguageManager.Instance.GetString("Msg_ProcessImagesPrompt") ?? "Process {0} images using {1} detection?", imagesToProcess.Length, processingMode) +
                scopeMessage +
                (modelCount > 3 ? "\n\n" + (LanguageManager.Instance.GetString("Msg_ManyModelsWarning") ?? "Note: Processing with many models may take considerable time.") : ""),
                LanguageManager.Instance.GetString("Msg_StartDetection") ?? "Start Detection",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (continueResult != MessageBoxResult.Yes) return;

            // Store current selection
            var currentSelection = ImageListBox.SelectedItem as ImageListItem;
            var teamPairs = GetClassTeamPairs();

            CancellationTokenSource tokenSource = new CancellationTokenSource();
            overlayManager.ShowOverlayWithProgress($"Running AI Detections ({processingMode})...", tokenSource);

            int totalDetections = 0;
            int totalImages = imagesToProcess.Length;
            int completedImages = 0;
            int processedImages = 0;
            int failedImages = 0;
            // DirectML serializes the actual GPU Run call behind a single lock, so extra workers
            // do not run inference in parallel. Their value is keeping CPU-side preprocessing
            // (decode, resize, tensor build) far enough ahead that the GPU rarely waits for the
            // next image. A few workers per model input covers even the ensemble case; cap it so
            // memory (one decoded bitmap + float buffer per worker) stays bounded.
            int workerCount = yoloAI.UsesDirectMl
                ? Math.Min(totalImages, Math.Clamp(Environment.ProcessorCount / 2, 3, 6))
                : Math.Min(totalImages, Math.Clamp(Environment.ProcessorCount / 2, 2, 8));
            var parallelOptions = new ParallelOptions
            {
                CancellationToken = tokenSource.Token,
                MaxDegreeOfParallelism = Math.Max(1, workerCount)
            };

            try
            {
                await Parallel.ForEachAsync(imagesToProcess, parallelOptions, (imageEntry, cancellationToken) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        if (!File.Exists(imageEntry.ImagePath))
                        {
                            Interlocked.Increment(ref failedImages);
                            return ValueTask.CompletedTask;
                        }

                        using Bitmap image = new Bitmap(imageEntry.ImagePath);

                        var boxesWithClasses = yoloAI.RunInferenceWithClasses(image, null, teamPairs);
                        labelManager.AddAILabels(imageEntry.FileName, boxesWithClasses);
                        Interlocked.Add(ref totalDetections, boxesWithClasses.Count);
                        Interlocked.Increment(ref processedImages);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"AI labeling failed for {imageEntry.FileName}: {ex.Message}");
                        Interlocked.Increment(ref failedImages);
                    }
                    finally
                    {
                        int completed = Interlocked.Increment(ref completedImages);
                        _ = Dispatcher.BeginInvoke(() =>
                        {
                            overlayManager.UpdateProgress((completed * 100) / totalImages);
                            overlayManager.UpdateMessage(
                                $"Processing image {completed}/{totalImages} ({processingMode}, {workerCount} workers)...");
                        });
                    }

                    return ValueTask.CompletedTask;
                });
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("AI auto-labeling was cancelled.");
            }
            finally
            {
                overlayManager.HideOverlay();
                tokenSource.Dispose();
            }

            string currentFileName = GetCurrentFileName();
            if (!string.IsNullOrEmpty(currentFileName) &&
                imagesToProcess.Any(item => string.Equals(
                    item.FileName,
                    currentFileName,
                    StringComparison.OrdinalIgnoreCase)))
            {
                drawingCanvas.Labels = labelManager.GetLabels(currentFileName);
                uiStateManager.RefreshLabelList();
                drawingCanvas.InvalidateVisual();
            }

            await UpdateAllImageStatusesAsync();

            Dispatcher.Invoke(() =>
            {
                // Restore selection if it was lost
                if (ImageListBox.SelectedItem == null)
                {
                    RestoreImageSelection(currentSelection);
                }

                OnLabelsChanged();

                string modeInfo = modelCount == 1 ? "" : string.Format(LanguageManager.Instance.GetString("Msg_UsingModels") ?? " using {0} models", modelCount);
                string completionMessage = string.Format(
                    LanguageManager.Instance.GetString("Msg_AutoLabelComplete") ??
                    "Auto-labeling complete{0}.\nTotal detections: {1}",
                    modeInfo,
                    totalDetections);
                if (onlyUnlabeled)
                {
                    completionMessage += "\n" + string.Format(
                        LanguageManager.Instance.GetString("Msg_AutoLabelScopeComplete") ??
                        "Processed: {0}; skipped labeled: {1}.",
                        processedImages,
                        skippedLabeledImages);
                }
                if (failedImages > 0)
                {
                    completionMessage += "\n" + string.Format(
                        LanguageManager.Instance.GetString("Msg_AutoLabelFailedImages") ??
                        "{0} image(s) failed or were missing.",
                        failedImages);
                }

                CustomMessageBox.Show(completionMessage,
                    LanguageManager.Instance.GetString("Msg_AILabels") ?? "AI Labels", MessageBoxButton.OK, MessageBoxImage.Information);

                if (processedImages > 0)
                    MarkProjectDirty();
            });
        }

        private void AutoSuggestLabels_Click(object sender, RoutedEventArgs e)
        {
            // Allow suggestions without a project - works like auto-labeling with in-memory storage
            if (imageManager.ImagePathMap.Count == 0)
            {
                CustomMessageBox.Show(
                    LanguageManager.Instance.GetString("Main_NoImageSelected") ?? "No images loaded.",
                    LanguageManager.Instance.GetString("Main_Error") ?? "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var dialog = new PropagationDialog { Owner = this };
            if (dialog.ShowDialog() != true)
                return;

            // Create a fresh PropagationManager for each run to ensure clean state
            propagationManager = new PropagationManager(labelManager, imageManager);
            if (projectManager?.IsProjectOpen == true && !string.IsNullOrEmpty(projectManager.CurrentProject?.ProjectFolder))
            {
                propagationManager.SetProjectFolder(projectManager.CurrentProject.ProjectFolder);
            }

            _ = RunPropagationAsync(dialog);
        }

        private async Task RunPropagationAsync(PropagationDialog dialog)
        {
            try
            {
                await RunPropagationInternalAsync(dialog);
            }
            finally
            {
                // Clear reference and force GC to release GDI+ resources
                propagationManager = null;
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }

        private async Task RunPropagationInternalAsync(PropagationDialog dialog)
        {
            if (!Properties.Settings.Default.EnablePropagation)
            {
                CustomMessageBox.Show(
                    LanguageManager.Instance.GetString("Propagation_Disabled") ?? "Propagation is disabled in settings.",
                    LanguageManager.Instance.GetString("Main_Error") ?? "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            string currentFile = GetCurrentFileName();
            if (dialog.Scope == PropagationScope.CurrentImage && string.IsNullOrEmpty(currentFile))
            {
                CustomMessageBox.Show(
                    LanguageManager.Instance.GetString("Main_NoImageSelected") ?? "No image selected.",
                    LanguageManager.Instance.GetString("Main_Error") ?? "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var allFiles = imageManager.ImagePathMap.Keys.ToList();
            var sourceFiles = new List<string>();

            if (dialog.Scope == PropagationScope.CurrentImage)
            {
                sourceFiles.Add(currentFile);
            }
            else
            {
                sourceFiles = labelManager.LabelStorage
                    .Where(kvp => kvp.Value != null && kvp.Value.Count > 0)
                    .Select(kvp => kvp.Key)
                    .ToList();
            }

            if (sourceFiles.Count == 0)
            {
                CustomMessageBox.Show(
                    LanguageManager.Instance.GetString("Propagation_NoSources") ?? "No labeled images found to use as sources.",
                    LanguageManager.Instance.GetString("Main_Error") ?? "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var tokenSource = new CancellationTokenSource();
            overlayManager.ShowOverlayWithProgress(
                LanguageManager.Instance.GetString("Propagation_Running") ?? "Running propagation...",
                tokenSource);

            bool runImageSimilarity = dialog.RunImageSimilarity;
            bool runObjectSimilarity = dialog.RunObjectSimilarity;
            bool runTracking = dialog.RunTracking;
            bool autoAccept = dialog.AutoAccept;

            var summary = new PropagationSummary();
            var progressTimer = Stopwatch.StartNew();
            string lastPhase = null;
            string runningMessage = LanguageManager.Instance.GetString("Propagation_Running") ?? "Running propagation...";
            string imageMessage = LanguageManager.Instance.GetString("Propagation_Running_Image") ?? "Running image similarity...";
            string objectMessage = LanguageManager.Instance.GetString("Propagation_Running_Object") ?? "Running object similarity...";
            string trackingMessage = LanguageManager.Instance.GetString("Propagation_Running_Tracking") ?? "Running tracking...";
            var progressReporter = new Progress<(string phase, int current, int total)>(progress =>
            {
                if (progress.total <= 0)
                    return;

                bool phaseChanged = !string.Equals(lastPhase, progress.phase, StringComparison.Ordinal);
                bool isComplete = progress.current >= progress.total;
                if (!phaseChanged && !isComplete && progressTimer.ElapsedMilliseconds < 250)
                    return;

                lastPhase = progress.phase;
                progressTimer.Restart();

                string baseMessage = progress.phase switch
                {
                    PropagationManager.PhaseImageSimilarity => imageMessage,
                    PropagationManager.PhaseObjectRanking => objectMessage,
                    PropagationManager.PhaseObjectMatching => objectMessage,
                    PropagationManager.PhaseTracking => trackingMessage,
                    _ => runningMessage
                };

                string message = $"{baseMessage} {progress.current:n0}/{progress.total:n0}";
                overlayManager.UpdateMessage(message);
                int percent = (int)Math.Max(0, Math.Min(100, (progress.current * 100.0) / progress.total));
                overlayManager.UpdateProgress(percent);
            });

            var orderedFiles = ImageListBox.Items.Cast<ImageListItem>().Select(i => i.FileName).ToList();

            try
            {
                await Task.Run(() =>
                {
                    double imageSimilarityThreshold = Properties.Settings.Default.ImageSimilarityThreshold;
                    double objectSimilarityThreshold = Properties.Settings.Default.ObjectSimilarityThreshold;
                    double trackingThreshold = Properties.Settings.Default.TrackingConfidenceThreshold;
                    bool skipLabeled = Properties.Settings.Default.PropagationSkipLabeled;
                    bool restrictToSimilar = Properties.Settings.Default.PropagationUseClusterFilter;
                    int maxSuggestionsPerImage = Properties.Settings.Default.PropagationMaxSuggestionsPerImage;
                    int minBoxSize = Properties.Settings.Default.PropagationMinBoxSize;
                    int trackingWindow = Properties.Settings.Default.PropagationTrackingFrameWindow;
                    int candidateLimit = Properties.Settings.Default.PropagationObjectCandidateLimit;
                    int searchStride = Properties.Settings.Default.PropagationSearchStride;
                    double mergeIoUThreshold = Properties.Settings.Default.EnsembleIoUThreshold;

                    if (runImageSimilarity)
                    {
                        overlayManager.UpdateMessage(imageMessage);
                        var result = propagationManager.RunImageSimilarity(
                            sourceFiles,
                            allFiles,
                            imageSimilarityThreshold,
                            autoAccept,
                            skipLabeled,
                            maxSuggestionsPerImage,
                            mergeIoUThreshold,
                        progressReporter,
                        tokenSource.Token);
                        summary.SuggestionsAdded += result.SuggestionsAdded;
                        summary.LabelsAdded += result.LabelsAdded;
                        summary.ImagesAffected += result.ImagesAffected;
                    }

                    if (runObjectSimilarity)
                    {
                        overlayManager.UpdateMessage(objectMessage);
                        var result = propagationManager.RunObjectSimilarity(
                            sourceFiles,
                            allFiles,
                            objectSimilarityThreshold,
                            autoAccept,
                            skipLabeled,
                            restrictToSimilar,
                            imageSimilarityThreshold,
                            maxSuggestionsPerImage,
                            minBoxSize,
                            candidateLimit,
                            searchStride,
                            mergeIoUThreshold,
                        progressReporter,
                        tokenSource.Token);
                        summary.SuggestionsAdded += result.SuggestionsAdded;
                        summary.LabelsAdded += result.LabelsAdded;
                        summary.ImagesAffected += result.ImagesAffected;
                    }

                    if (runTracking && !string.IsNullOrEmpty(currentFile))
                    {
                        overlayManager.UpdateMessage(trackingMessage);
                        var result = propagationManager.RunTracking(
                            currentFile,
                            orderedFiles,
                            trackingWindow,
                            trackingThreshold,
                            autoAccept,
                            skipLabeled,
                            maxSuggestionsPerImage,
                            minBoxSize,
                            searchStride,
                            mergeIoUThreshold,
                        progressReporter,
                        tokenSource.Token);
                        summary.SuggestionsAdded += result.SuggestionsAdded;
                        summary.LabelsAdded += result.LabelsAdded;
                        summary.ImagesAffected += result.ImagesAffected;
                    }
                }, tokenSource.Token);
            }
            catch (Exception ex)
            {
                overlayManager.HideOverlay();
                CustomMessageBox.Show(
                    $"{LanguageManager.Instance.GetString("Main_Error") ?? "Error"}\n{ex.Message}",
                    LanguageManager.Instance.GetString("Main_Error") ?? "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            overlayManager.UpdateProgress(100);
            overlayManager.HideOverlay();

            if (tokenSource.IsCancellationRequested)
            {
                // Check if any suggestions were added before the cancel
                bool hasPartialResults = summary.SuggestionsAdded > 0 || summary.LabelsAdded > 0;

                if (hasPartialResults)
                {
                    var keepResult = CustomMessageBox.Show(
                        string.Format(LanguageManager.Instance.GetString("Msg_PropagationPartialResults") ?? "Propagation was canceled, but some results were generated.\n\nSuggestions: {0}\nLabels: {1}\nImages affected: {2}\n\nDo you want to keep these partial results?",
                            summary.SuggestionsAdded, summary.LabelsAdded, summary.ImagesAffected),
                        LanguageManager.Instance.GetString("Msg_KeepPartialResults") ?? "Keep Partial Results?",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (keepResult == MessageBoxResult.No)
                    {
                        // Clear all suggestions if user doesn't want partial results
                        int cleared = labelManager.ClearAllSuggestions();
                        if (cleared > 0)
                        {
                            await UpdateAllImageStatusesAsync();
                            if (!string.IsNullOrEmpty(currentFile))
                            {
                                drawingCanvas.SuggestedLabels = labelManager.GetSuggestions(currentFile);
                            }
                            uiStateManager.RefreshLabelList();
                            uiStateManager.UpdateStatusCounts();
                            UpdateSuggestionSummaryUI();
                            drawingCanvas.InvalidateVisual();
                        }

                        CustomMessageBox.Show(
                            LanguageManager.Instance.GetString("Propagation_Canceled") ?? "Propagation canceled.",
                            LanguageManager.Instance.GetString("Main_Information") ?? "Information",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                        return;
                    }
                    // Fall through to update UI with partial results
                }
                else
                {
                    CustomMessageBox.Show(
                        LanguageManager.Instance.GetString("Propagation_Canceled") ?? "Propagation canceled.",
                        LanguageManager.Instance.GetString("Main_Information") ?? "Information",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }
            }
            await UpdateAllImageStatusesAsync();

            if (!string.IsNullOrEmpty(currentFile))
            {
                drawingCanvas.SuggestedLabels = labelManager.GetSuggestions(currentFile);
            }

            uiStateManager.RefreshLabelList();
            uiStateManager.UpdateStatusCounts();
            UpdateSuggestionSummaryUI();
            drawingCanvas.InvalidateVisual();

            string message = string.Format(
                LanguageManager.Instance.GetString("Propagation_Complete") ?? "Propagation complete.\nSuggestions: {0}\nLabels: {1}\nImages affected: {2}",
                summary.SuggestionsAdded,
                summary.LabelsAdded,
                summary.ImagesAffected);
            CustomMessageBox.Show(message, LanguageManager.Instance.GetString("Propagation_Title") ?? "Propagation",
                MessageBoxButton.OK, MessageBoxImage.Information);

            MarkProjectDirty();
        }

        private void DarkTheme_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem)
            {
                ThemeManager.Current.ApplicationTheme = menuItem.IsChecked ? ApplicationTheme.Dark : ApplicationTheme.Light;

                // Save setting
                Properties.Settings.Default.DarkTheme = menuItem.IsChecked;
                Properties.Settings.Default.Save();
            }
        }

        public Task LoadImagesAsync(string directoryPath)
        {
            if (!Directory.Exists(directoryPath))
                return Task.CompletedTask;

            return LoadImagesInternalAsync(directoryPath);
        }

        private async Task LoadImagesInternalAsync(string directoryPath)
        {
            var tokenSource = new CancellationTokenSource();
            overlayManager.ShowOverlayWithProgress("Loading images...", tokenSource);

            try
            {
                // Progress reporter
                var progress = CreateProgressReporter();

                // Load images asynchronously
                await imageManager.LoadImagesFromDirectoryAsync(
                    directoryPath,
                    progress,
                    tokenSource.Token,
                    Properties.Settings.Default.EnableParallelProcessing);

                // Update UI in batches for better performance with large sets
                var loadedFiles = imageManager.ImagePathMap.Values.Select(img => img.Path).ToArray();
                await UpdateImageListInBatchesAsync(loadedFiles, tokenSource.Token);

                // Warn about duplicate file names that were skipped
                ShowDuplicateImagesWarning(imageManager.ConsumeDuplicateImageFiles());

                if (ImageListBox.Items.Count > 0 && ImageListBox.SelectedItem == null)
                {
                    ImageListBox.SelectedIndex = 0;
                }

                // Build the cache for O(1) lookups when updating statuses
                uiStateManager.BuildCache(ImageListBox.Items);
                uiStateManager.UpdateStatusCounts();
                uiStateManager.RefreshAllImagesList(); // Refresh the complete list for filtering

                MarkProjectDirty();
            }
            catch (OperationCanceledException)
            {
                CustomMessageBox.Show(LanguageManager.Instance.GetString("Msg_ImageLoadCanceled") ?? "Image loading was canceled.", LanguageManager.Instance.GetString("Msg_Canceled") ?? "Canceled", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show(string.Format(LanguageManager.Instance.GetString("Msg_ErrorLoadingImages") ?? "Error loading images: {0}", ex.Message), LanguageManager.Instance.GetString("Msg_Error") ?? "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                overlayManager.HideOverlay();
            }
        }

        private async Task UpdateImageListInBatchesAsync(string[] files, CancellationToken cancellationToken)
        {
            int batchSize = Properties.Settings.Default.UIBatchSize; // Use setting
            if (batchSize <= 0)
            {
                batchSize = 100;
            }

            // Folder imports are incremental. Keep existing UI items and only append
            // filenames that are not already represented in the list.
            var existingFileNames = await Dispatcher.InvokeAsync(() =>
                ImageListBox.Items
                    .OfType<ImageListItem>()
                    .Select(item => item.FileName)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase));

            var filesToAdd = new List<string>();
            foreach (var file in files)
            {
                string fileName = Path.GetFileName(file);
                if (existingFileNames.Add(fileName))
                    filesToAdd.Add(file);
            }

            for (int i = 0; i < filesToAdd.Count; i += batchSize)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var batch = filesToAdd.Skip(i).Take(batchSize).ToArray();

                await Dispatcher.InvokeAsync(() =>
                {
                    // Temporarily disable UI updates for better performance
                    ImageListBox.SelectionChanged -= ImageListBox_SelectionChanged;

                    foreach (string file in batch)
                    {
                        string fileName = Path.GetFileName(file);
                        ImageListBox.Items.Add(new ImageListItem(fileName, imageManager.GetImageStatus(fileName)));
                    }

                    ImageListBox.SelectionChanged += ImageListBox_SelectionChanged;
                });

                // Update progress
                overlayManager.UpdateMessage($"Updating UI... {Math.Min(i + batchSize, filesToAdd.Count)}/{filesToAdd.Count}");
            }
        }

        private async Task LoadYOLOLabelsFromDirectory(string directoryPath)
        {
            if (!Directory.Exists(directoryPath)) return;

            // Check if images are loaded first (REQUIRED for cached dimensions)
            if (imageManager.ImagePathMap.Count == 0)
            {
                CustomMessageBox.Show(LanguageManager.Instance.GetString("Msg_LoadImagesFirst") ?? "Please load images before importing labels.\n\nImages must be loaded first so label dimensions can be calculated correctly.",
                    LanguageManager.Instance.GetString("Msg_LoadImagesFirstTitle") ?? "Load Images First", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Store current selection
            var currentSelection = ImageListBox.SelectedItem as ImageListItem;

            string[] labelFiles = Directory.GetFiles(directoryPath, "*.txt");
            int totalFiles = labelFiles.Length;

            if (totalFiles == 0)
            {
                CustomMessageBox.Show(LanguageManager.Instance.GetString("Msg_NoLabelFilesFound") ?? "No YOLO label files found in the selected directory.", LanguageManager.Instance.GetString("Msg_LabelImport") ?? "Label Import", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            CancellationTokenSource tokenSource = new CancellationTokenSource();
            overlayManager.ShowOverlayWithProgress("Importing YOLO labels...", tokenSource);

            try
            {
                // Progress reporter for real-time feedback
                var progress = CreateProgressReporter();

                // Use batch loading with parallel processing
                var (labelsLoaded, foundClassIds) = await labelManager.LoadYoloLabelsBatchAsync(
                    directoryPath,
                    imageManager,
                    progress,
                    tokenSource.Token,
                    Properties.Settings.Default.EnableParallelProcessing
                );

                // Auto-create missing classes from imported labels
                if (foundClassIds != null && foundClassIds.Count > 0)
                {
                    var existingClassIds = new HashSet<int>(projectClasses.Select(c => c.ClassId));
                    var missingClassIds = foundClassIds.Where(id => !existingClassIds.Contains(id) && id >= 0).OrderBy(id => id).ToList();

                    if (missingClassIds.Count > 0)
                    {
                        // Generate colors for new classes
                        var colors = new[] { "#E57373", "#64B5F6", "#81C784", "#FFB74D", "#BA68C8", "#4DB6AC", "#F06292", "#90A4AE" };
                        int colorIndex = 0;

                        foreach (var classId in missingClassIds)
                        {
                            // Use the original class ID from the label file
                            int newClassId = classId;

                            // Create new class with default name
                            string className = $"class_{newClassId}";
                            string colorHex = colors[colorIndex % colors.Length];
                            colorIndex++;

                            var newClass = new LabelClass(className, colorHex, newClassId);
                            projectClasses.Add(newClass);
                            existingClassIds.Add(newClassId);
                        }

                        // Update label manager's valid class IDs to include newly created classes
                        labelManager.SetValidClassIds(projectClasses.Select(c => c.ClassId));

                        // Update project data
                        if (projectManager?.CurrentProject != null)
                        {
                            projectManager.CurrentProject.Classes = projectClasses;
                        }

                        // Refresh UI
                        RefreshClassList();
                        MarkProjectDirty();
                    }
                }

                overlayManager.HideOverlay();

                // Update all image statuses after importing labels
                await UpdateAllImageStatusesAsync();
                uiStateManager.RefreshAllImagesList(); // Refresh for filtering

                if (!string.IsNullOrEmpty(imageManager.CurrentImagePath) && labelManager.LabelStorage.ContainsKey(imageManager.CurrentImagePath))
                {
                    drawingCanvas.Labels = labelManager.GetLabels(imageManager.CurrentImagePath);
                    uiStateManager.RefreshLabelList();

                    // Restore selection if it was lost
                    if (ImageListBox.SelectedItem == null)
                    {
                        RestoreImageSelection(currentSelection);
                    }

                    OnLabelsChanged();
                    drawingCanvas.InvalidateVisual();
                }

                MarkProjectDirty();
            }
            catch (OperationCanceledException)
            {
                overlayManager.HideOverlay();
                CustomMessageBox.Show(LanguageManager.Instance.GetString("Msg_LabelImportCanceled") ?? "Label import was cancelled by user.", LanguageManager.Instance.GetString("Msg_ImportCanceled") ?? "Import Cancelled", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                overlayManager.HideOverlay();
                CustomMessageBox.Show(string.Format(LanguageManager.Instance.GetString("Msg_ErrorImportingLabels") ?? "Error importing labels: {0}", ex.Message), LanguageManager.Instance.GetString("Msg_ImportError") ?? "Import Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task ExportLabelsToYoloAsync(string exportDirectory)
        {
            if (string.IsNullOrEmpty(exportDirectory)) return;

            var tokenSource = new CancellationTokenSource();
            overlayManager.ShowOverlayWithProgress("Exporting labels...", tokenSource);

            try
            {
                var progress = CreateProgressReporter();

                await labelManager.ExportLabelsBatchAsync(
                    exportDirectory,
                    imageManager,
                    progress,
                    tokenSource.Token);

                // Export class names file (classes.txt) so training keeps the class names.
                // YOLO label .txt files only store numeric class ids; without this file the
                // class names are lost.
                ExportClassesFile(exportDirectory);

                overlayManager.HideOverlay();
                CustomMessageBox.Show(LanguageManager.Instance.GetString("Msg_LabelsExported") ?? "Labels exported successfully!", LanguageManager.Instance.GetString("Msg_ExportComplete") ?? "Export Complete",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (OperationCanceledException)
            {
                overlayManager.HideOverlay();
                CustomMessageBox.Show(LanguageManager.Instance.GetString("Msg_ExportCanceled") ?? "Export canceled.", LanguageManager.Instance.GetString("Msg_Canceled") ?? "Canceled",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                overlayManager.HideOverlay();
                CustomMessageBox.Show(string.Format(LanguageManager.Instance.GetString("Msg_ExportFailed") ?? "Export failed: {0}", ex.Message), LanguageManager.Instance.GetString("Msg_Error") ?? "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Writes the project's class names to disk alongside the YOLO labels.
        /// Produces classes.txt (one name per ClassId line, indexed so line N == class N)
        /// and a data.yaml for direct use in YOLO training.
        /// </summary>
        private void ExportClassesFile(string exportDirectory)
        {
            try
            {
                // Shared with the training-dataset export so both produce identical class ordering.
                // Skips the "nan"/ -1 placeholder and fills id gaps with class_N.
                var names = DatasetExportManager.BuildClassNames(projectClasses);

                if (names.Length == 0)
                    return;

                // classes.txt
                System.IO.File.WriteAllLines(
                    System.IO.Path.Combine(exportDirectory, "classes.txt"),
                    names);

                // data.yaml (YOLO format)
                var yaml = new System.Text.StringBuilder();
                yaml.AppendLine($"nc: {names.Length}");
                yaml.Append("names: [");
                yaml.Append(string.Join(", ", names.Select(n => $"'{n.Replace("'", "''")}'")));
                yaml.AppendLine("]");
                System.IO.File.WriteAllText(
                    System.IO.Path.Combine(exportDirectory, "data.yaml"),
                    yaml.ToString());
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to export classes file: {ex.Message}");
            }
        }

        private void RestoreImageSelection(ImageListItem previousSelection)
        {
            if (previousSelection == null) return;

            if (uiStateManager.TryGetFromCache(previousSelection.FileName, out var cachedItem))
            {
                int index = ImageListBox.Items.IndexOf(cachedItem);
                if (index >= 0)
                {
                    ImageListBox.SelectedIndex = index;
                    ImageListBox.ScrollIntoView(cachedItem);
                }
            }
        }
        private ImageStatus DetermineImageStatus(string fileName)
        {
            if (labelManager.SuggestionStorage.TryGetValue(fileName, out var suggestions) && suggestions.Count > 0)
                return ImageStatus.Suggested;

            if (!labelManager.LabelStorage.TryGetValue(fileName, out var labels) || labels.Count == 0)
                return ImageStatus.NoLabel;

            // Imported/AI labels at or before the saved manual checkpoint have already
            // been reviewed by the user and must stay verified after another import.
            if (IsAtOrBeforeManualProgress(fileName))
                return ImageStatus.Verified;

            //if we are working on the image, don't mark it as review
            string currentFile = GetCurrentFileName();
            bool isCurrentImage = string.Equals(fileName, currentFile, StringComparison.OrdinalIgnoreCase);
            
            // Fast check for imported/AI labels
            for (int i = 0; i < labels.Count; i++)
            {
                var name = labels[i].Name;
                if (name.Length > 0 && (name[0] == 'I' || name[0] == 'A'))
                {
                    if (name.StartsWith("Imported") || name.StartsWith("AI"))
                    {
                        // if it is the one we are working on, don't mark it as review
                        if (isCurrentImage)
                        {
                            return ImageStatus.Verified;
                        }
                        return ImageStatus.VerificationNeeded;
                    }
                }
            }

            return ImageStatus.Verified;
        }

        public bool IsAtOrBeforeManualProgress(string fileName)
        {
            string? checkpoint = projectManager?.CurrentProject?.ManualProgressImageFile;
            return !string.IsNullOrWhiteSpace(checkpoint) &&
                   StringComparer.OrdinalIgnoreCase.Compare(fileName, checkpoint) <= 0;
        }

        private async void SetManualProgress_Click(object sender, RoutedEventArgs e)
        {
            if (projectManager?.CurrentProject == null)
            {
                CustomMessageBox.Show(
                    LanguageManager.Instance.GetString("Msg_ManualProgress_ProjectRequired") ?? "Open or create a project before setting a manual progress point.",
                    LanguageManager.Instance.GetString("Msg_ManualProgress_Title") ?? "Manual Labeling Progress",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (ImageListBox.SelectedItem is not ImageListItem selected)
            {
                CustomMessageBox.Show(
                    LanguageManager.Instance.GetString("Msg_ManualProgress_SelectImage") ?? "Select an image first.",
                    LanguageManager.Instance.GetString("Msg_ManualProgress_Title") ?? "Manual Labeling Progress",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            projectManager.CurrentProject.ManualProgressImageFile = selected.FileName;
            await UpdateAllImageStatusesAsync();
            uiStateManager.RefreshAllImagesList();
            MarkProjectDirty();

            CustomMessageBox.Show(
                string.Format(LanguageManager.Instance.GetString("Msg_ManualProgress_Set") ?? "Manual progress point set to: {0}", selected.FileName),
                LanguageManager.Instance.GetString("Msg_ManualProgress_Title") ?? "Manual Labeling Progress",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void JumpToManualProgress_Click(object sender, RoutedEventArgs e)
        {
            string? checkpoint = projectManager?.CurrentProject?.ManualProgressImageFile;
            if (string.IsNullOrWhiteSpace(checkpoint))
            {
                CustomMessageBox.Show(
                    LanguageManager.Instance.GetString("Msg_ManualProgress_NotSet") ?? "No manual progress point has been set yet.",
                    LanguageManager.Instance.GetString("Msg_ManualProgress_Title") ?? "Manual Labeling Progress",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Ensure the target is visible even when a status filter is active.
            FilterAll_Click(sender, e);
            var target = ImageListBox.Items.Cast<ImageListItem>()
                .FirstOrDefault(item => string.Equals(item.FileName, checkpoint, StringComparison.OrdinalIgnoreCase));

            if (target == null)
            {
                CustomMessageBox.Show(
                    string.Format(LanguageManager.Instance.GetString("Msg_ManualProgress_Missing") ?? "The progress image is not currently loaded: {0}", checkpoint),
                    LanguageManager.Instance.GetString("Msg_ManualProgress_Title") ?? "Manual Labeling Progress",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ImageListBox.SelectedItem = target;
            ImageListBox.ScrollIntoView(target);
            ImageListBox.Focus();
        }

        private void UpdateImageStatus(string fileName)
        {
            if (!imageManager.ImagePathMap.ContainsKey(fileName)) return;

            ImageStatus newStatus = DetermineImageStatus(fileName);

            imageManager.UpdateImageStatusValue(fileName, newStatus);

            // Force UI update on the dispatcher thread with high priority
            Dispatcher.Invoke(() =>
            {
                // Use O(1) dictionary lookup instead of O(n) iteration
                if (uiStateManager.TryGetFromCache(fileName, out var imageItem))
                {
                    // Update the status - this triggers PropertyChanged
                    imageItem.Status = newStatus;
                    
                    // Force the ListBox to refresh the specific item's visual
                    // This is needed because of VirtualizingStackPanel recycling
                    var container = ImageListBox.ItemContainerGenerator.ContainerFromItem(imageItem) as ListBoxItem;
                    if (container != null)
                    {
                        container.UpdateLayout();
                    }
                    else
                    {
                        // If container is null (item not visible/virtualized), force refresh of the entire list
                        ImageListBox.Items.Refresh();
                    }
                }
            }, System.Windows.Threading.DispatcherPriority.Render);
        }

        private void LoadHotkeys()
        {
            if (hotkeyManager == null) return;

            hotkeyManager.RegisterHotkey("SaveProject", Properties.Settings.Default.Hotkey_SaveProject ?? "Ctrl + S");
            hotkeyManager.RegisterHotkey("PreviousImage", Properties.Settings.Default.Hotkey_PreviousImage ?? "A");
            hotkeyManager.RegisterHotkey("NextImage", Properties.Settings.Default.Hotkey_NextImage ?? "D");
            hotkeyManager.RegisterHotkey("MoveLabelUp", Properties.Settings.Default.Hotkey_MoveLabelUp ?? "Up");
            hotkeyManager.RegisterHotkey("MoveLabelDown", Properties.Settings.Default.Hotkey_MoveLabelDown ?? "Down");
            hotkeyManager.RegisterHotkey("MoveLabelLeft", Properties.Settings.Default.Hotkey_MoveLabelLeft ?? "Left");
            hotkeyManager.RegisterHotkey("MoveLabelRight", Properties.Settings.Default.Hotkey_MoveLabelRight ?? "Right");
        }

        private async void Window_KeyDown(object sender, KeyEventArgs e)
        {
            // KeyDown is handled in OnPreviewKeyDown for better event routing
            OnPreviewKeyDown(e);
        }

        private bool IsInputControlFocused()
        {
            // Check if any input control has focus
            var focusedElement = Keyboard.FocusedElement;
            return focusedElement is System.Windows.Controls.TextBox ||
                   focusedElement is System.Windows.Controls.RichTextBox ||
                   focusedElement is System.Windows.Controls.PasswordBox ||
                   focusedElement is System.Windows.Controls.ComboBox;
        }

        private void NavigateToPreviousImage()
        {
            if (ImageListBox.Items.Count == 0) return;

            int currentIndex = ImageListBox.SelectedIndex;
            if (currentIndex > 0)
            {
                ImageListBox.SelectedIndex = currentIndex - 1;
                ImageListBox.ScrollIntoView(ImageListBox.SelectedItem);
            }
        }

        private void NavigateToNextImage()
        {
            if (ImageListBox.Items.Count == 0) return;

            int currentIndex = ImageListBox.SelectedIndex;
            if (currentIndex < ImageListBox.Items.Count - 1)
            {
                ImageListBox.SelectedIndex = currentIndex + 1;
                ImageListBox.ScrollIntoView(ImageListBox.SelectedItem);
            }
        }

        private void MoveSelectedLabel(double deltaX, double deltaY)
        {
            if (drawingCanvas.SelectedLabel != null)
            {
                var rect = drawingCanvas.SelectedLabel.Rect;
                drawingCanvas.SelectedLabel.Rect = new Rect(
                    rect.X + deltaX,
                    rect.Y + deltaY,
                    rect.Width,
                    rect.Height
                );

                // Also move all selected labels if multi-selection
                if (drawingCanvas.SelectedLabels.Count > 1)
                {
                    foreach (var label in drawingCanvas.SelectedLabels)
                    {
                        if (label != drawingCanvas.SelectedLabel)
                        {
                            var labelRect = label.Rect;
                            label.Rect = new Rect(
                                labelRect.X + deltaX,
                                labelRect.Y + deltaY,
                                labelRect.Width,
                                labelRect.Height
                            );
                        }
                    }
                }
            }
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);

            // Handle custom hotkeys first (before other handlers)
            if (hotkeyManager != null && !IsInputControlFocused())
            {
                // Save Project (always available, but check if input control is focused)
                if (hotkeyManager.IsHotkeyPressed("SaveProject", e))
                {
                    SaveProject_Click(this, e);
                    e.Handled = true;
                    return;
                }

                // Image Navigation (only when canvas doesn't have focus or when not editing)
                if (!drawingCanvas.IsFocused || (drawingCanvas.SelectedLabel == null && drawingCanvas.SelectedLabels.Count == 0))
                {
                    if (hotkeyManager.IsHotkeyPressed("PreviousImage", e))
                    {
                        NavigateToPreviousImage();
                        e.Handled = true;
                        return;
                    }

                    if (hotkeyManager.IsHotkeyPressed("NextImage", e))
                    {
                        NavigateToNextImage();
                        e.Handled = true;
                        return;
                    }
                }

                // Label Movement (when label is selected, allow custom hotkeys even if canvas has focus)
                if (drawingCanvas.SelectedLabel != null || drawingCanvas.SelectedLabels.Count > 0)
                {
                    int moveAmount = 1;
                    bool moved = false;

                    // Check if any custom label movement hotkey is pressed
                    if (hotkeyManager.IsHotkeyPressed("MoveLabelUp", e))
                    {
                        MoveSelectedLabel(0, -moveAmount);
                        moved = true;
                    }
                    else if (hotkeyManager.IsHotkeyPressed("MoveLabelDown", e))
                    {
                        MoveSelectedLabel(0, moveAmount);
                        moved = true;
                    }
                    else if (hotkeyManager.IsHotkeyPressed("MoveLabelLeft", e))
                    {
                        MoveSelectedLabel(-moveAmount, 0);
                        moved = true;
                    }
                    else if (hotkeyManager.IsHotkeyPressed("MoveLabelRight", e))
                    {
                        MoveSelectedLabel(moveAmount, 0);
                        moved = true;
                    }

                    if (moved)
                    {
                        e.Handled = true;
                        drawingCanvas.InvalidateVisual();
                        MarkProjectDirty();
                        return;
                    }
                }
            }

            // If DrawingCanvas has focus and Ctrl is pressed, let it handle the shortcuts
            // But only if the key is not a custom label movement hotkey
            if (drawingCanvas.IsFocused && (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl)))
            {
                // Check if this is a custom label movement hotkey
                bool isLabelMovementHotkey = hotkeyManager != null && drawingCanvas.SelectedLabel != null &&
                    (hotkeyManager.IsHotkeyPressed("MoveLabelUp", e) ||
                     hotkeyManager.IsHotkeyPressed("MoveLabelDown", e) ||
                     hotkeyManager.IsHotkeyPressed("MoveLabelLeft", e) ||
                     hotkeyManager.IsHotkeyPressed("MoveLabelRight", e));

                // Don't intercept if it's a label movement hotkey
                if (!isLabelMovementHotkey)
                {
                    switch (e.Key)
                    {
                        case Key.C:
                        case Key.V:
                        case Key.Z:
                        case Key.Y:
                        case Key.A:
                            // Let DrawingCanvas handle these
                            return;
                    }
                }
            }

            // Also let DrawingCanvas handle Delete key when it has focus and labels are selected
            if (drawingCanvas.IsFocused && e.Key == Key.Delete &&
                (drawingCanvas.SelectedLabel != null || drawingCanvas.SelectedLabels.Count > 0))
            {
                return;
            }

            int index = -1;

            // Check for both top number keys (D1-D9) and numpad keys (NumPad1-NumPad9)
            if (e.Key >= Key.D1 && e.Key <= Key.D9)
            {
                index = (int)e.Key - (int)Key.D1; // Convert key to index (0-based)
            }
            else if (e.Key >= Key.NumPad1 && e.Key <= Key.NumPad9)
            {
                index = (int)e.Key - (int)Key.NumPad1; // Convert numpad key to index (0-based)
            }

            if (index >= 0 && index < LabelListBox.Items.Count)
            {
                LabelListBox.SelectedIndex = index;

                // Clear multi-selection when using number keys
                drawingCanvas.SelectedLabels.Clear();

                // Ensure the selected label is updated in DrawingCanvas
                if (LabelListBox.SelectedItem is LabelListItemView selectedItem)
                {
                    drawingCanvas.SelectedLabel = selectedItem.Label;

                    // Add the single selected label to SelectedLabels for consistency
                    if (drawingCanvas.SelectedLabel != null)
                    {
                        drawingCanvas.SelectedLabels.Add(drawingCanvas.SelectedLabel);
                    }

                    drawingCanvas.InvalidateVisual();
                }

                // Force focus back to the window to keep key events working
                this.Focus();
                Keyboard.Focus(this);

                e.Handled = true;
                return; // Prevent further processing
            }

            // Handle label movement and deletion - but only if DrawingCanvas doesn't have focus
            // Note: Custom hotkeys are handled in Window_KeyDown, this is for backward compatibility
            if (!drawingCanvas.IsFocused && drawingCanvas.SelectedLabel != null)
            {
                int moveAmount = 1;
                bool moved = false;

                // Check if custom hotkeys are set (different from default arrow keys)
                bool useCustomHotkeys = hotkeyManager != null && 
                    ((!string.IsNullOrEmpty(Properties.Settings.Default.Hotkey_MoveLabelUp) && Properties.Settings.Default.Hotkey_MoveLabelUp != "Up") ||
                     (!string.IsNullOrEmpty(Properties.Settings.Default.Hotkey_MoveLabelDown) && Properties.Settings.Default.Hotkey_MoveLabelDown != "Down") ||
                     (!string.IsNullOrEmpty(Properties.Settings.Default.Hotkey_MoveLabelLeft) && Properties.Settings.Default.Hotkey_MoveLabelLeft != "Left") ||
                     (!string.IsNullOrEmpty(Properties.Settings.Default.Hotkey_MoveLabelRight) && Properties.Settings.Default.Hotkey_MoveLabelRight != "Right"));

                // Only use arrow keys if custom hotkeys are not set (backward compatibility)
                if (!useCustomHotkeys)
                {
                    switch (e.Key)
                    {
                        case Key.Up:
                            MoveSelectedLabel(0, -moveAmount);
                            moved = true;
                            break;

                        case Key.Down:
                            MoveSelectedLabel(0, moveAmount);
                            moved = true;
                            break;

                        case Key.Left:
                            MoveSelectedLabel(-moveAmount, 0);
                            moved = true;
                            break;

                        case Key.Right:
                            MoveSelectedLabel(moveAmount, 0);
                            moved = true;
                            break;
                    }
                }

                if (moved)
                {
                    e.Handled = true;
                    drawingCanvas.InvalidateVisual();
                    MarkProjectDirty();
                    return;
                }

                // Handle Delete key
                switch (e.Key)
                {
                    case Key.Delete:
                        // Handle multi-selection deletion
                        if (drawingCanvas.SelectedLabels.Count > 0)
                        {
                            var labelsToDelete = drawingCanvas.SelectedLabels.ToList();
                            foreach (var label in labelsToDelete)
                            {
                                drawingCanvas.Labels.Remove(label);
                            }
                            drawingCanvas.SelectedLabels.Clear();
                            drawingCanvas.SelectedLabel = null;
                        }
                        else if (drawingCanvas.SelectedLabel != null)
                        {
                            drawingCanvas.Labels.Remove(drawingCanvas.SelectedLabel);
                            drawingCanvas.SelectedLabel = null;
                        }
                        OnLabelsChanged();
                        drawingCanvas.InvalidateVisual();
                        e.Handled = true;
                        break;
                }

                if (e.Handled)
                {
                    drawingCanvas.InvalidateVisual();
                    MarkProjectDirty();
                }
            }
        }


        private void Window_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            // Don't handle mouse wheel if modifier keys are pressed (for zoom or other functionality)
            if (Keyboard.Modifiers != ModifierKeys.None)
            {
                return; // Let other handlers (like zoom) handle it
            }

            // If the pointer is over the class list, scroll that list instead of
            // navigating images. The window-level Preview handler would otherwise
            // swallow every wheel event, so we forward it to the ClassListBox here.
            if (e.OriginalSource is DependencyObject src && IsDescendantOf(src, ClassListBox))
            {
                var scrollViewer = FindVisualChild<ScrollViewer>(ClassListBox);
                if (scrollViewer != null)
                {
                    scrollViewer.ScrollToVerticalOffset(
                        scrollViewer.VerticalOffset - e.Delta / 3.0);
                    e.Handled = true;
                }
                return;
            }

            // Only navigate if we have images loaded
            if (ImageListBox.Items.Count == 0)
            {
                return;
            }

            int currentIndex = ImageListBox.SelectedIndex;

            // Scroll up = previous image (negative delta)
            // Scroll down = next image (positive delta)
            if (e.Delta > 0)
            {
                // Scroll up - go to previous image
                if (currentIndex > 0)
                {
                    ImageListBox.SelectedIndex = currentIndex - 1;
                    ImageListBox.ScrollIntoView(ImageListBox.SelectedItem);
                    e.Handled = true;
                }
            }
            else if (e.Delta < 0)
            {
                // Scroll down - go to next image
                if (currentIndex < ImageListBox.Items.Count - 1)
                {
                    ImageListBox.SelectedIndex = currentIndex + 1;
                    ImageListBox.ScrollIntoView(ImageListBox.SelectedItem);
                    e.Handled = true;
                }
            }
        }
        // Returns true if 'node' is (or is contained within) 'ancestor' in the visual tree.
        private static bool IsDescendantOf(DependencyObject node, DependencyObject ancestor)
        {
            while (node != null)
            {
                if (node == ancestor)
                    return true;

                // VisualTreeHelper.GetParent throws for non-visual nodes such as
                // System.Windows.Documents.Run (inline text inside a TextBlock),
                // which can appear as e.OriginalSource. Only walk the visual tree
                // for actual Visual/Visual3D nodes; otherwise fall back to the
                // logical tree.
                DependencyObject parent = null;
                if (node is Visual || node is System.Windows.Media.Media3D.Visual3D)
                    parent = VisualTreeHelper.GetParent(node);

                node = parent
                    ?? LogicalTreeHelper.GetParent(node)
                    ?? (node as FrameworkElement)?.Parent
                    ?? (node as FrameworkContentElement)?.Parent;
            }
            return false;
        }

        // Depth-first search for the first visual child of type T.
        private static T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typed)
                    return typed;

                var result = FindVisualChild<T>(child);
                if (result != null)
                    return result;
            }
            return null;
        }

        private void SortByName_Click(object sender, RoutedEventArgs e)
        {
            uiStateManager.SortImagesByName();
        }

        private void SortByStatus_Click(object sender, RoutedEventArgs e)
        {
            uiStateManager.SortImagesByStatus();
        }

        private void SortComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Can fire during initialization before uiStateManager is ready
            if (uiStateManager == null) return;

            switch (SortComboBox.SelectedIndex)
            {
                case 0:
                    uiStateManager.SortImagesByName();
                    break;
                case 1:
                    uiStateManager.SortImagesByStatus();
                    break;
            }
        }

        private void FilterAll_Click(object sender, RoutedEventArgs e)
        {
            uiStateManager.UpdateFilterButtonStyles(
                FilterAllButton, FilterReviewButton, FilterSuggestedButton, FilterNoLabelButton,
                FilterVerifiedButton, FilterAiLabelsButton,
                activeButton: FilterAllButton);
            uiStateManager.FilterImagesByStatus(null);
        }

        private void FilterReview_Click(object sender, RoutedEventArgs e)
        {
            uiStateManager.UpdateFilterButtonStyles(
                FilterAllButton, FilterReviewButton, FilterSuggestedButton, FilterNoLabelButton,
                FilterVerifiedButton, FilterAiLabelsButton,
                activeButton: FilterReviewButton);
            uiStateManager.FilterImagesByStatus(ImageStatus.VerificationNeeded);
        }

        private void FilterSuggested_Click(object sender, RoutedEventArgs e)
        {
            uiStateManager.UpdateFilterButtonStyles(
                FilterAllButton, FilterReviewButton, FilterSuggestedButton, FilterNoLabelButton,
                FilterVerifiedButton, FilterAiLabelsButton,
                activeButton: FilterSuggestedButton);
            uiStateManager.FilterImagesByStatus(ImageStatus.Suggested);
        }

        private void FilterNoLabel_Click(object sender, RoutedEventArgs e)
        {
            uiStateManager.UpdateFilterButtonStyles(
                FilterAllButton, FilterReviewButton, FilterSuggestedButton, FilterNoLabelButton,
                FilterVerifiedButton, FilterAiLabelsButton,
                activeButton: FilterNoLabelButton);
            uiStateManager.FilterImagesByStatus(ImageStatus.NoLabel);
        }

        private void FilterVerified_Click(object sender, RoutedEventArgs e)
        {
            uiStateManager.UpdateFilterButtonStyles(
                FilterAllButton, FilterReviewButton, FilterSuggestedButton, FilterNoLabelButton,
                FilterVerifiedButton, FilterAiLabelsButton,
                activeButton: FilterVerifiedButton);
            uiStateManager.FilterImagesByStatus(ImageStatus.Verified);
        }

        private void FilterAiLabels_Click(object sender, RoutedEventArgs e)
        {
            uiStateManager.UpdateFilterButtonStyles(
                FilterAllButton, FilterReviewButton, FilterSuggestedButton, FilterNoLabelButton,
                FilterVerifiedButton, FilterAiLabelsButton,
                activeButton: FilterAiLabelsButton);
            uiStateManager.FilterImagesWithAiLabels();
        }

        private async void SettingsMenuItem_Click(object sender, RoutedEventArgs e)
        {
            bool previousUseGpu = Properties.Settings.Default.UseGPU;
            var settingsWindow = new SettingsWindow(
                yoloAI,
                projectClasses,
                projectManager?.CurrentProject?.AIClassConfidenceThresholds,
                projectManager?.CurrentProject?.ClassTeamPairs);
            settingsWindow.Owner = this;
            if (settingsWindow.ShowDialog() == true)
            {
                if (settingsWindow.ClassConfidenceThresholdsChanged &&
                    projectManager?.CurrentProject != null)
                {
                    projectManager.CurrentProject.AIClassConfidenceThresholds =
                        new Dictionary<int, float>(settingsWindow.ClassConfidenceThresholds);
                    MarkProjectDirty();
                }

                if (settingsWindow.ClassTeamPairsChanged &&
                    projectManager?.CurrentProject != null)
                {
                    projectManager.CurrentProject.ClassTeamPairs =
                        settingsWindow.ClassTeamPairs;
                    MarkProjectDirty();
                }

                // Settings were saved, reapply batch sizes
                imageManager.BatchSize = Properties.Settings.Default.ProcessingBatchSize;
                labelManager.LabelLoadBatchSize = Properties.Settings.Default.LabelLoadBatchSize;

                // Reload hotkeys after settings are saved
                if (hotkeyManager != null)
                {
                    hotkeyManager.Clear();
                    LoadHotkeys();
                }

                bool processingDeviceChanged =
                    previousUseGpu != Properties.Settings.Default.UseGPU;
                bool providerDoesNotMatchSetting = Properties.Settings.Default.UseGPU
                    ? !yoloAI.AllModelsUseDirectMl
                    : yoloAI.UsesDirectMl;
                if ((processingDeviceChanged || providerDoesNotMatchSetting) &&
                    yoloAI.GetLoadedModelsCount() > 0)
                {
                    overlayManager.ShowOverlay(
                        LanguageManager.Instance.GetString("Msg_ReloadingModelsForDevice") ??
                        "Reloading AI models for the selected device...");

                    ExecutionProviderReloadResult reloadResult;
                    string reloadError;
                    try
                    {
                        (reloadResult, reloadError) = await Task.Run(() =>
                        {
                            var result = yoloAI.ReloadExecutionProviders(out string error);
                            return (result, error);
                        });
                    }
                    finally
                    {
                        overlayManager.HideOverlay();
                    }

                    if (reloadResult == ExecutionProviderReloadResult.Failed)
                    {
                        CustomMessageBox.Show(
                            string.Format(
                                LanguageManager.Instance.GetString("Msg_ModelInitFailed") ??
                                "Failed to initialize model: {0}",
                                reloadError),
                            LanguageManager.Instance.GetString("Msg_Error") ?? "Error",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                    }
                    else if (reloadResult == ExecutionProviderReloadResult.FallbackToCpu)
                    {
                        CustomMessageBox.Show(
                            string.Format(
                                LanguageManager.Instance.GetString("Msg_GPUInitFailed") ??
                                "GPU initialization failed: {0}\nFalling back to CPU.",
                                reloadError),
                            LanguageManager.Instance.GetString("Msg_GPUWarning") ?? "GPU Warning",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                    }
                }

                // Update the UI to reflect any changes
                UpdateProjectUI();
            }
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);
            yoloAI?.Dispose();
        }

        #endregion

        #region Class Management

        private void InitializeDefaultClass()
        {
            if (projectClasses.Count == 0)
            {
                projectClasses.Add(new LabelClass("default", "#E57373", 0));
                RefreshClassList();
            }
            else
            {
                RefreshClassFilterCheckBoxes();
            }
        }

        private void RefreshClassList()
        {
            // Update ClassListBox
            ClassListBox.ItemsSource = null;
            ClassListBox.ItemsSource = projectClasses;
            
            // Update DrawingCanvas with available classes
            drawingCanvas.SetAvailableClasses(projectClasses);
            
            // Update LabelManager with valid class IDs so it can fix orphaned labels
            labelManager.SetValidClassIds(projectClasses.Select(c => c.ClassId));
            
            // Select current class in list
            var currentClass = projectClasses.FirstOrDefault(c => c.ClassId == drawingCanvas.CurrentClassId);
            if (currentClass != null)
            {
                ClassListBox.SelectedItem = currentClass;
            }
            
            // Update UI display
            UpdateCurrentClassUI();
            
            // Refresh class filter checkboxes
            RefreshClassFilterCheckBoxes();
        }

        /// <summary>
        /// Refreshes the class filter checkboxes in the Expander
        /// </summary>
        private void RefreshClassFilterCheckBoxes()
        {
            if (ClassFilterCheckBoxPanel == null)
                return;

            // Clear existing checkboxes
            ClassFilterCheckBoxPanel.Children.Clear();

            // Create checkbox for each class
            foreach (var labelClass in projectClasses)
            {
                var checkBox = new CheckBox
                {
                    Content = labelClass.Name,
                    Tag = labelClass.ClassId,
                    IsChecked = true, // Default: all classes are selected
                    Margin = new Thickness(0, 4, 0, 4),
                    FontSize = 11
                };

                // Add color indicator
                var stackPanel = new StackPanel { Orientation = Orientation.Horizontal };
                
                // Color bar
                var colorBar = new Border
                {
                    Width = 4,
                    Height = 16,
                    Background = new SolidColorBrush(
                        (Color)ColorConverter.ConvertFromString(labelClass.ColorHex)),
                    Margin = new Thickness(0, 0, 8, 0),
                    CornerRadius = new CornerRadius(2),
                    VerticalAlignment = VerticalAlignment.Center
                };
                stackPanel.Children.Add(colorBar);

                // Checkbox
                stackPanel.Children.Add(checkBox);

                // Wrap in a container
                var container = new StackPanel { Orientation = Orientation.Horizontal };
                container.Children.Add(stackPanel);

                // Subscribe to checkbox change event
                checkBox.Checked += ClassFilterCheckBox_Changed;
                checkBox.Unchecked += ClassFilterCheckBox_Changed;

                ClassFilterCheckBoxPanel.Children.Add(container);
            }
        }

        /// <summary>
        /// Handles class filter checkbox changes
        /// </summary>
        private void ClassFilterCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            ApplyClassFilter();
        }

        /// <summary>
        /// Handles switching between the Include / Only class-filter modes.
        /// </summary>
        private void ClassFilterMode_Changed(object sender, RoutedEventArgs e)
        {
            ApplyClassFilter();
        }

        private ClassFilterMode GetClassFilterMode()
        {
            if (ClassFilterModeOnly?.IsChecked == true)
                return ClassFilterMode.Only;
            if (ClassFilterModeAll?.IsChecked == true)
                return ClassFilterMode.All;
            if (ClassFilterModeExclude?.IsChecked == true)
                return ClassFilterMode.Exclude;
            return ClassFilterMode.Include;
        }

        private IEnumerable<CheckBox> GetClassFilterCheckBoxes()
        {
            if (ClassFilterCheckBoxPanel == null)
                yield break;

            foreach (var container in ClassFilterCheckBoxPanel.Children.OfType<StackPanel>())
            {
                var stackPanel = container.Children.OfType<StackPanel>().FirstOrDefault();
                var checkBox = stackPanel?.Children.OfType<CheckBox>().FirstOrDefault();
                if (checkBox != null)
                    yield return checkBox;
            }
        }

        // Bulk-sets every class checkbox with the filter re-applied only once at the end.
        private void SetAllClassCheckBoxes(Func<CheckBox, bool> valueSelector)
        {
            var boxes = GetClassFilterCheckBoxes().ToList();
            if (boxes.Count == 0)
                return;

            suppressClassFilterApply = true;
            foreach (var box in boxes)
                box.IsChecked = valueSelector(box);
            suppressClassFilterApply = false;

            ApplyClassFilter();
        }

        private void ClassFilterSelectAll_Click(object sender, RoutedEventArgs e) =>
            SetAllClassCheckBoxes(_ => true);

        private void ClassFilterClear_Click(object sender, RoutedEventArgs e) =>
            SetAllClassCheckBoxes(_ => false);

        private void ClassFilterInvert_Click(object sender, RoutedEventArgs e) =>
            SetAllClassCheckBoxes(box => box.IsChecked != true);

        /// <summary>
        /// Applies the current class-filter selection and mode to the image list.
        /// </summary>
        private void ApplyClassFilter()
        {
            if (suppressClassFilterApply)
                return;

            // The mode radio's Checked fires during XAML init before the class checkboxes are
            // populated; bail out so we don't clear the list with an empty selection.
            if (ClassFilterCheckBoxPanel == null || ClassFilterCheckBoxPanel.Children.Count == 0)
                return;

            var checkedClassIds = new HashSet<int>();
            foreach (var container in ClassFilterCheckBoxPanel.Children.OfType<StackPanel>())
            {
                var stackPanel = container.Children.OfType<StackPanel>().FirstOrDefault();
                var checkBox = stackPanel?.Children.OfType<CheckBox>().FirstOrDefault();
                if (checkBox != null && checkBox.IsChecked == true && checkBox.Tag is int classId)
                    checkedClassIds.Add(classId);
            }

            ClassFilterMode mode = GetClassFilterMode();

            if (checkedClassIds.Count == 0)
            {
                // Nothing selected: show no images.
                ImageListBox.Items.Clear();
                uiStateManager.UpdateStatusCounts();
            }
            else if (mode == ClassFilterMode.Include && checkedClassIds.Count == projectClasses.Count)
            {
                // Include + every class selected is equivalent to no class filter (show all images).
                uiStateManager.FilterImagesByClasses(null);
            }
            else
            {
                uiStateManager.FilterImagesByClasses(checkedClassIds, mode);
            }
        }

        private void UpdateCurrentClassUI()
        {
            var currentClass = projectClasses.FirstOrDefault(c => c.ClassId == drawingCanvas.CurrentClassId);
            if (currentClass != null)
            {
                CurrentClassNameText.Text = currentClass.Name;

                var color = currentClass.ColorBrush.Color;
                CurrentClassColorIndicator.Background = currentClass.ColorBrush;
                
                // Semi-transparent background
                CurrentClassBorder.Background = new SolidColorBrush(
                    Color.FromArgb(0x44, color.R, color.G, color.B));
                CurrentClassBorder.BorderBrush = currentClass.ColorBrush;
            }
        }

        private void DrawingCanvas_CurrentClassChanged(object sender, int classId)
        {
            // Update UI when class changes (e.g., from mouse wheel during drawing)
            UpdateCurrentClassUI();
            
            // Update selection in ClassListBox
            var selectedClass = projectClasses.FirstOrDefault(c => c.ClassId == classId);
            if (selectedClass != null && ClassListBox.SelectedItem != selectedClass)
            {
                ClassListBox.SelectedItem = selectedClass;
            }
        }

        private void ClassListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ClassListBox.SelectedItem is LabelClass selectedClass)
            {
                // Update current drawing class
                drawingCanvas.CurrentClassId = selectedClass.ClassId;
                UpdateCurrentClassUI();
                
                // Enable/disable remove button (can't remove last class)
                RemoveClassButton.IsEnabled = projectClasses.Count > 1;
            }
            else
            {
                RemoveClassButton.IsEnabled = false;
            }
        }

        private void AddClass_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ClassInputDialog();
            dialog.Owner = this;
            
            if (dialog.ShowDialog() == true)
            {
                // Calculate next class ID based on projectClasses (the actual working list)
                // Find the maximum ClassId and add 1, or use 0 if no classes exist
                int newClassId = projectClasses.Any() ? projectClasses.Max(c => c.ClassId) + 1 : 0;
                
                var newClass = new LabelClass(dialog.ClassName, dialog.ClassColor, newClassId);
                projectClasses.Add(newClass);
                
                // Sync with CurrentProject.Classes if project is open
                if (projectManager?.IsProjectOpen == true && projectManager.CurrentProject != null)
                {
                    projectManager.CurrentProject.Classes = new List<LabelClass>(projectClasses);
                }
                
                RefreshClassList();
                
                // Select the new class
                ClassListBox.SelectedItem = newClass;
                drawingCanvas.CurrentClassId = newClassId;
                
                // Mark project as modified
                if (projectManager?.IsProjectOpen == true)
                {
                    MarkProjectDirty();
                }
            }
        }

        private void EditClass_Click(object sender, RoutedEventArgs e)
        {
            // Get the class from the button's Tag property
            if (sender is Button button && button.Tag is LabelClass classToEdit)
            {
                var dialog = new ClassInputDialog(classToEdit, projectClasses);
                dialog.Owner = this;
                
                if (dialog.ShowDialog() == true)
                {
                    // Handle merge if requested
                    if (dialog.ShouldMerge && dialog.MergeTargetClass != null)
                    {
                        // Merge all labels from this class to target class
                        int sourceClassId = classToEdit.ClassId;
                        int targetClassId = dialog.MergeTargetClass.ClassId;

                        // Update all labels in all images
                        foreach (var imagePath in labelManager.LabelStorage.Keys.ToList())
                        {
                            var labels = labelManager.GetLabels(imagePath);
                            bool modified = false;
                            foreach (var label in labels)
                            {
                                if (label.ClassId == sourceClassId)
                                {
                                    label.ClassId = targetClassId;
                                    modified = true;
                                }
                            }
                            if (modified)
                            {
                                labelManager.SaveLabels(imagePath, labels);
                            }
                        }

                        // Update current image labels if displayed
                        if (!string.IsNullOrEmpty(imageManager.CurrentImagePath) && 
                            labelManager.LabelStorage.ContainsKey(imageManager.CurrentImagePath))
                        {
                            drawingCanvas.Labels = labelManager.GetLabels(imageManager.CurrentImagePath);
                            uiStateManager.RefreshLabelList();
                            drawingCanvas.InvalidateVisual();
                        }

                        // Remove the merged class
                        projectClasses.Remove(classToEdit);
                        projectManager?.CurrentProject?.AIClassConfidenceThresholds?.Remove(sourceClassId);

                        // If current class was the merged one, switch to target class
                        if (drawingCanvas.CurrentClassId == sourceClassId)
                        {
                            drawingCanvas.CurrentClassId = targetClassId;
                        }

                        // Refresh UI
                        RefreshClassList();
                        OnLabelsChanged();
                    }
                    else
                    {
                        // Just update the class properties (normal edit)
                        classToEdit.Name = dialog.ClassName;
                        classToEdit.ColorHex = dialog.ClassColor;
                    }
                    
                    // Explicitly update the canvas's available classes list FIRST
                    drawingCanvas.SetAvailableClasses(projectClasses);
                    
                    // Force canvas to redraw with new colors immediately
                    drawingCanvas.InvalidateVisual();
                    drawingCanvas.UpdateLayout(); // Force immediate layout/render update
                    
                    // Refresh class list UI
                    RefreshClassList();
                    
                    // Refresh label list to show updated class names/colors
                    uiStateManager.RefreshLabelList();
                    
                    // Update current class UI if this is the active class
                    if (drawingCanvas.CurrentClassId == classToEdit?.ClassId || 
                        (dialog.ShouldMerge && drawingCanvas.CurrentClassId == dialog.MergeTargetClass?.ClassId))
                    {
                        UpdateCurrentClassUI();
                    }
                    
                    // Mark project as modified
                    if (projectManager?.IsProjectOpen == true)
                    {
                        MarkProjectDirty();
                    }
                }
            }
        }

        private void RemoveClass_Click(object sender, RoutedEventArgs e)
        {
            if (ClassListBox.SelectedItem is not LabelClass classToRemove)
                return;
            
            // Can't remove the last class
            if (projectClasses.Count <= 1)
            {
                CustomMessageBox.Show(
                    LanguageManager.Instance.GetString("Msg_CannotRemoveLastClass") ?? "Cannot remove the last class. At least one class must exist.",
                    LanguageManager.Instance.GetString("Msg_CannotRemoveClassTitle") ?? "Cannot Remove Class",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }
            
            // Check if any labels use this class
            int labelCount = 0;
            foreach (var kvp in labelManager.LabelStorage)
            {
                labelCount += kvp.Value.Count(l => l.ClassId == classToRemove.ClassId);
            }
            
            if (labelCount > 0)
            {
                // Show migration dialog
                var migrationDialog = new ClassMigrationDialog(projectClasses, classToRemove, labelCount);
                migrationDialog.Owner = this;
                
                if (migrationDialog.ShowDialog() != true)
                    return; // User cancelled
                
                int targetClassId = migrationDialog.TargetClassId;
                bool deleteLabels = migrationDialog.DeleteLabels;
                
                if (deleteLabels)
                {
                    // Remove all labels with this class
                    foreach (var kvp in labelManager.LabelStorage.ToList())
                    {
                        var labels = labelManager.GetLabels(kvp.Key);
                        labels.RemoveAll(l => l.ClassId == classToRemove.ClassId);
                        
                        // Remove entry if no labels remain
                        if (labels.Count == 0)
                        {
                            labelManager.RemoveLabels(kvp.Key);
                        }
                        else
                        {
                            labelManager.SaveLabels(kvp.Key, labels);
                        }
                    }
                    
                    // Update image statuses
                    _ = UpdateAllImageStatusesAsync();
                }
                else
                {
                    // Migrate labels to target class
                    foreach (var kvp in labelManager.LabelStorage)
                    {
                        var labels = labelManager.GetLabels(kvp.Key);
                        bool modified = false;
                        foreach (var label in labels.Where(l => l.ClassId == classToRemove.ClassId))
                        {
                            label.ClassId = targetClassId;
                            modified = true;
                        }
                        if (modified)
                        {
                            labelManager.SaveLabels(kvp.Key, labels);
                        }
                    }
                }
            }
            
            // Remove the class
            projectClasses.Remove(classToRemove);
            projectManager?.CurrentProject?.AIClassConfidenceThresholds?.Remove(classToRemove.ClassId);
            
            // If current drawing class was removed, switch to first class
            if (drawingCanvas.CurrentClassId == classToRemove.ClassId)
            {
                drawingCanvas.CurrentClassId = projectClasses.First().ClassId;
            }
            
            RefreshClassList();
            
            // Mark project as modified
            if (projectManager?.IsProjectOpen == true)
            {
                MarkProjectDirty();
            }
            
            // Refresh current image to show updated labels
            if (!string.IsNullOrEmpty(imageManager.CurrentImagePath))
            {
                var currentFile = Path.GetFileName(imageManager.CurrentImagePath);
                var labels = labelManager.GetLabels(currentFile);
                if (labels.Any())
                {
                    drawingCanvas.Labels = new List<LabelData>(labels);
                    drawingCanvas.InvalidateVisual();
                }
            }
            
            // Refresh label list to update UI
            uiStateManager.RefreshLabelList();
        }

        #endregion
    }
}
