using Microsoft.Win32;
using ModernWpf;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using YoableWPF.Managers;
using Size = System.Windows.Size;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using System.Diagnostics;
using System.Globalization;
using System.Windows.Media;

namespace YoableWPF
{
    public class ImageListItem
    {
        public string FileName { get; set; }
        public ImageStatus Status { get; set; }

        public ImageListItem(string fileName, ImageStatus status)
        {
            FileName = fileName;
            Status = status;
        }

        public override string ToString()
        {
            return FileName;
        }
    }

    public enum ImageStatus
    {
        NoLabel,
        VerificationNeeded,
        Verified
    }

    public partial class MainWindow : Window
    {
        // Managers/Handlers
        private YoloAI yoloAI;
        private OverlayManager overlayManager;
        private CloudUploader cloudUploader;
        private YoutubeDownloader youtubeDownloader;

        // Images and drawing
        private string currentImagePath = "";
        private Dictionary<string, ImageInfo> imagePathMap = new();
        private Dictionary<string, ImageStatus> imageStatuses = new();

        // Image info stored for scaling and ect
        private class ImageInfo
        {
            public string Path { get; set; }
            public Size OriginalDimensions { get; set; }

            public ImageInfo(string path, Size dimensions)
            {
                Path = path;
                OriginalDimensions = dimensions;
            }
        }

        // Labeling
        private Dictionary<string, List<LabelData>> labelStorage = new();

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

            yoloAI = new YoloAI();
            overlayManager = new OverlayManager(this);
            cloudUploader = new CloudUploader(this, overlayManager);
            youtubeDownloader = new YoutubeDownloader(this, overlayManager);

            // Check for updates from Github
            if (Properties.Settings.Default.CheckUpdatesOnLaunch)
            {
                var autoUpdater = new UpdateManager(this, overlayManager, "2.2.0"); // Current version
                autoUpdater.CheckForUpdatesAsync();
            }
        }

        private void RefreshLabelList()
        {
            LabelListBox.Items.Clear();
            foreach (var label in drawingCanvas.Labels)
            {
                LabelListBox.Items.Add(label.Name);
            }
        }

        public void OnLabelsChanged()
        {
            if (string.IsNullOrEmpty(currentImagePath)) return;

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
                    drawingCanvas.Image.ToString().Contains(currentImagePath))
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

            imageStatuses[currentImagePath] = newStatus;

            // Update ListBox item while preserving selection
            ImageListBox.SelectionChanged -= ImageListBox_SelectionChanged;

            // Find the item in the ListBox
            for (int i = 0; i < ImageListBox.Items.Count; i++)
            {
                if (ImageListBox.Items[i] is ImageListItem item && item.FileName == currentImagePath)
                {
                    ImageListBox.Items.RemoveAt(i);
                    ImageListBox.Items.Insert(i, new ImageListItem(currentImagePath, newStatus));
                    ImageListBox.SelectedIndex = i;
                    break;
                }
            }

