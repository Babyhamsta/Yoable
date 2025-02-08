using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.Win32;
using System.Drawing.Imaging;
using System.Windows;
using System.Drawing;
using Point = System.Drawing.Point;

namespace YoableWPF.Managers
{
    public class Detection
    {
        public Rectangle Box { get; }
        public float Confidence { get; }
        public float ClassConfidence { get; }
        public int ClassId { get; }
        public float Score => Confidence * ClassConfidence;

        public Detection(Rectangle box, float confidence, float classConfidence = 1.0f, int classId = 0)
        {
            Box = box;
            Confidence = confidence;
            ClassConfidence = classConfidence;
            ClassId = classId;
        }
    }

    public class YoloAI
    {
        public InferenceSession yoloSession;
        private List<string> outputNames;
        private bool isYoloV5;
        private string yoloModelPath;
        public int TotalDetections { get; private set; } = 0;

        public YoloAI()
        {

        }

        private Bitmap ConvertTo24bpp(Bitmap src)
        {
            Bitmap dst = new Bitmap(src.Width, src.Height, PixelFormat.Format24bppRgb);
            using Graphics g = Graphics.FromImage(dst);
            g.DrawImage(src, new Rectangle(0, 0, src.Width, src.Height));
            return dst;
        }

        private static float[] BitmapToFloatArray(Bitmap image)
        {
            int height = image.Height, width = image.Width;
            float[] result = new float[3 * height * width];
            float multiplier = 1.0f / 255.0f;

            Rectangle rect = new(0, 0, width, height);
            BitmapData bmpData = image.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);

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
                            result[baseIndex] = ptr[2] * multiplier;
                            result[height * width + baseIndex] = ptr[1] * multiplier;
                            result[2 * height * width + baseIndex] = ptr[0] * multiplier;
                            ptr += 3;
                            baseIndex++;
                        }
                        ptr += bmpData.Stride - width * 3;
                    }
                }
            }
            finally
            {
                image.UnlockBits(bmpData);
            }
            return result;
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
                MessageBox.Show($"Error detecting YOLO version: {ex.Message}", "Model Detection Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            return 0; // ERROR - Do not continue
        }

        private List<Detection> ApplyImprovedNMS(List<Detection> detections, float iouThreshold = 0.5f)
        {
            if (detections.Count == 0) return new List<Detection>();

            // Sort by total score (objectness * class confidence)
            detections = detections.OrderByDescending(d => d.Score).ToList();
            List<Detection> kept = new();

            for (int i = 0; i < detections.Count; i++)
            {
                var current = detections[i];
                bool shouldKeep = true;

                foreach (var previous in kept)
                {
                    float iou = ComputeIoU(current.Box, previous.Box);

                    if (iou >= iouThreshold)
                    {
                        // Additional checks for keeping potentially distinct objects
                        if (IsDistinctObject(current, previous))
                        {
                            continue;
                        }

                        shouldKeep = false;
                        break;
                    }
                }

                if (shouldKeep)
                {
                    kept.Add(current);
                }
            }

            return kept;
        }

        private bool IsDistinctObject(Detection d1, Detection d2)
        {
            // Different classes should be kept
            if (d1.ClassId != d2.ClassId) return true;

            // Calculate relative size difference
            float area1 = d1.Box.Width * d1.Box.Height;
            float area2 = d2.Box.Width * d2.Box.Height;
            float sizeDiff = Math.Abs(area1 - area2) / Math.Max(area1, area2);

            // Calculate center points
            Point center1 = new(d1.Box.X + d1.Box.Width / 2, d1.Box.Y + d1.Box.Height / 2);
            Point center2 = new(d2.Box.X + d2.Box.Width / 2, d2.Box.Y + d2.Box.Height / 2);

            // Calculate relative distance between centers
            float distance = GetDistance(center1, center2);
            float avgSize = (float)Math.Sqrt((area1 + area2) / 2);
            float relativeDistance = distance / avgSize;

            // Check confidence delta
            float confidenceDelta = Math.Abs(d1.Score - d2.Score);

            // Criteria for distinct objects:
            // 1. Very different sizes (> 50% difference)
            // 2. Centers are far apart relative to size
            // 3. Similar confidence scores (suggesting both might be valid)
            return sizeDiff > 0.5f ||
                   relativeDistance > 0.5f ||
                   (confidenceDelta < 0.1f && sizeDiff > 0.3f);
        }

        private float GetDistance(Point p1, Point p2)
        {
            return (float)Math.Sqrt(Math.Pow(p2.X - p1.X, 2) + Math.Pow(p2.Y - p1.Y, 2));
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

        private List<Detection> PostProcessYoloV5Output(Tensor<float> outputTensor, int imgWidth, int imgHeight)
        {
            List<Detection> detections = new();
            int numDetections = outputTensor.Dimensions[1]; // 25200 detections for YOLOv5

            for (int i = 0; i < numDetections; i++)
            {
                float objectness = outputTensor[0, i, 4]; // Objectness score
                if (objectness < Properties.Settings.Default.AIConfidence) continue;

                // Get class confidence and id
                float maxClassConf = 0;
                int classId = 0;
                for (int c = 5; c < outputTensor.Dimensions[2]; c++)
                {
                    float classConf = outputTensor[0, i, c];
                    if (classConf > maxClassConf)
                    {
                        maxClassConf = classConf;
                        classId = c - 5;
                    }
                }

                // Extract normalized coordinates
                float xCenter = outputTensor[0, i, 0];
                float yCenter = outputTensor[0, i, 1];
                float width = outputTensor[0, i, 2];
                float height = outputTensor[0, i, 3];

                // Convert to pixel coordinates with boundary checking
                float xMin = Math.Max(0, xCenter - width / 2);
                float yMin = Math.Max(0, yCenter - height / 2);
                float xMax = Math.Min(imgWidth, xCenter + width / 2);
                float yMax = Math.Min(imgHeight, yCenter + height / 2);

                // Skip if box is too small
                if (xMax - xMin < 1 || yMax - yMin < 1) continue;

                detections.Add(new Detection(
                    new Rectangle((int)xMin, (int)yMin, (int)(xMax - xMin), (int)(yMax - yMin)),
                    objectness,
                    maxClassConf,
                    classId
                ));
            }

            var finalBoxes = ApplyImprovedNMS(detections);
            TotalDetections += finalBoxes.Count;
            return finalBoxes;
        }

        private List<Detection> PostProcessYoloV8Output(Tensor<float> outputTensor, int imgWidth, int imgHeight)
        {
            List<Detection> detections = new();
            int numDetections = outputTensor.Dimensions[2];

            for (int i = 0; i < numDetections; i++)
            {
                float objectness = outputTensor[0, 4, i];
                if (objectness < Properties.Settings.Default.AIConfidence) continue;

                // Get class confidence and id
                float maxClassConf = 0;
                int classId = 0;
                for (int c = 5; c < outputTensor.Dimensions[1]; c++)
                {
                    float classConf = outputTensor[0, c, i];
                    if (classConf > maxClassConf)
                    {
                        maxClassConf = classConf;
                        classId = c - 5;
                    }
                }

                // Extract normalized coordinates
                float xCenter = outputTensor[0, 0, i];
                float yCenter = outputTensor[0, 1, i];
                float width = outputTensor[0, 2, i];
                float height = outputTensor[0, 3, i];

                // Convert to pixel coordinates with boundary checking
                float xMin = Math.Max(0, xCenter - width / 2);
                float yMin = Math.Max(0, yCenter - height / 2);
                float xMax = Math.Min(imgWidth, xCenter + width / 2);
                float yMax = Math.Min(imgHeight, yCenter + height / 2);

                // Skip if box is too small
                if (xMax - xMin < 1 || yMax - yMin < 1) continue;

                detections.Add(new Detection(
                    new Rectangle((int)xMin, (int)yMin, (int)(xMax - xMin), (int)(yMax - yMin)),
                    objectness,
                    maxClassConf,
                    classId
                ));
            }

            var finalBoxes = ApplyImprovedNMS(detections);
            TotalDetections += finalBoxes.Count;
            return finalBoxes;
        }

        public List<Rectangle> RunInference(Bitmap image)
        {
            Bitmap processedImage = ConvertTo24bpp(image);
            float[] inputArray = BitmapToFloatArray(processedImage);
            var inputTensor = new DenseTensor<float>(inputArray, new int[] { 1, 3, image.Height, image.Width });
            var inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor("images", inputTensor) };

            using var results = yoloSession.Run(inputs, outputNames);
            var outputTensor = results.First().AsTensor<float>();

            var detections = isYoloV5
                ? PostProcessYoloV5Output(outputTensor, image.Width, image.Height)
                : PostProcessYoloV8Output(outputTensor, image.Width, image.Height);

            return detections.Select(d => d.Box).ToList();
        }

        public void LoadYoloModel()
        {
            OpenFileDialog openFileDialog = new()
            {
                Filter = "ONNX Model Files|*.onnx",
                Title = "Select YOLO ONNX Model"
            };

            if (openFileDialog.ShowDialog() != true)
                return;

            if (!string.IsNullOrEmpty(yoloModelPath) && yoloModelPath != openFileDialog.FileName)
            {
                yoloSession?.Dispose();
            }

            yoloModelPath = openFileDialog.FileName;

            try
            {
                // Try DirectML first
                if (!TryInitializeSession(true, out string errorMessage))
                {
                    MessageBox.Show($"GPU initialization failed: {errorMessage}\nFalling back to CPU.",
                                  "GPU Warning", MessageBoxButton.OK, MessageBoxImage.Warning);

                    // Fall back to CPU
                    if (!TryInitializeSession(false, out errorMessage))
                    {
                        MessageBox.Show($"Failed to initialize model: {errorMessage}",
                                      "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                }

                // Detect model type
                int modelVersion = DetectYoloVersion();
                if (modelVersion == 0)
                {
                    yoloSession?.Dispose();
                    yoloSession = null;
                    MessageBox.Show("Failed to detect YOLO model version. Please select a valid YOLOv5 or YOLOv8 ONNX model.",
                                  "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                isYoloV5 = modelVersion == 5;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Unexpected error loading model: {ex.Message}",
                              "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private bool TryInitializeSession(bool useGPU, out string errorMessage)
        {
            try
            {
                var sessionOptions = new SessionOptions
                {
                    EnableCpuMemArena = true,
                    EnableMemoryPattern = true,
                    GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
                    ExecutionMode = ExecutionMode.ORT_PARALLEL
                };

                // Have to be launching with GPU and user settings have to match
                if (useGPU && Properties.Settings.Default.UseGPU)
                {
                    sessionOptions.AppendExecutionProvider_DML();
                }
                sessionOptions.AppendExecutionProvider_CPU();

                yoloSession = new InferenceSession(yoloModelPath, sessionOptions);
                outputNames = new List<string>(yoloSession.OutputMetadata.Keys);

                errorMessage = string.Empty;
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }
    }
}
