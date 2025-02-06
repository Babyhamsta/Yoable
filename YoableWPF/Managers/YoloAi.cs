using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.Win32;
using System.Drawing.Imaging;
using System.Windows;
using System.Drawing;

namespace YoableWPF.Managers
{
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

                if (objectness < Properties.Settings.Default.AIConfidence) continue; // Filter out weak detections

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
                if (objectness < Properties.Settings.Default.AIConfidence) continue;

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

        public List<Rectangle> RunInference(Bitmap image)
        {
            Bitmap processedImage = ConvertTo24bpp(image);
            float[] inputArray = BitmapToFloatArray(processedImage);
            var inputTensor = new DenseTensor<float>(inputArray, new int[] { 1, 3, image.Height, image.Width });
            var inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor("images", inputTensor) };

            using var results = yoloSession.Run(inputs, outputNames);
            var outputTensor = results.First().AsTensor<float>();

            return isYoloV5 ? PostProcessYoloV5Output(outputTensor, image.Width, image.Height)
                            : PostProcessYoloV8Output(outputTensor, image.Width, image.Height);
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