            ImageListBox.SelectionChanged += ImageListBox_SelectionChanged;
            UpdateStatusCounts();
        }

        private void ImageListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ImageListBox.SelectedItem is ImageListItem selected &&
                imagePathMap.TryGetValue(selected.FileName, out ImageInfo imageInfo))
            {
                // Save existing labels
                if (!string.IsNullOrEmpty(currentImagePath))
                {
                    labelStorage[currentImagePath] = new List<LabelData>(drawingCanvas.Labels);
                }

                currentImagePath = selected.FileName;
                drawingCanvas.LoadImage(imageInfo.Path, imageInfo.OriginalDimensions);

                // Reset zoom on image change
                drawingCanvas.ResetZoom();

                // Load labels for this image if they exist
                if (labelStorage.ContainsKey(selected.FileName))
                {
                    drawingCanvas.Labels = new List<LabelData>(labelStorage[selected.FileName]);

                    // Update status when viewing an image with AI or imported labels
                    if (labelStorage[selected.FileName].Any(l => l.Name.StartsWith("AI") || l.Name.StartsWith("Imported")))
                    {
                        // Only update to Verified if it was previously VerificationNeeded
                        if (imageStatuses[selected.FileName] == ImageStatus.VerificationNeeded)
                        {
                            imageStatuses[selected.FileName] = ImageStatus.Verified;

                            // Find the current item's index
                            var currentIndex = ImageListBox.SelectedIndex;
                            if (currentIndex >= 0)
                            {
                                // Temporarily disable SelectionChanged event
                                ImageListBox.SelectionChanged -= ImageListBox_SelectionChanged;

                                // Update the item
                                ImageListBox.Items.RemoveAt(currentIndex);
                                ImageListBox.Items.Insert(currentIndex, new ImageListItem(selected.FileName, ImageStatus.Verified));

                                // Restore selection and scroll position
                                ImageListBox.SelectedIndex = currentIndex;
                                ImageListBox.ScrollIntoView(ImageListBox.SelectedItem);

                                // Re-enable SelectionChanged event
                                ImageListBox.SelectionChanged += ImageListBox_SelectionChanged;
                            }
                        }
                    }
                }
                else
                {
                    drawingCanvas.Labels.Clear();
                }

                // Update UI
                UpdateStatusCounts();
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
            foreach (var fileName in imagePathMap.Keys.ToList())
            {
                UpdateImageStatus(fileName);
            }
        }

        private void ClearAll_Click(object sender, RoutedEventArgs e)
        {
            imagePathMap.Clear();
            imageStatuses.Clear();
            labelStorage.Clear();
            drawingCanvas.Labels.Clear(); // Clear labels inside DrawingCanvas
            ImageListBox.Items.Clear();
            LabelListBox.Items.Clear();
            drawingCanvas.Image = null; // Clear the image in DrawingCanvas
            drawingCanvas.InvalidateVisual(); // Force a redraw
            UpdateStatusCounts();
        }

        private async void AutoLabelImages_Click(object sender, RoutedEventArgs e)
        {
            yoloAI.LoadYoloModel();
            if (yoloAI.yoloSession == null) return;  // Exit if model loading failed

            // Store current selection
            var currentSelection = ImageListBox.SelectedItem as ImageListItem;

            CancellationTokenSource tokenSource = new CancellationTokenSource();
            overlayManager.ShowOverlayWithProgress("Running AI Detections...", tokenSource);

            int totalDetections = 0;
            int totalImages = imagePathMap.Count;
            int processedImages = 0;

            await Task.Run(() =>
            {
                foreach (var imagePath in imagePathMap.Values)
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

                    lock (labelStorage)
                    {
                        if (!labelStorage.ContainsKey(fileName))
                            labelStorage[fileName] = new List<LabelData>();

                        foreach (var box in detectedBoxes)
                        {
                            var labelCount = labelStorage[fileName].Count + 1;
                            var label = new LabelData($"AI Label {labelCount}", new Rect(box.X, box.Y, box.Width, box.Height));
                            labelStorage[fileName].Add(label);
                        }

                        totalDetections += detectedBoxes.Count;
                    }

                    Dispatcher.Invoke(() =>
                    {
                        if (drawingCanvas != null && drawingCanvas.Image != null &&
                            drawingCanvas.Image.ToString().Contains(fileName))
                        {
                            drawingCanvas.Labels.AddRange(labelStorage[fileName]);
                            drawingCanvas.LabelListBox?.Items.Clear();
                            foreach (var label in labelStorage[fileName])
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
                        overlayManager.UpdateMessage($"Processing image {processedImages}/{totalImages}...");
                    });
                }
            }, tokenSource.Token);

            yoloAI.yoloSession.Dispose();
            yoloAI.yoloSession = null;

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
                UpdateStatusCounts();
                MessageBox.Show($"Auto-labeling complete, total detections: {totalDetections}", "AI Labels", MessageBoxButton.OK, MessageBoxImage.Information);
            });
        }

        private void AutoSuggestLabels_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Auto Suggest Labels - Not Implemented Yet");
        }

        private void AISettings_Click(object sender, RoutedEventArgs e)
        {
            var settingsWindow = new SettingsWindow();
            settingsWindow.AISettingsGroup.Visibility = Visibility.Visible;
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

        public void LoadImages(string directoryPath)
        {
            if (!Directory.Exists(directoryPath)) return;

            string[] files = Directory.GetFiles(directoryPath, "*.*")
                                      .Where(f => f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                                      .ToArray();

            ImageListBox.Items.Clear();
            imagePathMap.Clear();

            foreach (string file in files)
            {
                AddImage(file);
            }

            if (ImageListBox.Items.Count > 0)
            {
                ImageListBox.SelectedIndex = 0;
            }

            UpdateStatusCounts();
        }

        private void AddImage(string filePath)
        {
            if (!File.Exists(filePath)) return;

            string fileName = Path.GetFileName(filePath);

            using (var imageStream = File.OpenRead(filePath))
            {
                var decoder = BitmapDecoder.Create(
                    imageStream,
                    BitmapCreateOptions.None,
                    BitmapCacheOption.None);

                var dimensions = new Size(decoder.Frames[0].PixelWidth, decoder.Frames[0].PixelHeight);
                imagePathMap[fileName] = new ImageInfo(filePath, dimensions);
                imageStatuses[fileName] = ImageStatus.NoLabel;
            }

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
                    string matchingImagePath = imagePathMap.Values
                        .FirstOrDefault(img => Path.GetFileNameWithoutExtension(img.Path) == imageName)?.Path;

                    if (!string.IsNullOrEmpty(matchingImagePath))
                    {
                        labelsLoaded += LoadYoloLabels(labelFile, matchingImagePath);
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

            if (!string.IsNullOrEmpty(currentImagePath) && labelStorage.ContainsKey(currentImagePath))
            {
                drawingCanvas.Labels = labelStorage[currentImagePath];
                RefreshLabelList();
                UpdateAllImageStatuses();
                UpdateStatusCounts();

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
            List<string> imageFilesToUpload = imagePathMap.Values.Select(img => img.Path).ToList();

            int exportedFiles = 0;
            int totalFiles = labelStorage.Count;
            int processedFiles = 0;

            bool canceled = await Task.Run(() =>
            {
                foreach (var kvp in labelStorage)
                {
                    if (tokenSource.Token.IsCancellationRequested) return true;

                    string fileName = kvp.Key; // This is just the filename
                    if (!imagePathMap.TryGetValue(fileName, out ImageInfo imageInfo))
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
                        ExportLabelsToYolo(labelFilePath, imagePath, labels);
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

            if (Properties.Settings.Default.AskForUpload)
            {
                overlayManager.UpdateMessage("Export complete. Uploading dataset...");
                await cloudUploader.AskForUploadAsync(labelFilesToUpload, imageFilesToUpload);
            }

            overlayManager.HideOverlay();
            MessageBox.Show($"Labels exported successfully!", "Process Complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }


        private int LoadYoloLabels(string labelFile, string imagePath)
        {
            if (!File.Exists(labelFile)) return 0;

            string fileName = Path.GetFileName(imagePath);

            // Ensure the correct image path
            if (!imagePathMap.TryGetValue(Path.GetFileName(imagePath), out ImageInfo imageInfo))
            {
                MessageBox.Show($"Image not found for label file: {imagePath}", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return 0;
            }
            imagePath = imageInfo.Path;

            int labelsAdded = 0;

            try
            {
                using (FileStream fs = new FileStream(imagePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (Bitmap tempImage = new Bitmap(fs))
                {
                    int imgWidth = tempImage.Width;
                    int imgHeight = tempImage.Height;

                    if (!labelStorage.ContainsKey(fileName))
                        labelStorage[fileName] = new List<LabelData>();

                    using StreamReader reader = new StreamReader(labelFile);
                    string? line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        string[] parts = line.Trim().Split(' ');
                        if (parts.Length != 5) continue;

                        float xCenter = float.Parse(parts[1], CultureInfo.InvariantCulture) * imgWidth;
                        float yCenter = float.Parse(parts[2], CultureInfo.InvariantCulture) * imgHeight;
                        float width = float.Parse(parts[3], CultureInfo.InvariantCulture) * imgWidth;
                        float height = float.Parse(parts[4], CultureInfo.InvariantCulture) * imgHeight;

                        double x = xCenter - (width / 2);
                        double y = yCenter - (height / 2);

                        var labelCount = labelStorage[fileName].Count + 1;
                        var label = new LabelData($"Imported Label {labelCount}", new Rect(x, y, width, height));
                        labelStorage[fileName].Add(label);

                        labelsAdded++;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading labels from {labelFile}:\n{ex.Message}", "Label Load Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            return labelsAdded;
        }

        private void ExportLabelsToYolo(string filePath, string imagePath, List<LabelData> labelsToExport)
        {
            using Bitmap image = new Bitmap(imagePath);
            int imageWidth = image.Width;
            int imageHeight = image.Height;

            using StreamWriter writer = new(filePath)
            {
                AutoFlush = true
            };

            foreach (var label in labelsToExport)
            {
                float x_center = (float)((label.Rect.X + label.Rect.Width / 2f) / imageWidth);
                float y_center = (float)(label.Rect.Y + label.Rect.Height / 2f) / imageHeight;
                float width = (float)label.Rect.Width / (float)imageWidth;
                float height = (float)label.Rect.Height / (float)imageHeight;

                writer.WriteLine($"0 {x_center:F6} {y_center:F6} {width:F6} {height:F6}");
            }
        }

        private void UpdateStatusCounts()
        {
            var needsReview = ImageListBox.Items.Cast<ImageListItem>()
                .Count(x => x.Status == ImageStatus.VerificationNeeded);
            var unverified = ImageListBox.Items.Cast<ImageListItem>()
                .Count(x => x.Status == ImageStatus.NoLabel);

            // Update the text blocks with counts
            NeedsReviewCount.Text = needsReview > 0
                ? $"{needsReview} need{(needsReview == 1 ? "s" : "")} review"
                : "0 need review";
            NeedsReviewCount.Foreground = needsReview > 0
                ? (SolidColorBrush)(new BrushConverter().ConvertFrom("#CC7A00"))
                : NeedsReviewCount.Foreground;

            UnverifiedCount.Text = unverified > 0
                ? $"{unverified} unverified"
                : "0 unverified";
            UnverifiedCount.Foreground = unverified > 0
                ? (SolidColorBrush)(new BrushConverter().ConvertFrom("#CC3300"))
                : UnverifiedCount.Foreground;
        }

        private void UpdateImageStatus(string fileName)
        {
            if (!imagePathMap.ContainsKey(fileName)) return;

            var hasLabels = labelStorage.ContainsKey(fileName) && labelStorage[fileName].Any();
            var isImportedOrAI = hasLabels && labelStorage[fileName].Any(l => l.Name.StartsWith("Imported") || l.Name.StartsWith("AI"));

            ImageStatus newStatus;
            if (!hasLabels)
                newStatus = ImageStatus.NoLabel;
            else if (isImportedOrAI)
                newStatus = ImageStatus.VerificationNeeded;
            else
                newStatus = ImageStatus.Verified;

            imageStatuses[fileName] = newStatus;

            // Refresh ListBox item
            var index = -1;
            for (int i = 0; i < ImageListBox.Items.Count; i++)
            {
                if (ImageListBox.Items[i] is ImageListItem item && item.FileName == fileName)
                {
                    index = i;
                    break;
                }
            }

            if (index >= 0)
            {
                ImageListBox.Items.RemoveAt(index);
                ImageListBox.Items.Insert(index, new ImageListItem(fileName, newStatus));
            }
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            OnPreviewKeyDown(e);
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);

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

                // Ensure the selected label is updated in DrawingCanvas
                string selectedLabelName = LabelListBox.SelectedItem as string;
                if (!string.IsNullOrEmpty(selectedLabelName))
                {
                    drawingCanvas.SelectedLabel = drawingCanvas.Labels.FirstOrDefault(label => label.Name == selectedLabelName);
                    drawingCanvas.InvalidateVisual();
                }

                // Force focus back to the window to keep key events working
                this.Focus();
                Keyboard.Focus(this);

                e.Handled = true;
                return; // Prevent further processing
            }

            // Handle label movement and deletion
            if (drawingCanvas.SelectedLabel != null)
            {
                int moveAmount = 1;

                switch (e.Key)
                {
                    case Key.Up:
                        drawingCanvas.SelectedLabel.Rect = new Rect(drawingCanvas.SelectedLabel.Rect.X, drawingCanvas.SelectedLabel.Rect.Y - moveAmount, drawingCanvas.SelectedLabel.Rect.Width, drawingCanvas.SelectedLabel.Rect.Height);
                        break;

                    case Key.Down:
                        drawingCanvas.SelectedLabel.Rect = new Rect(drawingCanvas.SelectedLabel.Rect.X, drawingCanvas.SelectedLabel.Rect.Y + moveAmount, drawingCanvas.SelectedLabel.Rect.Width, drawingCanvas.SelectedLabel.Rect.Height);
                        break;

                    case Key.Left:
                        drawingCanvas.SelectedLabel.Rect = new Rect(drawingCanvas.SelectedLabel.Rect.X - moveAmount, drawingCanvas.SelectedLabel.Rect.Y, drawingCanvas.SelectedLabel.Rect.Width, drawingCanvas.SelectedLabel.Rect.Height);
                        break;

                    case Key.Right:
                        drawingCanvas.SelectedLabel.Rect = new Rect(drawingCanvas.SelectedLabel.Rect.X + moveAmount, drawingCanvas.SelectedLabel.Rect.Y, drawingCanvas.SelectedLabel.Rect.Width, drawingCanvas.SelectedLabel.Rect.Height);
                        break;

                    case Key.Delete:
                        drawingCanvas.Labels.Remove(drawingCanvas.SelectedLabel);
                        LabelListBox.Items.Remove(drawingCanvas.SelectedLabel.Name);
                        drawingCanvas.SelectedLabel = null;
                        if (Application.Current.MainWindow is MainWindow mainWindow)
                        {
                            mainWindow.OnLabelsChanged();
                        }
                        break;
                }

                drawingCanvas.InvalidateVisual();
                e.Handled = true;
            }
        }

        private void SortByName_Click(object sender, RoutedEventArgs e)
        {
            var items = ImageListBox.Items.Cast<ImageListItem>().ToList();
            var selectedItem = ImageListBox.SelectedItem as ImageListItem;

            // Sort by filename
            var sorted = items.OrderBy(x => x.FileName).ToList();

            // Update ListBox while preserving selection
            ImageListBox.SelectionChanged -= ImageListBox_SelectionChanged;
            ImageListBox.Items.Clear();
            foreach (var item in sorted)
            {
                ImageListBox.Items.Add(item);
            }

            // Restore selection
            if (selectedItem != null)
            {
                for (int i = 0; i < ImageListBox.Items.Count; i++)
                {
                    if (ImageListBox.Items[i] is ImageListItem item &&
                        item.FileName == selectedItem.FileName)
                    {
                        ImageListBox.SelectedIndex = i;
                        ImageListBox.ScrollIntoView(ImageListBox.SelectedItem);
                        break;
                    }
                }
            }
            ImageListBox.SelectionChanged += ImageListBox_SelectionChanged;
        }

        private void SortByStatus_Click(object sender, RoutedEventArgs e)
        {
            var items = ImageListBox.Items.Cast<ImageListItem>().ToList();
            var selectedItem = ImageListBox.SelectedItem as ImageListItem;

            // Custom sort order: VerificationNeeded first, then NoLabel, then Verified
            var sorted = items.OrderBy(x => {
                switch (x.Status)
                {
                    case ImageStatus.VerificationNeeded: return 0;
                    case ImageStatus.NoLabel: return 1;
                    case ImageStatus.Verified: return 2;
                    default: return 3;
                }
            }).ThenBy(x => x.FileName).ToList();

            // Update ListBox while preserving selection
            ImageListBox.SelectionChanged -= ImageListBox_SelectionChanged;
            ImageListBox.Items.Clear();
            foreach (var item in sorted)
            {
                ImageListBox.Items.Add(item);
            }

            // Restore selection
            if (selectedItem != null)
            {
                for (int i = 0; i < ImageListBox.Items.Count; i++)
                {
                    if (ImageListBox.Items[i] is ImageListItem item &&
                        item.FileName == selectedItem.FileName)
                    {
                        ImageListBox.SelectedIndex = i;
                        ImageListBox.ScrollIntoView(ImageListBox.SelectedItem);
                        break;
                    }
                }
            }
            ImageListBox.SelectionChanged += ImageListBox_SelectionChanged;
        }

        private void SettingsMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var settingsWindow = new SettingsWindow();
            settingsWindow.GeneralSettingsGroup.Visibility = Visibility.Visible;
            settingsWindow.Owner = this;
            settingsWindow.ShowDialog();
        }
    }
}
