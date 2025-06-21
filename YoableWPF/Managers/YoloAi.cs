using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System.Drawing.Imaging;
using System.Windows;
using System.Drawing;
using System.IO;
using Point = System.Drawing.Point;

namespace YoableWPF.Managers
{
    // Add a new class to store raw YOLO format detections
    public class YoloDetection
    {
        public float CenterX { get; set; }
        public float CenterY { get; set; }
        public float Width { get; set; }
        public float Height { get; set; }
        public float Confidence { get; set; }
        public float ClassConfidence { get; set; }
        public int ClassId { get; set; }
        public float Score => Confidence * ClassConfidence;
        public int ModelIndex { get; set; } = -1;

        public Rectangle ToRectangle()
        {
            int x = (int)Math.Round(CenterX - Width / 2.0f);
            int y = (int)Math.Round(CenterY - Height / 2.0f);
            int w = (int)Math.Round(Width);
            int h = (int)Math.Round(Height);

            // Ensure valid dimensions
            w = Math.Max(1, w);
            h = Math.Max(1, h);

            return new Rectangle(x, y, w, h);
        }
    }

    public class Detection
    {
        public Rectangle Box { get; set; }
        public float Confidence { get; }
        public float ClassConfidence { get; }
        public int ClassId { get; }
        public float Score => Confidence * ClassConfidence;
        public int ModelIndex { get; set; } = -1;

        public Detection(Rectangle box, float confidence, float classConfidence = 1.0f, int classId = 0)
        {
            Box = box;
            Confidence = confidence;
            ClassConfidence = classConfidence;
            ClassId = classId;
        }
    }

    public class EnsembleDetection
    {
        public Rectangle Box { get; set; }
        public float AverageConfidence { get; set; }
        public int ClassId { get; set; }
        public int ConsensusCount { get; set; }
        public List<Detection> SourceDetections { get; set; } = new List<Detection>();
    }

    public class YoloModel
    {
        public string ModelPath { get; set; }
        public InferenceSession Session { get; set; }
        public bool IsYoloV5 { get; set; }
        public List<string> OutputNames { get; set; }
        public string Name { get; set; }
    }

    public class YoloAI
    {
        private List<YoloModel> loadedModels = new List<YoloModel>();
        public int TotalDetections { get; private set; } = 0;

        // Legacy single model support
        public InferenceSession yoloSession => loadedModels.FirstOrDefault()?.Session;

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

