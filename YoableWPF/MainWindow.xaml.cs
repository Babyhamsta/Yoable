using Microsoft.Win32;
using ModernWpf;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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

        // External Managers/Handlers (unchanged)
        private YoloAI yoloAI;
        public OverlayManager overlayManager;
        private YoutubeDownloader youtubeDownloader;

        public MainWindow()
        {
            InitializeComponent();

            // Find DrawingCanvas from XAML (pass Listbox)
            drawingCanvas = (DrawingCanvas)FindName("drawingCanvas");
            drawingCanvas.LabelListBox = LabelListBox;

            // Subscribe to the LabelsChanged event to detect any label modifications
            drawingCanvas.LabelsChanged += (sender, e) =>
            {
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

            // Apply batch size settings from user preferences
            imageManager.BatchSize = Properties.Settings.Default.ProcessingBatchSize;
            labelManager.LabelLoadBatchSize = Properties.Settings.Default.LabelLoadBatchSize;

            yoloAI = new YoloAI();
            overlayManager = new OverlayManager(this);
            youtubeDownloader = new YoutubeDownloader(this, overlayManager);

            // Note: projectManager will be set by StartupWindow or initialized here if continuing without project
            // Check for updates from Github
            if (Properties.Settings.Default.CheckUpdatesOnLaunch)
            {
                var autoUpdater = new UpdateManager(this, overlayManager, "3.0.1"); // Current version
                autoUpdater.CheckForUpdatesAsync();
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

            var result = MessageBox.Show(
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

                // Hide overlay and enable window
                overlayManager.HideOverlay();
                this.IsEnabled = true;
            }
            catch (OperationCanceledException)
            {
                overlayManager.HideOverlay();
                this.IsEnabled = true;
                MessageBox.Show("Project loading was canceled.", "Canceled", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                overlayManager.HideOverlay();
                this.IsEnabled = true;
                MessageBox.Show($"Failed to load project:\n\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void SaveProject_Click(object sender, RoutedEventArgs e)
        {
            if (projectManager == null || !projectManager.IsProjectOpen)
            {
                // No project open, prompt to create one
                MessageBox.Show(
                    "No project is currently open. Create a new project or open an existing one.",
                    "No Project",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            // Prevent concurrent saves
            if (projectManager.IsSaving)
            {
                return; // Already saving, ignore this request
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
                MessageBox.Show(
                    "No project is currently open.",
                    "No Project",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var saveFileDialog = new SaveFileDialog
            {
                Filter = "Yoable Project Files (*.yoable)|*.yoable",
                Title = "Save Project As",
                FileName = projectManager.CurrentProject.ProjectName
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                // Show overlay for save operation
                overlayManager.ShowOverlay("Saving project as...");

                try
                {
                    // Export current state to project
                    projectManager.ExportProjectData();

                    // Save to new location
                    if (projectManager.SaveProjectAs(saveFileDialog.FileName))
                    {
                        ProjectNameText.Text = projectManager.CurrentProject.ProjectName;
                        UpdateProjectUI();
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
                MessageBox.Show(
                    "No project is currently open.",
                    "No Project",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            if (projectManager.CloseProject())
            {
                // Clear UI completely
                ProjectNameText.Text = "No Project";
                LastSaveText.Text = "Not saved";
                LastSaveTimeText.Text = "";

                // Clear all data
                ImageListBox.Items.Clear();
                LabelListBox.Items.Clear();
                drawingCanvas.Labels.Clear();
                drawingCanvas.Image = null;
                drawingCanvas.InvalidateVisual();

                // Update project UI
                UpdateProjectUI();

                // Update status counts
                uiStateManager.UpdateStatusCounts();
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
                LastSaveText.Text = "No project";
                LastSaveText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0xFF, 0x9E, 0x9E, 0x9E));
                LastSaveTimeText.Text = "";
                AutoSaveText.Text = "Auto-save disabled";
                AutoSaveIndicator.Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0xFF, 0x9E, 0x9E, 0x9E));
                return;
            }

            // Update save status
            if (projectManager.HasUnsavedChanges)
            {
                SaveStatusBorder.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x44, 0xFF, 0xB7, 0x4D));
                LastSaveText.Text = "Unsaved changes";
                LastSaveText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0xFF, 0xFF, 0xB7, 0x4D));
            }
            else
            {
                SaveStatusBorder.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x44, 0x81, 0xC7, 0x84));
                LastSaveText.Text = "All changes saved";
                LastSaveText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0xFF, 0x81, 0xC7, 0x84));
            }

            // Update last save time
            if (projectManager.LastSaveTime != DateTime.MinValue)
            {
                TimeSpan timeSince = DateTime.Now - projectManager.LastSaveTime;
                if (timeSince.TotalSeconds < 5)
                    LastSaveTimeText.Text = "Saved just now";
                else if (timeSince.TotalMinutes < 1)
                    LastSaveTimeText.Text = $"Saved {(int)timeSince.TotalSeconds} seconds ago";
                else if (timeSince.TotalMinutes < 60)
                    LastSaveTimeText.Text = $"Saved {(int)timeSince.TotalMinutes} minutes ago";
                else if (timeSince.TotalHours < 24)
                    LastSaveTimeText.Text = $"Saved {(int)timeSince.TotalHours} hours ago";
                else
                    LastSaveTimeText.Text = $"Saved on {projectManager.LastSaveTime:MMM dd}";
            }
            else
            {
                LastSaveTimeText.Text = "Not saved yet";
            }

            // Update auto-save indicator
            if (Properties.Settings.Default.EnableAutoSave)
            {
                AutoSaveText.Text = "Auto-save enabled";
                AutoSaveIndicator.Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0xFF, 0x4C, 0xAF, 0x50));
            }
            else
            {
                AutoSaveText.Text = "Auto-save disabled";
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
                var result = MessageBox.Show(
                    $"Do you want to save changes to '{projectManager.CurrentProject.ProjectName}'?",
                    "Unsaved Changes",
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
        }

        #endregion

        #region Existing Methods

        public void OnLabelsChanged()
        {
            if (string.IsNullOrEmpty(imageManager.CurrentImagePath)) return;

            // Get currently selected index
            int currentIndex = ImageListBox.SelectedIndex;
            if (currentIndex < 0 || currentIndex >= ImageListBox.Items.Count) return;

            // Determine status using helper
            ImageStatus newStatus = DetermineImageStatus(imageManager.CurrentImagePath);

            // Use the imageManager method to update status
            imageManager.UpdateImageStatusValue(imageManager.CurrentImagePath, newStatus);

            // Use O(1) cache lookup instead of O(n) iteration
            if (uiStateManager.TryGetFromCache(imageManager.CurrentImagePath, out var imageItem))
            {
                imageItem.Status = newStatus;
            }

            uiStateManager.UpdateStatusCounts();

            // Mark project as dirty
            MarkProjectDirty();
        }

        public void ImageListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ImageListBox.SelectedItem is ImageListItem selected &&
                imageManager.ImagePathMap.TryGetValue(selected.FileName, out ImageManager.ImageInfo imageInfo))
            {
                // Save existing labels
                if (!string.IsNullOrEmpty(imageManager.CurrentImagePath))
                {
                    labelManager.SaveLabels(imageManager.CurrentImagePath, drawingCanvas.Labels);
                    MarkProjectDirty();
                }

                imageManager.CurrentImagePath = selected.FileName;

                drawingCanvas.LoadImage(imageInfo.Path, imageInfo.OriginalDimensions);

                // Reset zoom on image change
                drawingCanvas.ResetZoom();

                // Load labels for this image if they exist
                if (labelManager.LabelStorage.ContainsKey(selected.FileName))
                {
                    drawingCanvas.Labels = new System.Collections.Generic.List<LabelData>(labelManager.LabelStorage[selected.FileName]);

                    // Update status when viewing an image with AI or imported labels
                    if (labelManager.LabelStorage[selected.FileName].Any(l => l.Name.StartsWith("AI") || l.Name.StartsWith("Imported")))
                    {
                        // Only update to Verified if it was previously VerificationNeeded
                        var currentStatus = imageManager.GetImageStatus(selected.FileName);
                        if (currentStatus == ImageStatus.VerificationNeeded)
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

                // Update UI
                uiStateManager.UpdateStatusCounts();
                uiStateManager.RefreshLabelList();

                // CRITICAL FIX: Force canvas redraw after loading labels
                drawingCanvas.InvalidateVisual();
            }
        }

        private void LabelListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LabelListBox.SelectedItem is string selectedLabelName)
            {
                var selectedLabel = drawingCanvas.Labels.FirstOrDefault(label => label.Name == selectedLabelName);

                if (selectedLabel != null)
                {
                    drawingCanvas.SelectedLabel = selectedLabel;
                    drawingCanvas.InvalidateVisual();
                    Keyboard.Focus(this); // Ensure window captures key events
                }
            }
            else
            {
                // No label selected, so immediately deselect in canvas
                drawingCanvas.SelectedLabel = null;
                drawingCanvas.InvalidateVisual();
            }
        }

        private void ImportDirectory_Click(object sender, RoutedEventArgs e)
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

                LoadImages(folderPath);
            }
        }

        private async void ImportImage_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new() { Filter = "Image Files|*.jpg;*.png" };
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
            ImageListBox.Items.Clear();
            LabelListBox.Items.Clear();
            drawingCanvas.Image = null; // Clear the image in DrawingCanvas
            drawingCanvas.InvalidateVisual(); // Force a redraw
            uiStateManager.UpdateStatusCounts();
            uiStateManager.RefreshAllImagesList(); // Clear the filter cache

            MarkProjectDirty();
        }

        private void ManageModels_Click(object sender, RoutedEventArgs e)
        {
            yoloAI.OpenModelManager();
        }

        private async void AutoLabelImages_Click(object sender, RoutedEventArgs e)
        {
            // Check if any models are loaded
            int modelCount = yoloAI.GetLoadedModelsCount();
            if (modelCount == 0)
            {
                var result = MessageBox.Show("No models loaded. Would you like to load models now?",
                    "No Models", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    yoloAI.OpenModelManager();
                }
                return;
            }

            // Show ensemble info if multiple models
            string processingMode = modelCount == 1 ? "single model" : $"ensemble ({modelCount} models)";
            var continueResult = MessageBox.Show(
                $"Process {imageManager.ImagePathMap.Count} images using {processingMode} detection?\n\n" +
                (modelCount > 3 ? "Note: Processing with many models may take considerable time." : ""),
                "Start Detection",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (continueResult != MessageBoxResult.Yes) return;

            // Store current selection
            var currentSelection = ImageListBox.SelectedItem as ImageListItem;

            CancellationTokenSource tokenSource = new CancellationTokenSource();
            overlayManager.ShowOverlayWithProgress($"Running AI Detections ({processingMode})...", tokenSource);

            int totalDetections = 0;
            int totalImages = imageManager.ImagePathMap.Count;
            int processedImages = 0;

            await Task.Run(() =>
            {
                foreach (var imagePath in imageManager.ImagePathMap.Values)
                {
                    if (tokenSource.Token.IsCancellationRequested) break;

                    if (!File.Exists(imagePath.Path))
                    {
                        Dispatcher.Invoke(() =>
                        {
                            MessageBox.Show($"File not found: {imagePath.Path}", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                        });
                        continue;
                    }

                    using Bitmap image = new Bitmap(imagePath.Path);
                    string fileName = Path.GetFileName(imagePath.Path);
                    System.Collections.Generic.List<Rectangle> detectedBoxes = yoloAI.RunInference(image);

                    labelManager.AddAILabels(fileName, detectedBoxes);
                    totalDetections += detectedBoxes.Count;

                    // Only update UI if this is the current image
                    Dispatcher.Invoke(() =>
                    {
                        if (drawingCanvas != null && drawingCanvas.Image != null &&
                            drawingCanvas.Image.ToString().Contains(fileName))
                        {
                            drawingCanvas.Labels.AddRange(labelManager.LabelStorage[fileName]);
                            uiStateManager.RefreshLabelList();
                            drawingCanvas.InvalidateVisual();
                        }
                    });

                    processedImages++;
                    Dispatcher.Invoke(() =>
                    {
                        overlayManager.UpdateProgress((processedImages * 100) / totalImages);
                        overlayManager.UpdateMessage($"Processing image {processedImages}/{totalImages} ({processingMode})...");
                    });
                }
            }, tokenSource.Token);

            overlayManager.HideOverlay();
            await UpdateAllImageStatusesAsync();

            Dispatcher.Invoke(() =>
            {
                uiStateManager.RefreshAllImagesList(); // Refresh for filtering

                // Restore selection if it was lost
                if (ImageListBox.SelectedItem == null)
                {
                    RestoreImageSelection(currentSelection);
                }

                OnLabelsChanged();

                string modeInfo = modelCount == 1 ? "" : $" using {modelCount} models";
                MessageBox.Show($"Auto-labeling complete{modeInfo}.\nTotal detections: {totalDetections}",
                    "AI Labels", MessageBoxButton.OK, MessageBoxImage.Information);

                MarkProjectDirty();
            });
        }

        private void AutoSuggestLabels_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Auto Suggest Labels - Not Implemented Yet");
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

        public async void LoadImages(string directoryPath)
        {
            if (!Directory.Exists(directoryPath)) return;

            await LoadImagesAsync(directoryPath);
        }

        private async Task LoadImagesAsync(string directoryPath)
        {
            var tokenSource = new CancellationTokenSource();
            overlayManager.ShowOverlayWithProgress("Loading images...", tokenSource);

            try
            {
                // Progress reporter
                var progress = CreateProgressReporter();

                // Load images asynchronously
                var files = await imageManager.LoadImagesFromDirectoryAsync(
                    directoryPath,
                    progress,
                    tokenSource.Token,
                    Properties.Settings.Default.EnableParallelProcessing);

                // Update UI in batches for better performance with large sets
                await UpdateImageListInBatchesAsync(files, tokenSource.Token);

                if (ImageListBox.Items.Count > 0)
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
                MessageBox.Show("Image loading was canceled.", "Canceled", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading images: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                overlayManager.HideOverlay();
            }
        }

        private async Task UpdateImageListInBatchesAsync(string[] files, CancellationToken cancellationToken)
        {
            int batchSize = Properties.Settings.Default.UIBatchSize; // Use setting

            await Dispatcher.InvokeAsync(() => ImageListBox.Items.Clear());

            for (int i = 0; i < files.Length; i += batchSize)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var batch = files.Skip(i).Take(batchSize).ToArray();

                await Dispatcher.InvokeAsync(() =>
                {
                    // Temporarily disable UI updates for better performance
                    ImageListBox.SelectionChanged -= ImageListBox_SelectionChanged;

                    foreach (string file in batch)
                    {
                        string fileName = Path.GetFileName(file);
                        ImageListBox.Items.Add(new ImageListItem(fileName, ImageStatus.NoLabel));
                    }

                    ImageListBox.SelectionChanged += ImageListBox_SelectionChanged;
                });

                // Update progress
                overlayManager.UpdateMessage($"Updating UI... {Math.Min(i + batchSize, files.Length)}/{files.Length}");
            }
        }

        private async Task LoadYOLOLabelsFromDirectory(string directoryPath)
        {
            if (!Directory.Exists(directoryPath)) return;

            // Check if images are loaded first (REQUIRED for cached dimensions)
            if (imageManager.ImagePathMap.Count == 0)
            {
                MessageBox.Show("Please load images before importing labels.\n\nImages must be loaded first so label dimensions can be calculated correctly.",
                    "Load Images First", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Store current selection
            var currentSelection = ImageListBox.SelectedItem as ImageListItem;

            string[] labelFiles = Directory.GetFiles(directoryPath, "*.txt");
            int totalFiles = labelFiles.Length;

            if (totalFiles == 0)
            {
                MessageBox.Show("No YOLO label files found in the selected directory.", "Label Import", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            CancellationTokenSource tokenSource = new CancellationTokenSource();
            overlayManager.ShowOverlayWithProgress("Importing YOLO labels...", tokenSource);

            try
            {
                // ✅ Progress reporter for real-time feedback
                var progress = CreateProgressReporter();

                // ✅ 🚀 NEW: Use high-performance batch loading with parallel processing
                int labelsLoaded = await labelManager.LoadYoloLabelsBatchAsync(
                    directoryPath,
                    imageManager,
                    progress,
                    tokenSource.Token,
                    Properties.Settings.Default.EnableParallelProcessing
                );

                overlayManager.HideOverlay();

                // Update all image statuses after importing labels
                Debug.WriteLine("Updating All Image Statuses");
                await UpdateAllImageStatusesAsync();
                uiStateManager.RefreshAllImagesList(); // Refresh for filtering

                if (!string.IsNullOrEmpty(imageManager.CurrentImagePath) && labelManager.LabelStorage.ContainsKey(imageManager.CurrentImagePath))
                {
                    drawingCanvas.Labels = labelManager.LabelStorage[imageManager.CurrentImagePath];
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
                MessageBox.Show("Label import was cancelled by user.", "Import Cancelled", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                overlayManager.HideOverlay();
                MessageBox.Show($"Error importing labels: {ex.Message}", "Import Error", MessageBoxButton.OK, MessageBoxImage.Error);
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

                overlayManager.HideOverlay();
                MessageBox.Show("Labels exported successfully!", "Export Complete",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (OperationCanceledException)
            {
                overlayManager.HideOverlay();
                MessageBox.Show("Export canceled.", "Canceled",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                overlayManager.HideOverlay();
                MessageBox.Show($"Export failed: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
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
            if (!labelManager.LabelStorage.TryGetValue(fileName, out var labels) || labels.Count == 0)
                return ImageStatus.NoLabel;

            // Fast check for imported/AI labels
            for (int i = 0; i < labels.Count; i++)
            {
                var name = labels[i].Name;
                if (name.Length > 0 && (name[0] == 'I' || name[0] == 'A'))
                {
                    if (name.StartsWith("Imported") || name.StartsWith("AI"))
                        return ImageStatus.VerificationNeeded;
                }
            }

            return ImageStatus.Verified;
        }

        private void UpdateImageStatus(string fileName)
        {
            if (!imageManager.ImagePathMap.ContainsKey(fileName)) return;

            ImageStatus newStatus = DetermineImageStatus(fileName);

            imageManager.UpdateImageStatusValue(fileName, newStatus);

            // Use O(1) dictionary lookup instead of O(n) iteration
            if (uiStateManager.TryGetFromCache(fileName, out var imageItem))
            {
                imageItem.Status = newStatus;
            }
        }

        private async void Window_KeyDown(object sender, KeyEventArgs e)
        {
            // Handle Ctrl+S for save
            if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control && e.Key == Key.S)
            {
                SaveProject_Click(sender, e);
                e.Handled = true;
                return;
            }

            OnPreviewKeyDown(e);
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);

            // If DrawingCanvas has focus and Ctrl is pressed, let it handle the shortcuts
            if (drawingCanvas.IsFocused && (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl)))
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
                string selectedLabelName = LabelListBox.SelectedItem as string;
                if (!string.IsNullOrEmpty(selectedLabelName))
                {
                    drawingCanvas.SelectedLabel = drawingCanvas.Labels.FirstOrDefault(label => label.Name == selectedLabelName);

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
            if (!drawingCanvas.IsFocused && drawingCanvas.SelectedLabel != null)
            {
                int moveAmount = 1;

                switch (e.Key)
                {
                    case Key.Up:
                        drawingCanvas.SelectedLabel.Rect = new Rect(drawingCanvas.SelectedLabel.Rect.X, drawingCanvas.SelectedLabel.Rect.Y - moveAmount, drawingCanvas.SelectedLabel.Rect.Width, drawingCanvas.SelectedLabel.Rect.Height);
                        e.Handled = true;
                        break;

                    case Key.Down:
                        drawingCanvas.SelectedLabel.Rect = new Rect(drawingCanvas.SelectedLabel.Rect.X, drawingCanvas.SelectedLabel.Rect.Y + moveAmount, drawingCanvas.SelectedLabel.Rect.Width, drawingCanvas.SelectedLabel.Rect.Height);
                        e.Handled = true;
                        break;

                    case Key.Left:
                        drawingCanvas.SelectedLabel.Rect = new Rect(drawingCanvas.SelectedLabel.Rect.X - moveAmount, drawingCanvas.SelectedLabel.Rect.Y, drawingCanvas.SelectedLabel.Rect.Width, drawingCanvas.SelectedLabel.Rect.Height);
                        e.Handled = true;
                        break;

                    case Key.Right:
                        drawingCanvas.SelectedLabel.Rect = new Rect(drawingCanvas.SelectedLabel.Rect.X + moveAmount, drawingCanvas.SelectedLabel.Rect.Y, drawingCanvas.SelectedLabel.Rect.Width, drawingCanvas.SelectedLabel.Rect.Height);
                        e.Handled = true;
                        break;

                    case Key.Delete:
                        // Handle multi-selection deletion
                        if (drawingCanvas.SelectedLabels.Count > 0)
                        {
                            var labelsToDelete = drawingCanvas.SelectedLabels.ToList();
                            foreach (var label in labelsToDelete)
                            {
                                drawingCanvas.Labels.Remove(label);
                                LabelListBox.Items.Remove(label.Name);
                            }
                            drawingCanvas.SelectedLabels.Clear();
                            drawingCanvas.SelectedLabel = null;
                        }
                        else if (drawingCanvas.SelectedLabel != null)
                        {
                            drawingCanvas.Labels.Remove(drawingCanvas.SelectedLabel);
                            LabelListBox.Items.Remove(drawingCanvas.SelectedLabel.Name);
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
                FilterAllButton, FilterReviewButton, FilterNoLabelButton, FilterVerifiedButton,
                activeButton: FilterAllButton);
            uiStateManager.FilterImagesByStatus(null);
        }

        private void FilterReview_Click(object sender, RoutedEventArgs e)
        {
            uiStateManager.UpdateFilterButtonStyles(
                FilterAllButton, FilterReviewButton, FilterNoLabelButton, FilterVerifiedButton,
                activeButton: FilterReviewButton);
            uiStateManager.FilterImagesByStatus(ImageStatus.VerificationNeeded);
        }

        private void FilterNoLabel_Click(object sender, RoutedEventArgs e)
        {
            uiStateManager.UpdateFilterButtonStyles(
                FilterAllButton, FilterReviewButton, FilterNoLabelButton, FilterVerifiedButton,
                activeButton: FilterNoLabelButton);
            uiStateManager.FilterImagesByStatus(ImageStatus.NoLabel);
        }

        private void FilterVerified_Click(object sender, RoutedEventArgs e)
        {
            uiStateManager.UpdateFilterButtonStyles(
                FilterAllButton, FilterReviewButton, FilterNoLabelButton, FilterVerifiedButton,
                activeButton: FilterVerifiedButton);
            uiStateManager.FilterImagesByStatus(ImageStatus.Verified);
        }

        private void SettingsMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var settingsWindow = new SettingsWindow(yoloAI);
            settingsWindow.Owner = this;
            if (settingsWindow.ShowDialog() == true)
            {
                // Settings were saved, reapply batch sizes
                imageManager.BatchSize = Properties.Settings.Default.ProcessingBatchSize;
                labelManager.LabelLoadBatchSize = Properties.Settings.Default.LabelLoadBatchSize;

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
    }
}