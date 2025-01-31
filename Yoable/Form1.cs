using Dark.Net;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Yoable;

namespace Yoble
{
    public partial class Form1 : Form
    {

        // Managers/Handlers
        private YoloAI yoloAI;
        private ThemeManager themeManager;
        private OverlayManager overlayManager;

        // Images and drawing
        private string currentImagePath = "";
        private string exportDirectory = ""; // Stores the user-selected directory
        private Dictionary<string, string> imagePathMap = new(); // Image paths to get name
        private bool isDrawing = false;
        private Point startPoint;
        private Rectangle currentRect;

        // Crosshair
        private Point cursorPosition = Point.Empty;
        private int crosshairSize = 1;
        private Color crosshairColor = Color.Black;

        // Zoom
        private float zoomFactor = 1.0f;
        private const float zoomStep = 0.1f;
        private Point zoomCenter = Point.Empty;
        private Matrix transformMatrix = new Matrix();

        // Labeling
        private List<LabelData> labels = new();
        private Dictionary<string, List<LabelData>> labelStorage = new();
        private LabelData selectedLabel = null;
        private bool isResizing = false;
        private bool isDragging = false;
        private int resizeHandleSize = 4;
        private Point dragStart;
        private ResizeHandleType resizeHandleType;

        private enum ResizeHandleType
        {
            None, TopLeft, TopRight, BottomLeft, BottomRight,
            Left, Right, Top, Bottom
        }

        private class LabelData
        {
            public string Name { get; set; }
            public Rectangle Rect { get; set; }
        }

        public Form1()
        {
            InitializeComponent();

            LoadedImage.MouseWheel += PictureBox_MouseWheel;

            // Load saved settings
            bool isDarkTheme = Yoable.Properties.Settings.Default.DarkTheme;
            float confidence = Yoable.Properties.Settings.Default.AIConfidence;

            yoloAI = new YoloAI(confidence);
            overlayManager = new OverlayManager(this);
            themeManager = new ThemeManager(this);

            // Apply Dark Theme
            themeManager.ToggleDarkMode(isDarkTheme);
            DarkThemeToolStrip.Checked = isDarkTheme;
            DarkNet.Instance.SetWindowThemeForms(this, Theme.Auto);
        }

        //----------------\\
        //-- Form EVENTS --\\
        //------------------\\

        private void RefreshLabelList()
        {
            LabelListBox.Items.Clear();

            foreach (var label in labels)
            {
                LabelListBox.Items.Add(label.Name);
            }

            LabelListBox.ClearSelected();
        }


        private void ImageListBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (ImageListBox.SelectedItem is string fileName && imagePathMap.ContainsKey(fileName))
            {
                // Store previous image labels before switching
                if (!string.IsNullOrEmpty(currentImagePath))
                {
                    labelStorage[currentImagePath] = new List<LabelData>(labels);
                }

                currentImagePath = imagePathMap[fileName];
                LoadedImage.Image = Image.FromFile(currentImagePath);

                // Load labels for the new selected image
                if (!labelStorage.ContainsKey(currentImagePath))
                {
                    labelStorage[currentImagePath] = new List<LabelData>();
                }

                labels = labelStorage[currentImagePath];

                // Deselect any selected label
                selectedLabel = null;
                LabelListBox.ClearSelected();

                // Refresh label list and image
                LabelListBox.Items.Clear();
                foreach (var label in labels)
                {
                    LabelListBox.Items.Add(label.Name);
                }

                // Reset zoom when switching images
                zoomFactor = 1.0f;
                transformMatrix.Reset();

                LoadedImage.Invalidate();
            }
        }

