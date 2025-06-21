using Microsoft.Win32;
using ModernWpf;
using System.ComponentModel;
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
        // Managers
        private ImageManager imageManager;
        private LabelManager labelManager;
        private UIStateManager uiStateManager;

        // External Managers/Handlers (unchanged)
        private YoloAI yoloAI;
        private OverlayManager overlayManager;
        // Commented out for now as not being used
        // private CloudUploader cloudUploader;
        private YoutubeDownloader youtubeDownloader;

        public MainWindow()
        {
            InitializeComponent();

            // Find DrawingCanvas from XAML (pass Listbox)
            drawingCanvas = (DrawingCanvas)FindName("drawingCanvas");
            drawingCanvas.LabelListBox = LabelListBox;

            // Load saved settings
            bool isDarkTheme = Properties.Settings.Default.DarkTheme;
            ThemeManager.Current.ApplicationTheme = isDarkTheme ? ApplicationTheme.Dark : ApplicationTheme.Light;

            string FormAccentHex = Properties.Settings.Default.FormAccent;
            ThemeManager.Current.AccentColor = (Color)ColorConverter.ConvertFromString(FormAccentHex);

            // Initialize managers
            imageManager = new ImageManager();
            labelManager = new LabelManager();
            uiStateManager = new UIStateManager(this);

            yoloAI = new YoloAI();
            overlayManager = new OverlayManager(this);
            // Commented out for now as not being used
            // cloudUploader = new CloudUploader(this, overlayManager);
            youtubeDownloader = new YoutubeDownloader(this, overlayManager);

            // Check for updates from Github
            if (Properties.Settings.Default.CheckUpdatesOnLaunch)
            {
                var autoUpdater = new UpdateManager(this, overlayManager, "2.4.0"); // Current version
                autoUpdater.CheckForUpdatesAsync();
            }
        }

        private void RefreshLabelList()
        {
            uiStateManager.RefreshLabelList();
        }

        public void OnLabelsChanged()
        {
            if (string.IsNullOrEmpty(imageManager.CurrentImagePath)) return;

            // Get currently selected index
            int currentIndex = ImageListBox.SelectedIndex;
            if (currentIndex < 0 || currentIndex >= ImageListBox.Items.Count) return;

            // Update the status based on whether there are any labels
            var hasLabels = drawingCanvas.Labels.Any();
            var isImportedOrAI = hasLabels && drawingCanvas.Labels.Any(l => l.Name.StartsWith("AI") || l.Name.StartsWith("Imported"));

            ImageStatus newStatus;
            if (!hasLabels)
            {
                newStatus = ImageStatus.NoLabel;
            }
            else
            {
                // If the image is currently loaded in Canvas, mark it as Verified since the user is actively working with it
                if (drawingCanvas.Image != null &&
                    drawingCanvas.Image.ToString().Contains(imageManager.CurrentImagePath))
                {
                    newStatus = ImageStatus.Verified;
                }
                else if (isImportedOrAI)
                {
                    newStatus = ImageStatus.VerificationNeeded;
                }
                else
                {
                    newStatus = ImageStatus.Verified;
                }
            }

            // Use the imageManager method to update status
            imageManager.UpdateImageStatusValue(imageManager.CurrentImagePath, newStatus);

            // Update the existing ListBox item instead of replacing it
            foreach (var item in ImageListBox.Items)
            {
                if (item is ImageListItem imageItem && imageItem.FileName == imageManager.CurrentImagePath)
                {
                    imageItem.Status = newStatus;
                    break;
                }
            }

            uiStateManager.UpdateStatusCounts();
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
                }

                imageManager.CurrentImagePath = selected.FileName;

                drawingCanvas.LoadImage(imageInfo.Path, imageInfo.OriginalDimensions);

                // Reset zoom on image change
                drawingCanvas.ResetZoom();

                // Load labels for this image if they exist
                if (labelManager.LabelStorage.ContainsKey(selected.FileName))
                {
                    drawingCanvas.Labels = new List<LabelData>(labelManager.LabelStorage[selected.FileName]);

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
                RefreshLabelList();
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

            if (openFileDialog.ShowDialog() == true)
            {
                string folderPath = Path.GetDirectoryName(openFileDialog.FileName);
                LoadImages(folderPath);
            }
        }

        private void ImportImage_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new() { Filter = "Image Files|*.jpg;*.png" };
            if (openFileDialog.ShowDialog() == true)
            {
                AddImage(openFileDialog.FileName);
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

            if (openFileDialog.ShowDialog() == true)
            {
                string folderPath = Path.GetDirectoryName(openFileDialog.FileName);
                if (!string.IsNullOrEmpty(folderPath))
                {
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

            if (openFileDialog.ShowDialog() == true)
            {
                string folderPath = Path.GetDirectoryName(openFileDialog.FileName);
                if (!string.IsNullOrEmpty(folderPath))
                {
                    await ExportLabelsToYoloAsync(folderPath);
                }
            }
        }

        private void UpdateAllImageStatuses()
        {
            foreach (var fileName in imageManager.ImagePathMap.Keys.ToList())
            {
                UpdateImageStatus(fileName);
            }
            uiStateManager.UpdateStatusCounts();
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
                    List<Rectangle> detectedBoxes = yoloAI.RunInference(image);

                    lock (labelManager.LabelStorage)
                    {
                        labelManager.AddAILabels(fileName, detectedBoxes);
                        totalDetections += detectedBoxes.Count;
                    }

                    // Only update UI if this is the current image
                    Dispatcher.Invoke(() =>
                    {
                        if (drawingCanvas != null && drawingCanvas.Image != null &&
                            drawingCanvas.Image.ToString().Contains(fileName))
                        {
                            drawingCanvas.Labels.AddRange(labelManager.LabelStorage[fileName]);
                            drawingCanvas.LabelListBox?.Items.Clear();
                            foreach (var label in labelManager.LabelStorage[fileName])
                            {
                                drawingCanvas.LabelListBox?.Items.Add(label.Name);
                            }
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

            Dispatcher.Invoke(() =>
            {
                overlayManager.HideOverlay();
                UpdateAllImageStatuses();

                // Restore selection if it was lost
                if (currentSelection != null && ImageListBox.SelectedItem == null)
                {
                    for (int i = 0; i < ImageListBox.Items.Count; i++)
                    {
                        if (ImageListBox.Items[i] is ImageListItem item &&
                            item.FileName == currentSelection.FileName)
                        {
                            ImageListBox.SelectedIndex = i;
                            ImageListBox.ScrollIntoView(ImageListBox.SelectedItem);
                            break;
                        }
                    }
                }

                OnLabelsChanged();

                string modeInfo = modelCount == 1 ? "" : $" using {modelCount} models";
                MessageBox.Show($"Auto-labeling complete{modeInfo}.\nTotal detections: {totalDetections}",
                    "AI Labels", MessageBoxButton.OK, MessageBoxImage.Information);
            });
        }

        private void AutoSuggestLabels_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Auto Suggest Labels - Not Implemented Yet");
        }

        private void AISettings_Click(object sender, RoutedEventArgs e)
        {
            var settingsWindow = new SettingsWindow(yoloAI); // Pass yoloAI instance
            settingsWindow.AISettingsGroup.Visibility = Visibility.Visible;
            settingsWindow.PerformanceSettingsGroup.Visibility = Visibility.Collapsed;
            settingsWindow.Owner = this;
            settingsWindow.ShowDialog();
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

            // Set batch size from settings
            imageManager.BatchSize = Properties.Settings.Default.ProcessingBatchSize;

            await LoadImagesAsync(directoryPath);
        }

        private async Task LoadImagesAsync(string directoryPath)
        {
            var tokenSource = new CancellationTokenSource();
            overlayManager.ShowOverlayWithProgress("Loading images...", tokenSource);

            try
            {
                // Progress reporter
                var progress = new Progress<(int current, int total, string message)>(report =>
                {
                    overlayManager.UpdateMessage(report.message);
                    overlayManager.UpdateProgress((report.current * 100) / report.total);
                });

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

                uiStateManager.UpdateStatusCounts();
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

        private void AddImage(string filePath)
        {
            if (!imageManager.AddImage(filePath)) return;

            string fileName = Path.GetFileName(filePath);

            if (!ImageListBox.Items.Contains(new ImageListItem(fileName, ImageStatus.NoLabel)))
            {
                ImageListBox.Items.Add(new ImageListItem(fileName, ImageStatus.NoLabel));
            }

            if (ImageListBox.Items.Count == 1)
            {
                ImageListBox.SelectedIndex = 0;
            }
        }

        private async Task LoadYOLOLabelsFromDirectory(string directoryPath)
        {
            if (!Directory.Exists(directoryPath)) return;

            // Store current selection
            var currentSelection = ImageListBox.SelectedItem as ImageListItem;

            string[] labelFiles = Directory.GetFiles(directoryPath, "*.txt");
            int totalFiles = labelFiles.Length;
            int processedFiles = 0;

            if (totalFiles == 0)
            {
                MessageBox.Show("No YOLO label files found in the selected directory.", "Label Import", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            CancellationTokenSource tokenSource = new CancellationTokenSource();
            overlayManager.ShowOverlayWithProgress("Importing YOLO labels...", tokenSource);

            int labelsLoaded = 0;

            await Task.Run(() =>
            {
                foreach (string labelFile in labelFiles)
                {
                    if (tokenSource.Token.IsCancellationRequested) break;

                    string imageName = Path.GetFileNameWithoutExtension(labelFile);
                    string matchingImagePath = imageManager.ImagePathMap.Values
                        .FirstOrDefault(img => Path.GetFileNameWithoutExtension(img.Path) == imageName)?.Path;

                    if (!string.IsNullOrEmpty(matchingImagePath))
                    {
                        labelsLoaded += labelManager.LoadYoloLabels(labelFile, matchingImagePath, imageManager);
                    }

                    processedFiles++;
                    Dispatcher.Invoke(() =>
                    {
                        overlayManager.UpdateProgress((processedFiles * 100) / totalFiles);
                        overlayManager.UpdateMessage($"Importing labels {processedFiles}/{totalFiles}...");
                    });
                }
            }, tokenSource.Token);

            overlayManager.HideOverlay();

            MessageBox.Show($"YOLO labels imported: {labelsLoaded}", "YOLO Label Import", MessageBoxButton.OK, MessageBoxImage.Information);

            // Update all image statuses after importing labels
            UpdateAllImageStatuses();

            if (!string.IsNullOrEmpty(imageManager.CurrentImagePath) && labelManager.LabelStorage.ContainsKey(imageManager.CurrentImagePath))
            {
                drawingCanvas.Labels = labelManager.LabelStorage[imageManager.CurrentImagePath];
                RefreshLabelList();

                // Restore selection if it was lost
                if (currentSelection != null && ImageListBox.SelectedItem == null)
                {
                    for (int i = 0; i < ImageListBox.Items.Count; i++)
                    {
                        if (ImageListBox.Items[i] is ImageListItem item &&
                            item.FileName == currentSelection.FileName)
                        {
                            ImageListBox.SelectedIndex = i;
                            ImageListBox.ScrollIntoView(ImageListBox.SelectedItem);
                            break;
                        }
                    }
                }

                OnLabelsChanged();
                drawingCanvas.InvalidateVisual();
            }
        }

        private async Task ExportLabelsToYoloAsync(string exportDirectory)
        {
            if (string.IsNullOrEmpty(exportDirectory)) return;

            CancellationTokenSource tokenSource = new CancellationTokenSource();
            overlayManager.ShowOverlayWithProgress("Exporting labels...", tokenSource);

            List<string> labelFilesToUpload = new List<string>();
            List<string> imageFilesToUpload = imageManager.GetAllImagePaths();

            int exportedFiles = 0;
            int totalFiles = labelManager.LabelStorage.Count;
            int processedFiles = 0;

            bool canceled = await Task.Run(() =>
            {
                foreach (var kvp in labelManager.LabelStorage)
                {
                    if (tokenSource.Token.IsCancellationRequested) return true;

                    string fileName = kvp.Key; // This is just the filename
                    if (!imageManager.ImagePathMap.TryGetValue(fileName, out ImageManager.ImageInfo imageInfo))
                    {
                        continue; // Skip if full path is not found
                    }

                    string imagePath = imageInfo.Path; // Get full image path
                    if (!File.Exists(imagePath)) continue;

                    List<LabelData> labels = kvp.Value;
                    if (labels.Count == 0) continue;

                    string labelFilePath = Path.Combine(exportDirectory, Path.GetFileNameWithoutExtension(imagePath) + ".txt");

                    try
                    {
                        labelManager.ExportLabelsToYolo(labelFilePath, imagePath, labels);
                        labelFilesToUpload.Add(labelFilePath);
                        exportedFiles++;
                    }
                    catch (Exception ex)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            MessageBox.Show($"Failed to export labels for {fileName}.\n\nError: {ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        });
                    }

                    processedFiles++;
                    Dispatcher.Invoke(() =>
                    {
                        overlayManager.UpdateProgress((processedFiles * 100) / totalFiles);
                        overlayManager.UpdateMessage($"Exporting {processedFiles}/{totalFiles} files...");
                    });
                }
                return false;
            });

            if (canceled)
            {
                overlayManager.HideOverlay();
                MessageBox.Show("Export canceled by user.", "Export Canceled", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Commented out cloud upload functionality for now
            /*
            if (Properties.Settings.Default.AskForUpload)
            {
                overlayManager.UpdateMessage("Export complete. Uploading dataset...");
                await cloudUploader.AskForUploadAsync(labelFilesToUpload, imageFilesToUpload);
            }
            */

            overlayManager.HideOverlay();
            MessageBox.Show($"Labels exported successfully!", "Process Complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void UpdateImageStatus(string fileName)
        {
            if (!imageManager.ImagePathMap.ContainsKey(fileName)) return;

            var hasLabels = labelManager.LabelStorage.ContainsKey(fileName) && labelManager.LabelStorage[fileName].Any();
            var isImportedOrAI = hasLabels && labelManager.LabelStorage[fileName].Any(l => l.Name.StartsWith("Imported") || l.Name.StartsWith("AI"));

            ImageStatus newStatus;
            if (!hasLabels)
                newStatus = ImageStatus.NoLabel;
            else if (isImportedOrAI)
                newStatus = ImageStatus.VerificationNeeded;
            else
                newStatus = ImageStatus.Verified;

            // Use the imageManager method to update status (we'll add this method)
            imageManager.UpdateImageStatusValue(fileName, newStatus);

            // Update the existing ListBox item instead of replacing it
            foreach (var item in ImageListBox.Items)
            {
                if (item is ImageListItem imageItem && imageItem.FileName == fileName)
                {
                    imageItem.Status = newStatus;
                    break;
                }
            }
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
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

        private void SettingsMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var settingsWindow = new SettingsWindow();
            // Show all settings groups when accessing from main menu
            settingsWindow.GeneralSettingsGroup.Visibility = Visibility.Visible;
            settingsWindow.PerformanceSettingsGroup.Visibility = Visibility.Visible;
            settingsWindow.Owner = this;
            settingsWindow.ShowDialog();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);
            yoloAI?.Dispose();
        }
    }
}