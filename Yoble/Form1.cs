using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using Yoable;

namespace Yoble
{
    public partial class Form1 : Form
    {
        // Onnx Runtime
        private InferenceSession yoloSession;
        private List<string>? outputNames;
        private string yoloModelPath = "";
        private float confidenceThreshold = 0.5f; // Minimum confidence to accept a detection
        private bool isYoloV5 = true; // Flag to differentiate YOLOv5 vs YOLOv8
        private List<LabelData> suggestedLabels = new();
        private int TotalDetections = 0;

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

        // Labeling
        private List<LabelData> labels = new();
        private Dictionary<string, List<LabelData>> labelStorage = new(); // Labels per imageDrawResizeHandles
        private LabelData selectedLabel = null;  // The currently selected label
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
        }

        //-----------------\\
        //-- AI Inference --\\
        //-------------------\\

        Bitmap ConvertTo24bpp(Bitmap src)
        {
            Bitmap dst = new Bitmap(src.Width, src.Height, PixelFormat.Format24bppRgb);
            using (Graphics g = Graphics.FromImage(dst))
            {
                g.DrawImage(src, new Rectangle(0, 0, src.Width, src.Height));
            }
            return dst;
        }


        public static float[] BitmapToFloatArray(Bitmap image)
        {
            int height = image.Height;
            int width = image.Width;
            float[] result = new float[3 * height * width];
            float multiplier = 1.0f / 255.0f;

            Rectangle rect = new(0, 0, width, height);
            BitmapData bmpData = image.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);

            int stride = bmpData.Stride;
            int offset = stride - width * 3;

            try
            {
                unsafe
                {
                    byte* ptr = (byte*)bmpData.Scan0.ToPointer();
                    int baseIndex = 0;
                    for (int i = 0; i < height; i++)
                    {
                        for (int x = 0; x < width; x++)
                        {
                            result[baseIndex] = ptr[2] * multiplier; // R
                            result[height * width + baseIndex] = ptr[1] * multiplier; // G
                            result[2 * height * width + baseIndex] = ptr[0] * multiplier; // B
                            ptr += 3;
                            baseIndex++;
                        }
                        ptr += offset;
                    }
                }
            }
            finally
            {
                image.UnlockBits(bmpData);
            }