        private void LabelListBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            LoadedImage.Invalidate(); // Redraw image to highlight selected label
        }

        private void ImportDirectoryToolStrip_Click(object sender, EventArgs e)
        {
            using FolderBrowserDialog folderDialog = new();
            if (folderDialog.ShowDialog() == DialogResult.OK)
            {
                LoadImages(folderDialog.SelectedPath);
            }
        }

        private void ImportImageToolStrip_Click(object sender, EventArgs e)
        {
            using OpenFileDialog openFileDialog = new() { Filter = "Image Files|*.jpg;*.png" };
            if (openFileDialog.ShowDialog() == DialogResult.OK)
            {
                AddImage(openFileDialog.FileName);
            }
        }

        private void YOLOLabelImportToolStrip_Click(object sender, EventArgs e)
        {
            using FolderBrowserDialog folderDialog = new();
            folderDialog.Description = "Select YOLO Label Directory";

            if (folderDialog.ShowDialog() == DialogResult.OK)
            {
                LoadYOLOLabelsFromDirectory(folderDialog.SelectedPath);
            }
        }

        private void ExportLabelsToolStrip_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(exportDirectory))
            {
                using FolderBrowserDialog folderDialog = new();
                folderDialog.Description = "Select a directory to save YOLO labels";

                if (folderDialog.ShowDialog() == DialogResult.OK)
                {
                    exportDirectory = folderDialog.SelectedPath;
                }
                else
                {
                    return; // User canceled, so don't export
                }
            }

            int exportedFiles = 0;

            foreach (var kvp in labelStorage)
            {
                string imagePath = kvp.Key; // Get the original image path
                if (!File.Exists(imagePath)) continue; // Skip due to image file missing

                List<LabelData> labels = kvp.Value;
                if (labels.Count == 0) continue; // Skip images with no labels

                string imageName = Path.GetFileNameWithoutExtension(imagePath);
                string labelFilePath = Path.Combine(exportDirectory, imageName + ".txt");

                try
                {
                    ExportLabelsToYolo(labelFilePath, imagePath, labels);
                    exportedFiles++;
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to export labels for {imageName}.\n\nError: {ex.Message}", "Export Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }

            if (exportedFiles > 0)
            {
                MessageBox.Show($"Export complete! {exportedFiles} label files saved in {exportDirectory}.", "Export Successful", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                MessageBox.Show("No labels were found to export.", "Export Labels", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void ClearAllToolStrip_Click(object sender, EventArgs e)
        {
            // Confirm before clearing
            var result = MessageBox.Show("Are you sure you want to clear all images and labels?",
                                         "Clear All", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

            if (result == DialogResult.Yes)
            {
                // Reset everything
                imagePathMap.Clear();
                labelStorage.Clear();
                labels.Clear();
                currentImagePath = "";

                // Clear UI elements
                ImageListBox.Items.Clear();
                LabelListBox.Items.Clear();
                LoadedImage.Image = null;

                // Invalidate UI to reflect changes
                LoadedImage.Invalidate();
            }
        }

        private async void AutoLabelImagesToolStrip_Click(object sender, EventArgs e)
        {
            if (yoloAI.yoloSession == null)
                yoloAI.LoadYoloModel();

            if (yoloAI.yoloSession == null) return; // Exit if loading failed

            overlayManager.aiProcessingToken = new CancellationTokenSource();

            // Show overlay
            overlayManager.ShowOverlay();
            overlayManager.CenterOverlay();

            int totalDetections = 0;

            await Task.Run(() =>
            {
                foreach (var imagePath in imagePathMap.Values)
                {
                    if (overlayManager.aiProcessingToken.IsCancellationRequested) return;

                    using Bitmap? image = new Bitmap(imagePath);
                    List<Rectangle> detectedBoxes = yoloAI.RunInference(image);

                    lock (labelStorage)
                    {
                        if (!labelStorage.ContainsKey(imagePath))
                            labelStorage[imagePath] = new List<LabelData>();

                        foreach (var box in detectedBoxes)
                        {
                            labelStorage[imagePath].Add(new LabelData { Rect = box, Name = "AI Label" });
                        }

                        totalDetections += detectedBoxes.Count;
                    }
                }
            });

            // Hide overlay when done
            overlayManager.HideOverlay();

            MessageBox.Show($"Auto-labeling complete, total detections: {totalDetections}", "AI Labels", MessageBoxButtons.OK, MessageBoxIcon.Information);
            LoadedImage.Invalidate();
        }

        private void AutoSuggestLabelsToolStrip_Click(object sender, EventArgs e)
        {
            MessageBox.Show("Suggest labels is still a WIP");
            return;
        }

        private void AISettingsToolStrip_Click(object sender, EventArgs e)
        {
            using (var settingsForm = new AiSettings(yoloAI.confidenceThreshold))
            {
                if (settingsForm.ShowDialog() == DialogResult.OK)
                {
                    yoloAI.confidenceThreshold = settingsForm.ConfidenceThreshold;

                    // Save setting
                    Yoable.Properties.Settings.Default.AIConfidence = yoloAI.confidenceThreshold;
                    Yoable.Properties.Settings.Default.Save();

                    MessageBox.Show($"AI Confidence Threshold updated to: {(yoloAI.confidenceThreshold * 100):F0}%",
                                    "AI Settings Updated", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
        }

        private void DarkThemeToolStrip_Click(object sender, EventArgs e)
        {
            if (sender is ToolStripMenuItem menuItem)
            {
                menuItem.Checked = !menuItem.Checked;
                themeManager.ToggleDarkMode(menuItem.Checked);

                // Save setting
                Yoable.Properties.Settings.Default.DarkTheme = menuItem.Checked;
                Yoable.Properties.Settings.Default.Save();
            }
        }

        private void AboutUsToolStrip_Click(object sender, EventArgs e)
        {
            MessageBox.Show("- Yoable was created by Babyhamsta to help users label images faster for YOLO training.\n\n- Yoable is free to use and open source, if you paid for it you've been scammed.\n\n- Please check out our github for updates!", "About Yoable (V1.0)");
        }

        //---------------------------------\\
        //-- LABEL RESIZING, DRAWING, ECT --\\
        //-----------------------------------\\

        private void ResizeLabel(LabelData label, ResizeHandleType handleType, Point mousePos)
        {
            int x = label.Rect.X;
            int y = label.Rect.Y;
            int width = label.Rect.Width;
            int height = label.Rect.Height;

            switch (handleType)
            {
                case ResizeHandleType.TopLeft:
                    width += x - mousePos.X;
                    height += y - mousePos.Y;
                    x = mousePos.X;
                    y = mousePos.Y;
                    break;

                case ResizeHandleType.TopRight:
                    width = mousePos.X - x;
                    height += y - mousePos.Y;
                    y = mousePos.Y;
                    break;

                case ResizeHandleType.BottomLeft:
                    width += x - mousePos.X;
                    x = mousePos.X;
                    height = mousePos.Y - y;
                    break;

                case ResizeHandleType.BottomRight:
                    width = mousePos.X - x;
                    height = mousePos.Y - y;
                    break;

                case ResizeHandleType.Left:
                    width += x - mousePos.X;
                    x = mousePos.X;
                    break;

                case ResizeHandleType.Right:
                    width = mousePos.X - x;
                    break;

                case ResizeHandleType.Top:
                    height += y - mousePos.Y;
                    y = mousePos.Y;
                    break;

                case ResizeHandleType.Bottom:
                    height = mousePos.Y - y;
                    break;
            }

            label.Rect = new Rectangle(x, y, Math.Max(10, width), Math.Max(10, height));
        }

        private ResizeHandleType GetResizeHandle(Point mousePos, Rectangle rect)
        {
            int s = resizeHandleSize;
            Rectangle[] handles =
            {
                new Rectangle(rect.Left - s, rect.Top - s, s * 2, s * 2),   // Top Left
                new Rectangle(rect.Right - s, rect.Top - s, s * 2, s * 2),  // Top Right
                new Rectangle(rect.Left - s, rect.Bottom - s, s * 2, s * 2),// Bottom Left
                new Rectangle(rect.Right - s, rect.Bottom - s, s * 2, s * 2),// Bottom Right
                new Rectangle(rect.Left - s, rect.Top + rect.Height / 2 - s, s * 2, s * 2),  // Left
                new Rectangle(rect.Right - s, rect.Top + rect.Height / 2 - s, s * 2, s * 2), // Right
                new Rectangle(rect.Left + rect.Width / 2 - s, rect.Top - s, s * 2, s * 2),  // Top
                new Rectangle(rect.Left + rect.Width / 2 - s, rect.Bottom - s, s * 2, s * 2) // Bottom
            };

            ResizeHandleType[] types = { ResizeHandleType.TopLeft, ResizeHandleType.TopRight,
                ResizeHandleType.BottomLeft, ResizeHandleType.BottomRight,
                ResizeHandleType.Left, ResizeHandleType.Right,
                ResizeHandleType.Top, ResizeHandleType.Bottom };

            for (int i = 0; i < handles.Length; i++)
            {
                if (handles[i].Contains(mousePos))
                    return types[i];
            }

            return ResizeHandleType.None;
        }


        private void DrawResizeHandles(Graphics g, Rectangle rect)
        {
            int s = resizeHandleSize;
            Rectangle[] handles =
            {
                new Rectangle(rect.Left - s, rect.Top - s, s * 2, s * 2),   // Top Left
                new Rectangle(rect.Right - s, rect.Top - s, s * 2, s * 2),  // Top Right
                new Rectangle(rect.Left - s, rect.Bottom - s, s * 2, s * 2),// Bottom Left
                new Rectangle(rect.Right - s, rect.Bottom - s, s * 2, s * 2),// Bottom Right
                new Rectangle(rect.Left - s, rect.Top + rect.Height / 2 - s, s * 2, s * 2),  // Left
                new Rectangle(rect.Right - s, rect.Top + rect.Height / 2 - s, s * 2, s * 2), // Right
                new Rectangle(rect.Left + rect.Width / 2 - s, rect.Top - s, s * 2, s * 2),  // Top
                new Rectangle(rect.Left + rect.Width / 2 - s, rect.Bottom - s, s * 2, s * 2) // Bottom
            };

            foreach (var handle in handles)
            {
                g.FillRectangle(Brushes.Black, handle);
            }
        }

        private void DrawCrosshair(Graphics g)
        {
            if (cursorPosition == Point.Empty) return;

            using Pen crosshairPen = new Pen(crosshairColor, crosshairSize / zoomFactor);

            // Draw horizontal line
            g.DrawLine(crosshairPen, new Point(0, cursorPosition.Y), new Point(LoadedImage.Width, cursorPosition.Y));

            // Draw vertical line
            g.DrawLine(crosshairPen, new Point(cursorPosition.X, 0), new Point(cursorPosition.X, LoadedImage.Height));
        }

        // Convert mouse coordinates to actual image coordinates
        private Point TransformPoint(Point p)
        {
            Point[] points = { p };
            Matrix invertedMatrix = transformMatrix.Clone();
            invertedMatrix.Invert();
            invertedMatrix.TransformPoints(points);
            return new Point((int)points[0].X, (int)points[0].Y);
        }

        private void PictureBox_MouseWheel(object sender, MouseEventArgs e)
        {
            if (Control.ModifierKeys == Keys.Control && LoadedImage.Image != null)
            {
                float oldZoomFactor = zoomFactor;
                PointF mouseBeforeZoom = TransformPoint(e.Location);

                if (e.Delta > 0)
                {
                    zoomFactor *= 1 + zoomStep; // Zoom In
                }
                else if (e.Delta < 0)
                {
                    zoomFactor /= 1 + zoomStep; // Zoom Out
                }

                // Prevent zooming out beyond original size
                zoomFactor = Math.Max(1.0f, Math.Min(zoomFactor, 5.0f));

                // Reset transformation if zoomFactor is back to normal (prevents drifting)
                if (zoomFactor == 1.0f)
                {
                    transformMatrix.Reset();
                    LoadedImage.Invalidate();
                    return;
                }

                // Get new mouse position after zoom
                PointF mouseAfterZoom = TransformPoint(e.Location);

                // Gradually adjust translation instead of jumping
                float offsetX = mouseAfterZoom.X - mouseBeforeZoom.X;
                float offsetY = mouseAfterZoom.Y - mouseBeforeZoom.Y;

                transformMatrix.Translate(-mouseBeforeZoom.X, -mouseBeforeZoom.Y, MatrixOrder.Append);
                transformMatrix.Scale(zoomFactor / oldZoomFactor, zoomFactor / oldZoomFactor, MatrixOrder.Append);
                transformMatrix.Translate(mouseBeforeZoom.X - offsetX, mouseBeforeZoom.Y - offsetY, MatrixOrder.Append);

                // Smooth transition to the center when zooming out
                if (zoomFactor < oldZoomFactor && zoomFactor == 1.0f)
                {
                    transformMatrix.Reset(); // Ensure reset when fully zoomed out
                }

                LoadedImage.Invalidate();
            }
        }

        private void PictureBox_MouseDown(object sender, MouseEventArgs e)
        {
            if (LoadedImage.Image == null) return;
            Point imagePoint = TransformPoint(e.Location);

            bool clickedOnLabel = false;
            foreach (var label in labels)
            {
                resizeHandleType = GetResizeHandle(imagePoint, label.Rect);

                if (resizeHandleType != ResizeHandleType.None)
                {
                    selectedLabel = label;
                    isResizing = true;
                    dragStart = imagePoint;
                    clickedOnLabel = true;
                    break;
                }

                if (label.Rect.Contains(imagePoint))
                {
                    selectedLabel = label;
                    isDragging = true;
                    dragStart = imagePoint;
                    clickedOnLabel = true;
                    break;
                }
            }

            if (!clickedOnLabel)
            {
                isDrawing = true;
                startPoint = imagePoint;
                currentRect = new Rectangle(startPoint, Size.Empty);
                selectedLabel = null;
                LabelListBox.ClearSelected();
            }
        }

        private void PictureBox_MouseMove(object sender, MouseEventArgs e)
        {
            cursorPosition = TransformPoint(e.Location);
            LoadedImage.Invalidate();

            if (isDrawing)
            {
                currentRect = new Rectangle(
                    Math.Min(startPoint.X, cursorPosition.X),
                    Math.Min(startPoint.Y, cursorPosition.Y),
                    Math.Abs(cursorPosition.X - startPoint.X),
                    Math.Abs(cursorPosition.Y - startPoint.Y)
                );
                return;
            }

            if (selectedLabel == null) return;

            if (isResizing)
            {
                ResizeLabel(selectedLabel, resizeHandleType, TransformPoint(e.Location));
            }
            else if (isDragging)
            {
                int dx = cursorPosition.X - dragStart.X;
                int dy = cursorPosition.Y - dragStart.Y;
                selectedLabel.Rect = new Rectangle(selectedLabel.Rect.X + dx, selectedLabel.Rect.Y + dy,
                                                   selectedLabel.Rect.Width, selectedLabel.Rect.Height);
                dragStart = cursorPosition;
            }
        }

        private void PictureBox_MouseUp(object sender, MouseEventArgs e)
        {
            if (isDrawing)
            {
                isDrawing = false;
                if (currentRect.Width > 10 && currentRect.Height > 10)
                {
                    var newLabel = new LabelData { Rect = currentRect, Name = $"Label {labels.Count + 1}" };
                    labels.Add(newLabel);
                    LabelListBox.Items.Add(newLabel.Name);
                }
            }
            isDragging = false;
            isResizing = false;
        }

        private void PictureBox_Paint(object sender, PaintEventArgs e)
        {
            if (LoadedImage.Image == null) return;
            e.Graphics.Transform = transformMatrix;
            e.Graphics.DrawImage(LoadedImage.Image, new Rectangle(0, 0, LoadedImage.Image.Width, LoadedImage.Image.Height));
            using Pen pen = new Pen(Color.Red, 2 / zoomFactor);
            foreach (var label in labels)
            {
                e.Graphics.DrawRectangle(pen, label.Rect);
                if (selectedLabel == label) DrawResizeHandles(e.Graphics, label.Rect);
            }
            if (isDrawing) e.Graphics.DrawRectangle(pen, currentRect);
            DrawCrosshair(e.Graphics);
        }

        //--------------------------------------\\
        //-- Image Loading and Label Exporting --\\
        //----------------------------------------\\

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
                string fileName = Path.GetFileName(file);
                imagePathMap[fileName] = file;
                ImageListBox.Items.Add(fileName);
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
            imagePathMap[fileName] = filePath;

            if (!ImageListBox.Items.Contains(fileName))
            {
                ImageListBox.Items.Add(fileName);
            }

            if (ImageListBox.Items.Count == 1)
            {
                ImageListBox.SelectedIndex = 0;
            }
        }

        private List<LabelData> LoadYoloLabels(string labelFile, string imagePath)
        {
            List<LabelData> loadedLabels = new();

            if (!File.Exists(labelFile)) return loadedLabels;

            try
            {
                using StreamReader reader = new StreamReader(labelFile);
                string? line;
                Bitmap tempImage = new Bitmap(imagePath); // Get image dimensions
                int imgWidth = tempImage.Width;
                int imgHeight = tempImage.Height;

                while ((line = reader.ReadLine()) != null)
                {
                    string[] parts = line.Split(' ');
                    if (parts.Length != 5) continue; // Ensure correct format

                    // Parse YOLO format
                    float xCenter = float.Parse(parts[1]) * imgWidth;
                    float yCenter = float.Parse(parts[2]) * imgHeight;
                    float width = float.Parse(parts[3]) * imgWidth;
                    float height = float.Parse(parts[4]) * imgHeight;

                    int x = (int)(xCenter - width / 2);
                    int y = (int)(yCenter - height / 2);

                    loadedLabels.Add(new LabelData { Rect = new Rectangle(x, y, (int)width, (int)height), Name = $"Label {loadedLabels.Count + 1}" });
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading labels from {labelFile}:\n{ex.Message}", "Label Load Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            return loadedLabels;
        }


        private void LoadYOLOLabelsFromDirectory(string directoryPath)
        {
            if (!Directory.Exists(directoryPath)) return;

            string[] labelFiles = Directory.GetFiles(directoryPath, "*.txt");
            int labelsLoaded = 0;

            foreach (string labelFile in labelFiles)
            {
                string imageName = Path.GetFileNameWithoutExtension(labelFile);
                string matchingImagePath = imagePathMap.Values.FirstOrDefault(img => Path.GetFileNameWithoutExtension(img) == imageName);

                if (!string.IsNullOrEmpty(matchingImagePath))
                {
                    labelStorage[matchingImagePath] = LoadYoloLabels(labelFile, matchingImagePath);
                    labelsLoaded++;
                }
            }

            MessageBox.Show($"YOLO labels loaded: {labelsLoaded}", "YOLO Label Import", MessageBoxButtons.OK, MessageBoxIcon.Information);

            // Refresh current image if needed
            if (!string.IsNullOrEmpty(currentImagePath) && labelStorage.ContainsKey(currentImagePath))
            {
                labels = labelStorage[currentImagePath];
                LabelListBox.Items.Clear();
                foreach (var label in labels)
                {
                    LabelListBox.Items.Add(label.Name);
                }
                LoadedImage.Invalidate();
            }
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
                float x_center = (label.Rect.X + label.Rect.Width / 2f) / imageWidth;
                float y_center = (label.Rect.Y + label.Rect.Height / 2f) / imageHeight;
                float width = label.Rect.Width / (float)imageWidth;
                float height = label.Rect.Height / (float)imageHeight;

                writer.WriteLine($"0 {x_center:F6} {y_center:F6} {width:F6} {height:F6}");
            }
        }

        //---------------------------\\
        //-- Key capture / modifier --\\
        //-----------------------------\\
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (selectedLabel != null && labels.Contains(selectedLabel))
            {
                int moveAmount = 1; // Amount of pixels to move

                switch (keyData)
                {
                    case Keys.Up:
                        selectedLabel.Rect = new Rectangle(selectedLabel.Rect.X, selectedLabel.Rect.Y - moveAmount,
                                                           selectedLabel.Rect.Width, selectedLabel.Rect.Height);
                        break;

                    case Keys.Down:
                        selectedLabel.Rect = new Rectangle(selectedLabel.Rect.X, selectedLabel.Rect.Y + moveAmount,
                                                           selectedLabel.Rect.Width, selectedLabel.Rect.Height);
                        break;

                    case Keys.Left:
                        selectedLabel.Rect = new Rectangle(selectedLabel.Rect.X - moveAmount, selectedLabel.Rect.Y,
                                                           selectedLabel.Rect.Width, selectedLabel.Rect.Height);
                        break;

                    case Keys.Right:
                        selectedLabel.Rect = new Rectangle(selectedLabel.Rect.X + moveAmount, selectedLabel.Rect.Y,
                                                           selectedLabel.Rect.Width, selectedLabel.Rect.Height);
                        break;

                    case Keys.Delete:
                        labels.Remove(selectedLabel);
                        LabelListBox.Items.Remove(selectedLabel.Name);
                        selectedLabel = null;
                        LoadedImage.Invalidate();
                        return true; // Prevent default behavior
                }

                LoadedImage.Invalidate(); // Refresh the image
                return true; // Prevent ListBox from switching items
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}