        private int DetectYoloVersion(InferenceSession session)
        {
            try
            {
                var modelOutput = session.OutputMetadata.First();
                int[] shape = modelOutput.Value.Dimensions.ToArray();

                if (shape.Length == 3)
                {
                    if (shape.SequenceEqual(new int[] { 1, 5, 8400 }))
                    {
                        return 8;
                    }

                    if (shape.SequenceEqual(new int[] { 1, 25200, 6 }))
                    {
                        return 5;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error detecting YOLO version: {ex.Message}", "Model Detection Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            return 0;
        }

        private float ComputeIoU(Rectangle a, Rectangle b)
        {
            float x1 = Math.Max(a.Left, b.Left);
            float y1 = Math.Max(a.Top, b.Top);
            float x2 = Math.Min(a.Right, b.Right);
            float y2 = Math.Min(a.Bottom, b.Bottom);

            float intersectionWidth = Math.Max(0, x2 - x1);
            float intersectionHeight = Math.Max(0, y2 - y1);
            float intersectionArea = intersectionWidth * intersectionHeight;

            float areaA = (float)(a.Width * a.Height);
            float areaB = (float)(b.Width * b.Height);
            float unionArea = areaA + areaB - intersectionArea;

            return unionArea == 0 ? 0 : intersectionArea / unionArea;
        }

        // New IoU calculation for YOLO format detections
        private float ComputeIoUYolo(YoloDetection a, YoloDetection b)
        {
            float x1_a = a.CenterX - a.Width / 2;
            float y1_a = a.CenterY - a.Height / 2;
            float x2_a = a.CenterX + a.Width / 2;
            float y2_a = a.CenterY + a.Height / 2;

            float x1_b = b.CenterX - b.Width / 2;
            float y1_b = b.CenterY - b.Height / 2;
            float x2_b = b.CenterX + b.Width / 2;
            float y2_b = b.CenterY + b.Height / 2;

            float x1 = Math.Max(x1_a, x1_b);
            float y1 = Math.Max(y1_a, y1_b);
            float x2 = Math.Min(x2_a, x2_b);
            float y2 = Math.Min(y2_a, y2_b);

            float intersection = Math.Max(0, x2 - x1) * Math.Max(0, y2 - y1);
            float area_a = a.Width * a.Height;
            float area_b = b.Width * b.Height;
            float union = area_a + area_b - intersection;

            return union == 0 ? 0 : intersection / union;
        }

        private List<Detection> ApplyImprovedNMS(List<Detection> detections, float iouThreshold = 0.5f)
        {
            if (detections.Count == 0) return new List<Detection>();

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

        // New NMS for YOLO format detections
        private List<YoloDetection> ApplyNMSYolo(List<YoloDetection> detections, float iouThreshold = 0.5f)
        {
            if (detections.Count == 0) return new List<YoloDetection>();

            detections = detections.OrderByDescending(d => d.Score).ToList();
            List<YoloDetection> kept = new();

            for (int i = 0; i < detections.Count; i++)
            {
                var current = detections[i];
                bool shouldKeep = true;

                foreach (var previous in kept)
                {
                    float iou = ComputeIoUYolo(current, previous);
                    if (iou >= iouThreshold)
                    {
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
            if (d1.ClassId != d2.ClassId) return true;

            float area1 = d1.Box.Width * d1.Box.Height;
            float area2 = d2.Box.Width * d2.Box.Height;
            float sizeDiff = Math.Abs(area1 - area2) / Math.Max(area1, area2);

            Point center1 = new(d1.Box.X + d1.Box.Width / 2, d1.Box.Y + d1.Box.Height / 2);
            Point center2 = new(d2.Box.X + d2.Box.Width / 2, d2.Box.Y + d2.Box.Height / 2);

            float distance = GetDistance(center1, center2);
            float avgSize = (float)Math.Sqrt((area1 + area2) / 2);
            float relativeDistance = distance / avgSize;

            float confidenceDelta = Math.Abs(d1.Score - d2.Score);

            return sizeDiff > 0.5f ||
                   relativeDistance > 0.5f ||
                   (confidenceDelta < 0.1f && sizeDiff > 0.3f);
        }

        private float GetDistance(Point p1, Point p2)
        {
            return (float)Math.Sqrt(Math.Pow(p2.X - p1.X, 2) + Math.Pow(p2.Y - p1.Y, 2));
        }

        // New ensemble processing using YOLO format throughout
        private List<YoloDetection> ApplyEnsembleConsensus(List<YoloDetection> allDetections)
        {
            if (allDetections.Count == 0) return new List<YoloDetection>();

            // Load ensemble settings from Properties
            float consensusIoUThreshold = Properties.Settings.Default.ConsensusIoUThreshold;
            int minimumConsensus = Properties.Settings.Default.MinimumConsensus;
            bool useWeightedAverage = Properties.Settings.Default.UseWeightedAverage;
            float confidenceThreshold = Properties.Settings.Default.AIConfidence;

            // Group detections into clusters based on IoU
            var clusters = new List<List<YoloDetection>>();
            var processed = new bool[allDetections.Count];

            for (int i = 0; i < allDetections.Count; i++)
            {
                if (processed[i]) continue;

                // Start a new cluster with this detection
                var cluster = new List<YoloDetection> { allDetections[i] };
                processed[i] = true;

                // Find all detections that overlap with any detection in the cluster
                bool addedNewDetection;
                do
                {
                    addedNewDetection = false;

                    for (int j = 0; j < allDetections.Count; j++)
                    {
                        if (processed[j]) continue;

                        // Check if this detection overlaps with any detection in the cluster
                        bool overlapsWithCluster = false;
                        foreach (var clusterDet in cluster)
                        {
                            float iou = ComputeIoUYolo(allDetections[j], clusterDet);
                            if (iou >= consensusIoUThreshold)
                            {
                                overlapsWithCluster = true;
                                break;
                            }
                        }

                        if (overlapsWithCluster)
                        {
                            cluster.Add(allDetections[j]);
                            processed[j] = true;
                            addedNewDetection = true;
                        }
                    }
                } while (addedNewDetection);

                clusters.Add(cluster);
            }

            // Process each cluster
            var consensusDetections = new List<YoloDetection>();
            int effectiveMinConsensus = Math.Min(minimumConsensus, loadedModels.Count);

            foreach (var cluster in clusters)
            {
                // Count unique models in this cluster
                var modelIndices = cluster.Select(d => d.ModelIndex).Distinct().ToList();

                // Check if cluster meets consensus requirements
                if (modelIndices.Count >= effectiveMinConsensus ||
                    (cluster[0].Score > confidenceThreshold + 0.2f) ||
                    (loadedModels.Count == 2 && minimumConsensus == 2 &&
                     cluster[0].Score > confidenceThreshold + 0.1f))
                {
                    // Only include detections from different models for merging
                    var detectionsToMerge = new List<YoloDetection>();
                    var usedModels = new HashSet<int>();

                    // Take the best detection from each model
                    foreach (var det in cluster.OrderByDescending(d => d.Score))
                    {
                        if (!usedModels.Contains(det.ModelIndex))
                        {
                            detectionsToMerge.Add(det);
                            usedModels.Add(det.ModelIndex);
                        }
                    }

                    // Merge detections from different models
                    if (detectionsToMerge.Count > 0)
                    {
                        var merged = MergeYoloDetections(detectionsToMerge, useWeightedAverage);
                        consensusDetections.Add(merged);
                    }
                }
            }

            // Apply final NMS to remove any remaining duplicates
            float ensembleIoUThreshold = Properties.Settings.Default.EnsembleIoUThreshold;
            return ApplyNMSYolo(consensusDetections, ensembleIoUThreshold);
        }


        private YoloDetection MergeYoloDetections(List<YoloDetection> detections, bool useWeightedAverage)
        {
            if (detections.Count == 0) return null;
            if (detections.Count == 1) return detections[0];

            if (useWeightedAverage)
            {
                // Calculate weighted average based on confidence scores
                float totalWeight = detections.Sum(d => d.Score);

                return new YoloDetection
                {
                    CenterX = detections.Sum(d => d.CenterX * d.Score) / totalWeight,
                    CenterY = detections.Sum(d => d.CenterY * d.Score) / totalWeight,
                    Width = detections.Sum(d => d.Width * d.Score) / totalWeight,
                    Height = detections.Sum(d => d.Height * d.Score) / totalWeight,
                    Confidence = detections.Average(d => d.Confidence),
                    ClassConfidence = detections.Average(d => d.ClassConfidence),
                    ClassId = detections[0].ClassId,
                    ModelIndex = -1  // Merged detection doesn't belong to a specific model
                };
            }
            else
            {
                // Use simple averaging
                return new YoloDetection
                {
                    CenterX = detections.Average(d => d.CenterX),
                    CenterY = detections.Average(d => d.CenterY),
                    Width = detections.Average(d => d.Width),
                    Height = detections.Average(d => d.Height),
                    Confidence = detections.Average(d => d.Confidence),
                    ClassConfidence = detections.Average(d => d.ClassConfidence),
                    ClassId = detections[0].ClassId,
                    ModelIndex = -1  // Merged detection doesn't belong to a specific model
                };
            }
        }

        private List<YoloDetection> PostProcessYoloV5OutputRaw(Tensor<float> outputTensor, int imgWidth, int imgHeight)
        {
            List<YoloDetection> detections = new();
            int numDetections = outputTensor.Dimensions[1];

            // Determine model input size from tensor dimensions
            // Assuming the model was trained on square images
            int modelInputSize = 640; // Default YOLO size, adjust if your models use different size

            for (int i = 0; i < numDetections; i++)
            {
                float objectness = outputTensor[0, i, 4];
                if (objectness < Properties.Settings.Default.AIConfidence) continue;

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

                // Get coordinates (these are in model input space, e.g., 0-640)
                float centerX = outputTensor[0, i, 0];
                float centerY = outputTensor[0, i, 1];
                float width = outputTensor[0, i, 2];
                float height = outputTensor[0, i, 3];

                // Scale to actual image dimensions
                float scaleX = (float)imgWidth / modelInputSize;
                float scaleY = (float)imgHeight / modelInputSize;

                detections.Add(new YoloDetection
                {
                    CenterX = centerX * scaleX,
                    CenterY = centerY * scaleY,
                    Width = width * scaleX,
                    Height = height * scaleY,
                    Confidence = objectness,
                    ClassConfidence = maxClassConf,
                    ClassId = classId
                });
            }

            return detections;
        }

        private List<YoloDetection> PostProcessYoloV8OutputRaw(Tensor<float> outputTensor, int imgWidth, int imgHeight)
        {
            List<YoloDetection> detections = new();
            int numDetections = outputTensor.Dimensions[2];

            // Determine model input size
            int modelInputSize = 640; // Default YOLO size

            for (int i = 0; i < numDetections; i++)
            {
                float objectness = outputTensor[0, 4, i];
                if (objectness < Properties.Settings.Default.AIConfidence) continue;

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

                // Get coordinates (these are in model input space)
                float centerX = outputTensor[0, 0, i];
                float centerY = outputTensor[0, 1, i];
                float width = outputTensor[0, 2, i];
                float height = outputTensor[0, 3, i];

                // Scale to actual image dimensions
                float scaleX = (float)imgWidth / modelInputSize;
                float scaleY = (float)imgHeight / modelInputSize;

                detections.Add(new YoloDetection
                {
                    CenterX = centerX * scaleX,
                    CenterY = centerY * scaleY,
                    Width = width * scaleX,
                    Height = height * scaleY,
                    Confidence = objectness,
                    ClassConfidence = maxClassConf,
                    ClassId = classId
                });
            }

            return detections;
        }

        private List<Detection> PostProcessYoloV5Output(Tensor<float> outputTensor, int imgWidth, int imgHeight)
        {
            var yoloDetections = PostProcessYoloV5OutputRaw(outputTensor, imgWidth, imgHeight);
            return yoloDetections.Select(d => new Detection(d.ToRectangle(), d.Confidence, d.ClassConfidence, d.ClassId)).ToList();
        }

        private List<Detection> PostProcessYoloV8Output(Tensor<float> outputTensor, int imgWidth, int imgHeight)
        {
            var yoloDetections = PostProcessYoloV8OutputRaw(outputTensor, imgWidth, imgHeight);
            return yoloDetections.Select(d => new Detection(d.ToRectangle(), d.Confidence, d.ClassConfidence, d.ClassId)).ToList();
        }

        public List<Rectangle> RunInference(Bitmap image)
        {
            if (loadedModels.Count == 0) return new List<Rectangle>();

            if (loadedModels.Count == 1)
            {
                return RunSingleModelInference(image, loadedModels[0]);
            }

            return RunEnsembleInference(image);
        }

        private List<Rectangle> RunSingleModelInference(Bitmap image, YoloModel model)
        {
            Bitmap processedImage = ConvertTo24bpp(image);
            float[] inputArray = BitmapToFloatArray(processedImage);
            var inputTensor = new DenseTensor<float>(inputArray, new int[] { 1, 3, image.Height, image.Width });
            var inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor("images", inputTensor) };

            using var results = model.Session.Run(inputs, model.OutputNames);
            var outputTensor = results.First().AsTensor<float>();

            var detections = model.IsYoloV5
                ? PostProcessYoloV5Output(outputTensor, image.Width, image.Height)
                : PostProcessYoloV8Output(outputTensor, image.Width, image.Height);

            var finalDetections = ApplyImprovedNMS(detections);
            TotalDetections += finalDetections.Count;
            return finalDetections.Select(d => d.Box).ToList();
        }

        public List<Rectangle> RunEnsembleInference(Bitmap image)
        {
            var allYoloDetections = new List<YoloDetection>();
            Bitmap processedImage = ConvertTo24bpp(image);
            float[] inputArray = BitmapToFloatArray(processedImage);
            var inputTensor = new DenseTensor<float>(inputArray, new int[] { 1, 3, image.Height, image.Width });

            // Run inference on all models
            for (int modelIdx = 0; modelIdx < loadedModels.Count; modelIdx++)
            {
                var model = loadedModels[modelIdx];
                var inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor("images", inputTensor) };

                using var results = model.Session.Run(inputs, model.OutputNames);
                var outputTensor = results.First().AsTensor<float>();

                // Get raw YOLO format detections
                var detections = model.IsYoloV5
                    ? PostProcessYoloV5OutputRaw(outputTensor, image.Width, image.Height)
                    : PostProcessYoloV8OutputRaw(outputTensor, image.Width, image.Height);

                // Apply per-model NMS in YOLO format
                detections = ApplyNMSYolo(detections, 0.5f);

                // Mark with model index
                foreach (var det in detections)
                {
                    det.ModelIndex = modelIdx;
                }

                allYoloDetections.AddRange(detections);
            }

            // Apply ensemble consensus in YOLO format
            var consensusDetections = ApplyEnsembleConsensus(allYoloDetections);
            TotalDetections += consensusDetections.Count;

            // Convert to rectangles only at the very end
            return consensusDetections.Select(d => d.ToRectangle()).ToList();
        }

        public void LoadYoloModel()
        {
            OpenModelManager();
        }

        public void OpenModelManager()
        {
            var dialog = new ModelManagerDialog(this);
            dialog.Owner = Application.Current.MainWindow;
            dialog.ShowDialog();
        }

        public int GetLoadedModelsCount()
        {
            return loadedModels.Count;
        }

        public List<YoloModel> GetLoadedModels()
        {
            return new List<YoloModel>(loadedModels);
        }

        public void RemoveModel(string modelName)
        {
            var model = loadedModels.FirstOrDefault(m =>
                $"{m.Name} ({(m.IsYoloV5 ? "YOLOv5" : "YOLOv8")})" == modelName);

            if (model != null)
            {
                model.Session?.Dispose();
                loadedModels.Remove(model);
            }
        }

        public void LoadModelFromPath(string modelPath)
        {
            try
            {
                if (loadedModels.Any(m => m.ModelPath == modelPath))
                {
                    MessageBox.Show("This model is already loaded.", "Model Already Loaded",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var model = new YoloModel
                {
                    ModelPath = modelPath,
                    Name = Path.GetFileNameWithoutExtension(modelPath)
                };

                if (!TryInitializeSession(model, Properties.Settings.Default.UseGPU, out string errorMessage))
                {
                    MessageBox.Show($"GPU initialization failed: {errorMessage}\nFalling back to CPU.",
                                  "GPU Warning", MessageBoxButton.OK, MessageBoxImage.Warning);

                    if (!TryInitializeSession(model, false, out errorMessage))
                    {
                        MessageBox.Show($"Failed to initialize model: {errorMessage}",
                                      "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                }

                int modelVersion = DetectYoloVersion(model.Session);
                if (modelVersion == 0)
                {
                    model.Session?.Dispose();
                    MessageBox.Show("Failed to detect YOLO model version.",
                                  "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                model.IsYoloV5 = modelVersion == 5;
                loadedModels.Add(model);

                MessageBox.Show($"Model '{model.Name}' loaded successfully.\nTotal models: {loadedModels.Count}",
                    "Model Loaded", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading model: {ex.Message}",
                              "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private bool TryInitializeSession(YoloModel model, bool useGPU, out string errorMessage)
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

                if (useGPU)
                {
                    sessionOptions.AppendExecutionProvider_DML();
                }
                sessionOptions.AppendExecutionProvider_CPU();

                model.Session = new InferenceSession(model.ModelPath, sessionOptions);
                model.OutputNames = new List<string>(model.Session.OutputMetadata.Keys);

                errorMessage = string.Empty;
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

        public void ClearAllModels()
        {
            foreach (var model in loadedModels)
            {
                model.Session?.Dispose();
            }
            loadedModels.Clear();
        }

        public void Dispose()
        {
            ClearAllModels();
        }
    }
}