            return result;
        }

        private List<Rectangle> ApplyNMS(List<(Rectangle box, float confidence)> detections, float iouThreshold = 0.5f)
        {
            List<Rectangle> finalDetections = new();

            // Sort detections by confidence descending
            detections = detections.OrderByDescending(d => d.confidence).ToList();

            while (detections.Count > 0)
            {
                var best = detections[0];
                finalDetections.Add(best.box);

                detections = detections.Skip(1)
                    .Where(d => ComputeIoU(best.box, d.box) < iouThreshold)
                    .ToList();
            }

            return finalDetections;
        }

        private float ComputeIoU(Rectangle a, Rectangle b)
        {
            int x1 = Math.Max(a.Left, b.Left);
            int y1 = Math.Max(a.Top, b.Top);
            int x2 = Math.Min(a.Right, b.Right);
            int y2 = Math.Min(a.Bottom, b.Bottom);

            int intersectionArea = Math.Max(0, x2 - x1) * Math.Max(0, y2 - y1);
            int unionArea = a.Width * a.Height + b.Width * b.Height - intersectionArea;

            return unionArea == 0 ? 0 : (float)intersectionArea / unionArea;
        }

        private List<Rectangle> PostProcessYoloV5Output(Tensor<float> outputTensor, int imgWidth, int imgHeight)
        {
            List<(Rectangle box, float confidence)> detections = new();
            int numDetections = outputTensor.Dimensions[1]; // 25200 detections for YOLOv5

            for (int i = 0; i < numDetections; i++)
            {
                float objectness = outputTensor[0, i, 4]; // Objectness score
                float classConfidence = outputTensor[0, i, 5]; // Class confidence

                if (objectness < confidenceThreshold) continue; // Filter out weak detections

                // Extract normalized bounding box values
                float xCenter = outputTensor[0, i, 0];
                float yCenter = outputTensor[0, i, 1];
                float width = outputTensor[0, i, 2];
                float height = outputTensor[0, i, 3];

                // Convert to absolute coordinates
                float xMin = xCenter - width / 2;
                float yMin = yCenter - height / 2;
                float xMax = xCenter + width / 2;
                float yMax = yCenter + height / 2;

                // Ensure bounding boxes are within the image bounds
                if (xMin < 0 || xMax > imgWidth || yMin < 0 || yMax > imgHeight) continue;
                detections.Add((new Rectangle((int)xMin, (int)yMin, (int)(xMax - xMin), (int)(yMax - yMin)), objectness));
            }

            var finalBoxes = ApplyNMS(detections);
            TotalDetections = TotalDetections + finalBoxes.Count;

            return finalBoxes;
        }

        private List<Rectangle> PostProcessYoloV8Output(Tensor<float> outputTensor, int imgWidth, int imgHeight)
        {
            List<(Rectangle box, float confidence)> detections = new();
            int numDetections = outputTensor.Dimensions[2]; // Typically 8400 for YOLOv8

            for (int i = 0; i < numDetections; i++)
            {
                float objectness = outputTensor[0, 4, i]; // Confidence score
                //Debug.WriteLine($"new conf: {objectness}"); // bad numbers something is off
                if (objectness < confidenceThreshold) continue;

                // Extract normalized coordinates
                float xCenter = outputTensor[0, 0, i];
                float yCenter = outputTensor[0, 1, i];
                float width = outputTensor[0, 2, i];
                float height = outputTensor[0, 3, i];

                // Convert YOLO format (center-x, center-y, width, height) to (x_min, y_min, width, height)
                float xMin = xCenter - width / 2;
                float yMin = yCenter - height / 2;
                float xMax = xCenter + width / 2;
                float yMax = yCenter + height / 2;

                // Ignore if outside bounds of image
                if (xMin < 0 || xMax > imgWidth || yMin < 0 || yMax > imgHeight) continue;
                detections.Add((new Rectangle((int)xMin, (int)yMin, (int)(xMax - xMin), (int)(yMax - yMin)), objectness));
            }

            var finalBoxes = ApplyNMS(detections);
            TotalDetections = TotalDetections + finalBoxes.Count;

            return finalBoxes;
        }


        private int DetectYoloVersion()
        {
            try
            {
                var modelOutput = yoloSession.OutputMetadata.First();
                int[] shape = modelOutput.Value.Dimensions.ToArray();
                string shapeString = string.Join(", ", shape);

                if (shape.Length == 3)
                {
                    if (shape.SequenceEqual(new int[] { 1, 5, 8400 }))
                    {
                        return 8; // YOLOv8 detected
                    }

                    if (shape.SequenceEqual(new int[] { 1, 25200, 6 }))
                    {
                        return 5; // YOLOv5 detected
                    }

                    MessageBox.Show($"Unable to detect YOLO version from shape: {shapeString}", "YOLO Model Shape");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error detecting YOLO version: {ex.Message}", "Model Detection Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            return 0; // ERROR - Do not continue
        }

        private void LoadYoloModel()
        {
            using OpenFileDialog openFileDialog = new()
            {
                Filter = "ONNX Model Files|*.onnx",
                Title = "Select YOLO ONNX Model"
            };

            if (openFileDialog.ShowDialog() != DialogResult.OK)
                return; // User canceled

            if (!string.IsNullOrEmpty(yoloModelPath) && yoloModelPath != openFileDialog.FileName)
            {
                yoloSession?.Dispose();
            }

            yoloModelPath = openFileDialog.FileName;
            bool usingGPU = false; // Track if DirectML was successfully used

            try
            {
                var sessionOptions = new SessionOptions
                {
                    EnableCpuMemArena = true,
                    EnableMemoryPattern = true,
                    GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
                    ExecutionMode = ExecutionMode.ORT_PARALLEL
                };

                // Try DirectML first
                try
                {
                    sessionOptions.AppendExecutionProvider_DML();
                    yoloSession = new InferenceSession(yoloModelPath, sessionOptions);
                    usingGPU = true; // DirectML successfully loaded
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"DirectML is not supported on this system. Falling back to CPU.\nError: {ex.Message}", "DirectML Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);

                    sessionOptions.AppendExecutionProvider_CPU();
                    yoloSession = new InferenceSession(yoloModelPath, sessionOptions);
                    outputNames = new List<string>(yoloSession.OutputMetadata.Keys);
                }

                // Detect model type
                int modelVersion = DetectYoloVersion();
                if (modelVersion == 0) // Detection failed
                {
                    yoloSession.Dispose();
                    yoloSession = null;
                    MessageBox.Show("Failed to detect YOLO model version. Please select a valid YOLOv5 or YOLOv8 ONNX model.", "Model Load Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                isYoloV5 = (modelVersion == 5);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load model.\nError: {ex.Message}", "Model Load Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private List<Rectangle> RunYoloInference(Bitmap? image)
        {
            Bitmap image24 = ConvertTo24bpp(image);
            float[] inputArray = BitmapToFloatArray(image24);
            if (inputArray == null) return null;

            Tensor<float> inputTensor = new DenseTensor<float>(inputArray, new int[] { 1, 3, image.Height, image.Width });
            var inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor("images", inputTensor) };

            using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results = yoloSession.Run(inputs, outputNames);

            // Extract the first output tensor
            var outputTensor = results.First().AsTensor<float>();

            // Use the correct post-processing based on the model type
            List<Rectangle> detections = isYoloV5
             ? PostProcessYoloV5Output(outputTensor, image.Width, image.Height)
             : PostProcessYoloV8Output(outputTensor, image.Width, image.Height);

            // Refresh the UI
            Invoke(new Action(() =>
            {
                labels = labelStorage[currentImagePath]; // Sync labels
                RefreshLabelList(); // Update the ListBox
            }));

            return detections;
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
                    labelStorage[currentImagePath] = new List<LabelData>(labels);

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

        private void AutoLabelImagesToolStrip_Click(object sender, EventArgs e)
        {

            // Reset on each run
            TotalDetections = 0;

            if (yoloSession == null)
                LoadYoloModel();

            if (yoloSession == null) return; // Exit if loading failed

            foreach (var imagePath in imagePathMap.Values)
            {
                Bitmap? image = new Bitmap(imagePath);
                List<Rectangle> detectedBoxes = RunYoloInference(image);

                if (!labelStorage.ContainsKey(imagePath))
                    labelStorage[imagePath] = new List<LabelData>();

                foreach (var box in detectedBoxes)
                {
                    labelStorage[imagePath].Add(new LabelData { Rect = box, Name = "AI Label" });
                }
            }

            MessageBox.Show($"Auto-labeling complete, total detections: {TotalDetections}", "AI Labels", MessageBoxButtons.OK, MessageBoxIcon.Information);
            LoadedImage.Invalidate();
        }

        private void AutoSuggestLabelsToolStrip_Click(object sender, EventArgs e)
        {
            MessageBox.Show("Suggest labels is still a WIP");
            return;
            if (string.IsNullOrEmpty(currentImagePath)) return;

            if (yoloSession == null)
                LoadYoloModel();

            if (yoloSession == null) return; // Exit if loading failed

            Bitmap? image = new Bitmap(currentImagePath);
            suggestedLabels = RunYoloInference(image)
                .Select(rect => new LabelData { Rect = rect, Name = "Suggested Label" })
                .ToList();

            LoadedImage.Invalidate();
        }

        private void AISettingsToolStrip_Click(object sender, EventArgs e)
        {
            using (var settingsForm = new AiSettings(confidenceThreshold))
            {
                if (settingsForm.ShowDialog() == DialogResult.OK)
                {
                    confidenceThreshold = settingsForm.ConfidenceThreshold;
                    MessageBox.Show($"AI Confidence Threshold updated to: {(confidenceThreshold * 100):F0}%",
                                    "AI Settings Updated", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
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

            using Pen crosshairPen = new(crosshairColor, crosshairSize);

            // Draw horizontal line
            g.DrawLine(crosshairPen, new Point(0, cursorPosition.Y), new Point(LoadedImage.Width, cursorPosition.Y));

            // Draw vertical line
            g.DrawLine(crosshairPen, new Point(cursorPosition.X, 0), new Point(cursorPosition.X, LoadedImage.Height));
        }


        private void PictureBox_MouseDown(object sender, MouseEventArgs e)
        {
            if (LoadedImage.Image == null) return;

            bool clickedOnLabel = false;

            foreach (var label in labels)
            {
                resizeHandleType = GetResizeHandle(e.Location, label.Rect);

                if (resizeHandleType != ResizeHandleType.None)
                {
                    selectedLabel = label;
                    isResizing = true;
                    dragStart = e.Location;
                    clickedOnLabel = true;
                    break;
                }

                if (label.Rect.Contains(e.Location))
                {
                    selectedLabel = label;
                    isDragging = true;
                    dragStart = e.Location;

                    // Highlight the label in the listbox
                    int index = labels.IndexOf(label);
                    LabelListBox.SelectedIndex = index;

                    clickedOnLabel = true;
                    break;
                }
            }

            if (!clickedOnLabel)
            {
                // Start drawing a new label if dragging
                isDrawing = true;
                startPoint = e.Location;
                currentRect = new Rectangle(startPoint, Size.Empty);
                selectedLabel = null;
                LabelListBox.ClearSelected();
                LoadedImage.Invalidate();
            }
        }

        private void PictureBox_MouseMove(object sender, MouseEventArgs e)
        {
            cursorPosition = e.Location; // Update cursor position
            LoadedImage.Invalidate(); // Redraw the image to include the crosshair

            if (isDrawing)
            {
                currentRect = new Rectangle(
                    Math.Min(startPoint.X, e.X),
                    Math.Min(startPoint.Y, e.Y),
                    Math.Abs(e.X - startPoint.X),
                    Math.Abs(e.Y - startPoint.Y)
                );
                return;
            }

            if (selectedLabel == null) return;

            if (isResizing)
            {
                ResizeLabel(selectedLabel, resizeHandleType, e.Location);
            }
            else if (isDragging)
            {
                int dx = e.X - dragStart.X;
                int dy = e.Y - dragStart.Y;
                selectedLabel.Rect = new Rectangle(selectedLabel.Rect.X + dx, selectedLabel.Rect.Y + dy,
                                                   selectedLabel.Rect.Width, selectedLabel.Rect.Height);
                dragStart = e.Location;
            }
        }

        private void PictureBox_MouseUp(object sender, MouseEventArgs e)
        {
            if (isDrawing)
            {
                isDrawing = false;
                if (currentRect.Width > 10 && currentRect.Height > 10) // Avoid tiny labels
                {
                    var newLabel = new LabelData { Rect = currentRect, Name = $"Label {labels.Count + 1}" };
                    labels.Add(newLabel);
                    LabelListBox.Items.Add(newLabel.Name);
                }
                LoadedImage.Invalidate();
                return;
            }

            isDragging = false;
            isResizing = false;
        }

        private void PictureBox_Paint(object sender, PaintEventArgs e)
        {
            if (LoadedImage.Image == null) return;
            using Pen pen = new(Color.Red, 2);
            using Brush brush = new SolidBrush(Color.Blue);

            foreach (var label in labels)
            {
                e.Graphics.DrawRectangle(pen, label.Rect);

                if (selectedLabel == label)
                {
                    DrawResizeHandles(e.Graphics, label.Rect);
                }
            }

            // Draw the new label while dragging
            if (isDrawing)
            {
                e.Graphics.DrawRectangle(pen, currentRect);
            }

            // Draw crosshair for precise placement
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