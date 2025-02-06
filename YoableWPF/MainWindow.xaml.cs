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

namespace YoableWPF
{
    public partial class MainWindow : Window
    {
        // Managers/Handlers
        private YoloAI yoloAI;
        private OverlayManager overlayManager;
        private CloudUploader cloudUploader;

        // Images and drawing
        private string currentImagePath = "";
        private Dictionary<string, ImageInfo> imagePathMap = new();

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

            // Check for updates from Github
            if (Properties.Settings.Default.CheckUpdatesOnLaunch)
            {
                var autoUpdater = new UpdateManager(this, overlayManager, "2.0.0"); // Current version
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


        private void ImageListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ImageListBox.SelectedItem is string fileName &&
             imagePathMap.TryGetValue(fileName, out ImageInfo imageInfo))
            {
                // Save existing labels
                if (!string.IsNullOrEmpty(currentImagePath))
                {
                    labelStorage[currentImagePath] = new List<LabelData>(drawingCanvas.Labels);
                }

                currentImagePath = fileName;
                drawingCanvas.LoadImage(imageInfo.Path, imageInfo.OriginalDimensions);

                // Load labels for this image if they exist
                if (labelStorage.ContainsKey(fileName))
                {
                    drawingCanvas.Labels = new List<LabelData>(labelStorage[fileName]);
                }
                else
                {
                    drawingCanvas.Labels.Clear();
                }

                // Update UI
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

        private void ClearAll_Click(object sender, RoutedEventArgs e)
        {
            imagePathMap.Clear();
            drawingCanvas.Labels.Clear(); // Clear labels inside DrawingCanvas
            ImageListBox.Items.Clear();
            LabelListBox.Items.Clear();
            drawingCanvas.Image = null; // Clear the image in DrawingCanvas
            drawingCanvas.InvalidateVisual(); // Force a redraw
        }

        private async void AutoLabelImages_Click(object sender, RoutedEventArgs e)
        {
            yoloAI.LoadYoloModel();

            if (yoloAI.yoloSession == null) return;  // Exit if model loading failed

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
                            drawingCanvas.Labels.Clear();
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

        private void LoadImages(string directoryPath)
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
        }

        private void AddImage(string filePath)
        {
            if (!File.Exists(filePath)) return;

            string fileName = Path.GetFileName(filePath);

            // Get image dimensions without loading the full image into memory
            using (var imageStream = File.OpenRead(filePath))
            {
                var decoder = BitmapDecoder.Create(
                    imageStream,
                    BitmapCreateOptions.None,
                    BitmapCacheOption.None);

                var dimensions = new Size(decoder.Frames[0].PixelWidth, decoder.Frames[0].PixelHeight);

                imagePathMap[fileName] = new ImageInfo(filePath, dimensions);
            }

            if (!ImageListBox.Items.Contains(fileName))
            {
                ImageListBox.Items.Add(fileName);
            }

            if (ImageListBox.Items.Count == 1)
            {
                ImageListBox.SelectedIndex = 0;
            }
        }

        private async Task LoadYOLOLabelsFromDirectory(string directoryPath)
        {
            if (!Directory.Exists(directoryPath)) return;

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

            overlayManager.UpdateMessage("Export complete. Uploading dataset...");
            bool uploadSuccess = await cloudUploader.AskForUploadAsync(labelFilesToUpload, imageFilesToUpload);

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

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            OnKeyDown(e);
        }

        // Same as DrawingCanvas, we need in both to support Label Listbox select and move/delete.
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            if (drawingCanvas.SelectedLabel != null)
            {
                int moveAmount = 1; // Amount of pixels to move

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
                        // Remove the selected label from canvas and list
                        drawingCanvas.Labels.Remove(drawingCanvas.SelectedLabel);
                        LabelListBox.Items.Remove(drawingCanvas.SelectedLabel.Name);
                        drawingCanvas.SelectedLabel = null;
                        break;
                }

                drawingCanvas.InvalidateVisual(); // Refresh canvas
                e.Handled = true;
            }
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
