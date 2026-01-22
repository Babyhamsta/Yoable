using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.Windows;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Reflection;
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

    public enum YoloFormat
    {
        Unknown,
        YoloV5,      // [batch, num_detections, 5+num_classes]
        YoloV8       // [batch, 4+num_classes, num_detections]
    }

    public enum EnsembleDetectionMode
    {
        Voting,      // Voting mode (requires consensus from multiple models)
        Union        // Union mode (mark if any model detects it)
    }

    public class YoloModelInfo
    {
        public YoloFormat Format { get; set; }
        public int[] OutputShape { get; set; }
        public int NumClasses { get; set; }
        public int NumDetections { get; set; }
        public int ModelInputSize { get; set; }
        public bool HasObjectness { get; set; }
    }

    public class YoloModel
    {
        public string ModelPath { get; set; }
        public InferenceSession Session { get; set; }
        public YoloModelInfo ModelInfo { get; set; }
        public List<string> OutputNames { get; set; }
        public string Name { get; set; }
        // Class mapping: Model Class ID -> Project Class ID
        // null = not configured, empty = all classes set to "nan", non-empty = configured with mappings
        public Dictionary<int, int> ClassMapping { get; set; } = null;
    }

    public class YoloAI
    {
        // Configuration constants (can be moved to settings if needed)
        private const int DEFAULT_MODEL_INPUT_SIZE = 640;
        private const int DEFAULT_DETECTION_COUNT = 8400;
        private const int SEGMENTATION_THRESHOLD = 84; // 4 box coords + typical class count threshold

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

        /// <summary>
        /// Resize image to match model's expected input size while maintaining aspect ratio
        /// </summary>
        private Bitmap ResizeImageForModel(Bitmap image, int modelInputSize)
        {
            // Create a square image with the model's expected size
            Bitmap resized = new Bitmap(modelInputSize, modelInputSize, PixelFormat.Format24bppRgb);

            using (Graphics g = Graphics.FromImage(resized))
            {
                // Fill background with black (typical for YOLO models)
                g.Clear(Color.Black);

                // Calculate scaling to fit image within model input size while maintaining aspect ratio
                float scale = Math.Min((float)modelInputSize / image.Width, (float)modelInputSize / image.Height);
                int scaledWidth = (int)(image.Width * scale);
                int scaledHeight = (int)(image.Height * scale);

                // Center the image
                int x = (modelInputSize - scaledWidth) / 2;
                int y = (modelInputSize - scaledHeight) / 2;

                // Use high-quality interpolation
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawImage(image, x, y, scaledWidth, scaledHeight);
            }

            return resized;
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

        private YoloModelInfo DetectYoloFormat(InferenceSession session)
        {
            try
            {
                var modelOutput = session.OutputMetadata.First();
                int[] shape = modelOutput.Value.Dimensions.ToArray();

                var info = new YoloModelInfo
                {
                    OutputShape = shape,
                    Format = YoloFormat.Unknown
                };

                // Handle dynamic dimensions (-1)
                for (int i = 0; i < shape.Length; i++)
                {
                    if (shape[i] == -1)
                    {
                        // Common defaults for dynamic dimensions
                        if (i == 0) shape[i] = 1; // Batch size
                        else if (i == shape.Length - 1) shape[i] = DEFAULT_DETECTION_COUNT;
                    }
                }

                if (shape.Length == 3)
                {
                    // Determine format by comparing middle vs last dimension
                    // YOLOv8: [batch, features, detections] - features is small, detections is large
                    // YOLOv5: [batch, detections, features] - detections is large, features is small to medium

                    // If middle dimension is significantly smaller than last dimension, it's YOLOv8
                    if (shape[1] < 100 && shape[2] > 1000)
                    {
                        // YOLOv8 format: [batch, 4+num_classes, num_detections]
                        info.Format = YoloFormat.YoloV8;
                        info.NumDetections = shape[2];
                        info.NumClasses = shape[1] - 4; // x, y, w, h, then classes (no objectness)
                        info.HasObjectness = false;
                        info.ModelInputSize = DEFAULT_MODEL_INPUT_SIZE;

                        // Reject segmentation models (they have extra outputs beyond standard detection)
                        // Threshold: 4 box coords + reasonable class count
                        if (shape[1] > SEGMENTATION_THRESHOLD)
                        {
                            info.Format = YoloFormat.Unknown;
                            return info; // This will be rejected as unsupported
                        }
                    }
                    // If middle dimension is large and last dimension is small to medium, it's YOLOv5
                    else if (shape[1] > 100 && shape[2] < 100)
                    {
                        // YOLOv5 format: [batch, num_detections, 5+num_classes]
                        info.Format = YoloFormat.YoloV5;
                        info.NumDetections = shape[1];
                        info.NumClasses = shape[2] - 5; // x, y, w, h, objectness, then classes
                        info.HasObjectness = true;
                        info.ModelInputSize = DEFAULT_MODEL_INPUT_SIZE;
                    }
                }

                // Try to detect input size from input metadata
                var inputMeta = session.InputMetadata.FirstOrDefault();
                if (inputMeta.Value != null)
                {
                    var inputShape = inputMeta.Value.Dimensions.ToArray();
                    if (inputShape.Length >= 4)
                    {
                        // Assume format: [batch, channels, height, width]
                        int height = inputShape[2] == -1 ? DEFAULT_MODEL_INPUT_SIZE : inputShape[2];
                        int width = inputShape[3] == -1 ? DEFAULT_MODEL_INPUT_SIZE : inputShape[3];
                        info.ModelInputSize = Math.Max(height, width);
                    }
                }

                return info;
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show(string.Format(LanguageManager.Instance.GetString("Msg_ModelDetectionError") ?? "Error detecting YOLO format: {0}\n\nModel may not be compatible.", ex.Message),
                    LanguageManager.Instance.GetString("Msg_ModelDetectionErrorTitle") ?? "Model Detection Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return new YoloModelInfo { Format = YoloFormat.Unknown };
            }
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

        private List<YoloDetection> ApplyEnsembleConsensus(List<YoloDetection> allDetections)
        {
            if (allDetections.Count == 0) return new List<YoloDetection>();

            float consensusIoUThreshold = Properties.Settings.Default.ConsensusIoUThreshold;
            int minimumConsensus = Properties.Settings.Default.MinimumConsensus;
            bool useWeightedAverage = Properties.Settings.Default.UseWeightedAverage;
            float confidenceThreshold = Properties.Settings.Default.AIConfidence;

            var clusters = new List<List<YoloDetection>>();
            var processed = new bool[allDetections.Count];

            for (int i = 0; i < allDetections.Count; i++)
            {
                if (processed[i]) continue;

                var cluster = new List<YoloDetection> { allDetections[i] };
                processed[i] = true;

                bool addedNewDetection;
                do
                {
                    addedNewDetection = false;

                    for (int j = 0; j < allDetections.Count; j++)
                    {
                        if (processed[j]) continue;

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

            var consensusDetections = new List<YoloDetection>();
            int effectiveMinConsensus = Math.Min(minimumConsensus, loadedModels.Count);

            foreach (var cluster in clusters)
            {
                var modelIndices = cluster.Select(d => d.ModelIndex).Distinct().ToList();

                if (modelIndices.Count >= effectiveMinConsensus ||
                    (cluster[0].Score > confidenceThreshold + 0.2f) ||
                    (loadedModels.Count == 2 && minimumConsensus == 2 &&
                     cluster[0].Score > confidenceThreshold + 0.1f))
                {
                    var detectionsToMerge = new List<YoloDetection>();
                    var usedModels = new HashSet<int>();

                    foreach (var det in cluster.OrderByDescending(d => d.Score))
                    {
                        if (!usedModels.Contains(det.ModelIndex))
                        {
                            detectionsToMerge.Add(det);
                            usedModels.Add(det.ModelIndex);
                        }
                    }

                    if (detectionsToMerge.Count > 0)
                    {
                        var merged = MergeYoloDetections(detectionsToMerge, useWeightedAverage);
                        consensusDetections.Add(merged);
                    }
                }
            }

            float ensembleIoUThreshold = Properties.Settings.Default.EnsembleIoUThreshold;
            return ApplyNMSYolo(consensusDetections, ensembleIoUThreshold);
        }

        private List<YoloDetection> ApplyEnsembleUnion(List<YoloDetection> allDetections)
        {
            if (allDetections.Count == 0) return new List<YoloDetection>();
            
            float mergeIoUThreshold = Properties.Settings.Default.EnsembleIoUThreshold;
            bool useWeightedAverage = Properties.Settings.Default.UseWeightedAverage;
            float confidenceThreshold = Properties.Settings.Default.AIConfidence;

            // Filter low confidence detections
            var validDetections = allDetections
                .Where(d => d.Score >= confidenceThreshold)
                .OrderByDescending(d => d.Score)
                .ToList();

            if (validDetections.Count == 0) return new List<YoloDetection>();

            // Merge overlapping detections (multiple models detecting the same object)
            var mergedDetections = new List<YoloDetection>();
            var processed = new bool[validDetections.Count];

            for (int i = 0; i < validDetections.Count; i++)
            {
                if (processed[i]) continue;

                var cluster = new List<YoloDetection> { validDetections[i] };
                processed[i] = true;

                // Find overlapping detections
                for (int j = i + 1; j < validDetections.Count; j++)
                {
                    if (processed[j]) continue;

                    float iou = ComputeIoUYolo(validDetections[i], validDetections[j]);
                    if (iou >= mergeIoUThreshold)
                    {
                        cluster.Add(validDetections[j]);
                        processed[j] = true;
                    }
                }

                // Merge multiple detections of the same object
                if (cluster.Count > 1)
                {
                    var merged = MergeYoloDetections(cluster, useWeightedAverage);
                    mergedDetections.Add(merged);
                }
                else
                {
                    mergedDetections.Add(cluster[0]);
                }
            }

            // Apply NMS to remove duplicate detections
            return ApplyNMSYolo(mergedDetections, mergeIoUThreshold);
        }

        private YoloDetection MergeYoloDetections(List<YoloDetection> detections, bool useWeightedAverage)
        {
            if (detections.Count == 0) return null;
            if (detections.Count == 1) return detections[0];

            // Determine final class ID by voting (most common class wins)
            // If there's a tie, use the class with highest confidence
            var classVotes = detections
                .GroupBy(d => d.ClassId)
                .Select(g => new { ClassId = g.Key, Count = g.Count(), MaxConfidence = g.Max(d => d.Confidence) })
                .OrderByDescending(x => x.Count)
                .ThenByDescending(x => x.MaxConfidence)
                .ToList();

            int finalClassId = classVotes.Count > 0 ? classVotes[0].ClassId : detections[0].ClassId;

            if (useWeightedAverage)
            {
                float totalWeight = detections.Sum(d => d.Score);

                return new YoloDetection
                {
                    CenterX = detections.Sum(d => d.CenterX * d.Score) / totalWeight,
                    CenterY = detections.Sum(d => d.CenterY * d.Score) / totalWeight,
                    Width = detections.Sum(d => d.Width * d.Score) / totalWeight,
                    Height = detections.Sum(d => d.Height * d.Score) / totalWeight,
                    Confidence = detections.Average(d => d.Confidence),
                    ClassConfidence = detections.Average(d => d.ClassConfidence),
                    ClassId = finalClassId,  // Use voted class ID
                    ModelIndex = -1
                };
            }
            else
            {
                return new YoloDetection
                {
                    CenterX = detections.Average(d => d.CenterX),
                    CenterY = detections.Average(d => d.CenterY),
                    Width = detections.Average(d => d.Width),
                    Height = detections.Average(d => d.Height),
                    Confidence = detections.Average(d => d.Confidence),
                    ClassConfidence = detections.Average(d => d.ClassConfidence),
                    ClassId = finalClassId,  // Use voted class ID
                    ModelIndex = -1
                };
            }
        }

        private List<YoloDetection> PostProcessYoloOutputDynamic(Tensor<float> outputTensor, YoloModel model, int imgWidth, int imgHeight)
        {
            var modelInfo = model.ModelInfo;
            List<YoloDetection> detections = new();
            float confidenceThreshold = Properties.Settings.Default.AIConfidence;

            float scaleX = (float)imgWidth / modelInfo.ModelInputSize;
            float scaleY = (float)imgHeight / modelInfo.ModelInputSize;

            if (modelInfo.Format == YoloFormat.YoloV5)
            {
                // Format: [batch, num_detections, 5+num_classes]
                // Layout: [x, y, w, h, objectness, class0, class1, ...]
                for (int i = 0; i < modelInfo.NumDetections; i++)
                {
                    float objectness = outputTensor[0, i, 4];
                    if (objectness < confidenceThreshold) continue;

                    float maxClassConf = 0;
                    int modelClassId = 0;
                    for (int c = 0; c < modelInfo.NumClasses; c++)
                    {
                        float classConf = outputTensor[0, i, 5 + c];
                        if (classConf > maxClassConf)
                        {
                            maxClassConf = classConf;
                            modelClassId = c;
                        }
                    }

                    // Apply class mapping: Model Class ID -> Project Class ID
                    // Skip detection if class is not mapped (user selected "nan")
                    // Only skip if ClassMapping has been configured (not null and not empty)
                    if (model.ClassMapping != null && model.ClassMapping.Count > 0 && !model.ClassMapping.ContainsKey(modelClassId))
                    {
                        continue; // Skip this detection - class is set to "nan"
                    }

                    int projectClassId = modelClassId;
                    if (model.ClassMapping != null && model.ClassMapping.ContainsKey(modelClassId))
                    {
                        projectClassId = model.ClassMapping[modelClassId];
                    }

                    float centerX = outputTensor[0, i, 0] * scaleX;
                    float centerY = outputTensor[0, i, 1] * scaleY;
                    float width = outputTensor[0, i, 2] * scaleX;
                    float height = outputTensor[0, i, 3] * scaleY;

                    detections.Add(new YoloDetection
                    {
                        CenterX = centerX,
                        CenterY = centerY,
                        Width = width,
                        Height = height,
                        Confidence = objectness,
                        ClassConfidence = maxClassConf,
                        ClassId = projectClassId  // Use mapped project class ID
                    });
                }
            }
            else if (modelInfo.Format == YoloFormat.YoloV8)
            {
                // Format: [batch, 4+num_classes, num_detections]
                // Layout: [x, y, w, h, class0, class1, ...] (transposed)
                for (int i = 0; i < modelInfo.NumDetections; i++)
                {
                    // Find max class confidence and ID
                    float maxClassConf = 0;
                    int modelClassId = 0;
                    for (int c = 0; c < modelInfo.NumClasses; c++)
                    {
                        float classConf = outputTensor[0, 4 + c, i];
                        if (classConf > maxClassConf)
                        {
                            maxClassConf = classConf;
                            modelClassId = c;
                        }
                    }

                    // YOLOv8 uses class confidence as the score (no separate objectness)
                    if (maxClassConf < confidenceThreshold) continue;

                    // Apply class mapping: Model Class ID -> Project Class ID
                    // Skip detection if class is not mapped (user selected "nan")
                    // Only skip if ClassMapping has been configured (not null and not empty)
                    if (model.ClassMapping != null && model.ClassMapping.Count > 0 && !model.ClassMapping.ContainsKey(modelClassId))
                    {
                        continue; // Skip this detection - class is set to "nan"
                    }

                    int projectClassId = modelClassId;
                    if (model.ClassMapping != null && model.ClassMapping.ContainsKey(modelClassId))
                    {
                        projectClassId = model.ClassMapping[modelClassId];
                    }

                    float centerX = outputTensor[0, 0, i] * scaleX;
                    float centerY = outputTensor[0, 1, i] * scaleY;
                    float width = outputTensor[0, 2, i] * scaleX;
                    float height = outputTensor[0, 3, i] * scaleY;

                    detections.Add(new YoloDetection
                    {
                        CenterX = centerX,
                        CenterY = centerY,
                        Width = width,
                        Height = height,
                        Confidence = maxClassConf, // YOLOv8 uses class conf as confidence
                        ClassConfidence = 1.0f,
                        ClassId = projectClassId  // Use mapped project class ID
                    });
                }
            }

            return detections;
        }

        private List<Detection> PostProcessYoloOutput(Tensor<float> outputTensor, YoloModel model, int imgWidth, int imgHeight)
        {
            var yoloDetections = PostProcessYoloOutputDynamic(outputTensor, model, imgWidth, imgHeight);
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

        // New method: Returns detection results with ClassId
        public List<(Rectangle box, int classId)> RunInferenceWithClasses(Bitmap image)
        {
            if (loadedModels.Count == 0) return new List<(Rectangle, int)>();

            if (loadedModels.Count == 1)
            {
                var detections = RunSingleModelInferenceWithClasses(image, loadedModels[0]);
                return detections.Select(d => (d.Box, d.ClassId)).ToList();
            }

            return RunEnsembleInferenceWithClasses(image);
        }

        private List<Rectangle> RunSingleModelInference(Bitmap image, YoloModel model)
        {
            try
            {
                // Convert to 24bpp first
                Bitmap processedImage = ConvertTo24bpp(image);

                // Resize image to match model's expected input size
                Bitmap resizedImage = ResizeImageForModel(processedImage, model.ModelInfo.ModelInputSize);

                // Convert resized image to float array
                float[] inputArray = BitmapToFloatArray(resizedImage);

                // Create tensor with model's expected dimensions
                var inputTensor = new DenseTensor<float>(inputArray,
                    new int[] { 1, 3, model.ModelInfo.ModelInputSize, model.ModelInfo.ModelInputSize });

                var inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor("images", inputTensor) };

                // Run inference with error handling
                using var results = model.Session.Run(inputs, model.OutputNames);
                var outputTensor = results.First().AsTensor<float>();

                // Post-process using original image dimensions for proper scaling
                var detections = PostProcessYoloOutput(outputTensor, model, image.Width, image.Height);
                var finalDetections = ApplyImprovedNMS(detections);
                TotalDetections += finalDetections.Count;

                // Clean up temporary bitmaps
                if (processedImage != image) processedImage.Dispose();
                resizedImage.Dispose();

                return finalDetections.Select(d => d.Box).ToList();
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show(
                    string.Format(LanguageManager.Instance.GetString("Msg_AIInferenceError") ?? "Error running AI inference on image:\n\n{0}\n\nImage size: {1}x{2}\nModel expected size: {3}x{3}\n\nPlease ensure your model is compatible with the image.", ex.Message, image.Width, image.Height, model.ModelInfo.ModelInputSize),
                    LanguageManager.Instance.GetString("Msg_AIInferenceErrorTitle") ?? "AI Inference Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return new List<Rectangle>();
            }
        }

        private List<Detection> RunSingleModelInferenceWithClasses(Bitmap image, YoloModel model)
        {
            try
            {
                // Convert to 24bpp first
                Bitmap processedImage = ConvertTo24bpp(image);

                // Resize image to match model's expected input size
                Bitmap resizedImage = ResizeImageForModel(processedImage, model.ModelInfo.ModelInputSize);

                // Convert resized image to float array
                float[] inputArray = BitmapToFloatArray(resizedImage);

                // Create tensor with model's expected dimensions
                var inputTensor = new DenseTensor<float>(inputArray,
                    new int[] { 1, 3, model.ModelInfo.ModelInputSize, model.ModelInfo.ModelInputSize });

                var inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor("images", inputTensor) };

                // Run inference with error handling
                using var results = model.Session.Run(inputs, model.OutputNames);
                var outputTensor = results.First().AsTensor<float>();

                // Post-process using original image dimensions for proper scaling
                var detections = PostProcessYoloOutput(outputTensor, model, image.Width, image.Height);
                var finalDetections = ApplyImprovedNMS(detections);
                TotalDetections += finalDetections.Count;

                // Clean up temporary bitmaps
                if (processedImage != image) processedImage.Dispose();
                resizedImage.Dispose();

                return finalDetections;
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show(
                    string.Format(LanguageManager.Instance.GetString("Msg_AIInferenceError") ?? "Error running AI inference on image:\n\n{0}\n\nImage size: {1}x{2}\nModel expected size: {3}x{3}\n\nPlease ensure your model is compatible with the image.", ex.Message, image.Width, image.Height, model.ModelInfo.ModelInputSize),
                    LanguageManager.Instance.GetString("Msg_AIInferenceErrorTitle") ?? "AI Inference Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return new List<Detection>();
            }
        }

        public List<Rectangle> RunEnsembleInference(Bitmap image)
        {
            try
            {
                var allYoloDetections = new List<YoloDetection>();

                // Convert to 24bpp first
                Bitmap processedImage = ConvertTo24bpp(image);

                for (int modelIdx = 0; modelIdx < loadedModels.Count; modelIdx++)
                {
                    var model = loadedModels[modelIdx];

                    try
                    {
                        // Resize image to match this model's expected input size
                        Bitmap resizedImage = ResizeImageForModel(processedImage, model.ModelInfo.ModelInputSize);

                        // Convert resized image to float array
                        float[] inputArray = BitmapToFloatArray(resizedImage);

                        // Create tensor with model's expected dimensions
                        var inputTensor = new DenseTensor<float>(inputArray,
                            new int[] { 1, 3, model.ModelInfo.ModelInputSize, model.ModelInfo.ModelInputSize });

                        var inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor("images", inputTensor) };

                        // Run inference
                        using var results = model.Session.Run(inputs, model.OutputNames);
                        var outputTensor = results.First().AsTensor<float>();

                        // Post-process using original image dimensions for proper scaling
                        var detections = PostProcessYoloOutputDynamic(outputTensor, model, image.Width, image.Height);
                        detections = ApplyNMSYolo(detections, 0.5f);

                        foreach (var det in detections)
                        {
                            det.ModelIndex = modelIdx;
                        }

                        allYoloDetections.AddRange(detections);

                        // Clean up resized image
                        resizedImage.Dispose();
                    }
                    catch (Exception modelEx)
                    {
                        // Log error but continue with other models
                        System.Diagnostics.Debug.WriteLine(
                            $"Error running model {model.Name}: {modelEx.Message}");

                        // If this is a critical error (like wrong dimensions), show warning
                        if (modelEx.Message.Contains("dimension") || modelEx.Message.Contains("shape"))
                        {
                            CustomMessageBox.Show(
                                string.Format(LanguageManager.Instance.GetString("Msg_ModelProcessingWarning") ?? "Warning: Model '{0}' failed to process the image.\n\nError: {1}\n\nContinuing with remaining models...", model.Name, modelEx.Message),
                                LanguageManager.Instance.GetString("Msg_ModelProcessingWarningTitle") ?? "Model Processing Warning",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);
                        }
                    }
                }

                // Clean up processed image
                if (processedImage != image) processedImage.Dispose();

                if (allYoloDetections.Count == 0)
                {
                    return new List<Rectangle>();
                }

                // Choose processing method based on mode
                EnsembleDetectionMode mode = (EnsembleDetectionMode)Properties.Settings.Default.EnsembleDetectionMode;
                List<YoloDetection> finalDetections;
                
                if (mode == EnsembleDetectionMode.Union)
                {
                    finalDetections = ApplyEnsembleUnion(allYoloDetections);
                }
                else // Voting mode (default)
                {
                    finalDetections = ApplyEnsembleConsensus(allYoloDetections);
                }
                
                TotalDetections += finalDetections.Count;

                return finalDetections.Select(d => d.ToRectangle()).ToList();
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show(
                    string.Format(LanguageManager.Instance.GetString("Msg_EnsembleInferenceError") ?? "Error running ensemble AI inference:\n\n{0}\n\nImage size: {1}x{2}\n\nPlease check your models and image compatibility.", ex.Message, image.Width, image.Height),
                    LanguageManager.Instance.GetString("Msg_EnsembleInferenceErrorTitle") ?? "Ensemble Inference Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return new List<Rectangle>();
            }
        }

        private List<(Rectangle box, int classId)> RunEnsembleInferenceWithClasses(Bitmap image)
        {
            try
            {
                var allYoloDetections = new List<YoloDetection>();

                // Convert to 24bpp first
                Bitmap processedImage = ConvertTo24bpp(image);

                for (int modelIdx = 0; modelIdx < loadedModels.Count; modelIdx++)
                {
                    var model = loadedModels[modelIdx];

                    try
                    {
                        // Resize image to match this model's expected input size
                        Bitmap resizedImage = ResizeImageForModel(processedImage, model.ModelInfo.ModelInputSize);

                        // Convert resized image to float array
                        float[] inputArray = BitmapToFloatArray(resizedImage);

                        // Create tensor with model's expected dimensions
                        var inputTensor = new DenseTensor<float>(inputArray,
                            new int[] { 1, 3, model.ModelInfo.ModelInputSize, model.ModelInfo.ModelInputSize });

                        var inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor("images", inputTensor) };

                        // Run inference
                        using var results = model.Session.Run(inputs, model.OutputNames);
                        var outputTensor = results.First().AsTensor<float>();

                        // Post-process using original image dimensions for proper scaling
                        var detections = PostProcessYoloOutputDynamic(outputTensor, model, image.Width, image.Height);
                        detections = ApplyNMSYolo(detections, 0.5f);

                        foreach (var det in detections)
                        {
                            det.ModelIndex = modelIdx;
                        }

                        allYoloDetections.AddRange(detections);

                        // Clean up resized image
                        resizedImage.Dispose();
                    }
                    catch (Exception modelEx)
                    {
                        // Log error but continue with other models
                        System.Diagnostics.Debug.WriteLine(
                            $"Error running model {model.Name}: {modelEx.Message}");

                        // If this is a critical error (like wrong dimensions), show warning
                        if (modelEx.Message.Contains("dimension") || modelEx.Message.Contains("shape"))
                        {
                            CustomMessageBox.Show(
                                string.Format(LanguageManager.Instance.GetString("Msg_ModelProcessingWarning") ?? "Warning: Model '{0}' failed to process the image.\n\nError: {1}\n\nContinuing with remaining models...", model.Name, modelEx.Message),
                                LanguageManager.Instance.GetString("Msg_ModelProcessingWarningTitle") ?? "Model Processing Warning",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);
                        }
                    }
                }

                // Clean up processed image
                if (processedImage != image) processedImage.Dispose();

                if (allYoloDetections.Count == 0)
                {
                    return new List<(Rectangle, int)>();
                }

                // Choose processing method based on mode
                EnsembleDetectionMode mode = (EnsembleDetectionMode)Properties.Settings.Default.EnsembleDetectionMode;
                List<YoloDetection> finalDetections;
                
                if (mode == EnsembleDetectionMode.Union)
                {
                    finalDetections = ApplyEnsembleUnion(allYoloDetections);
                }
                else // Voting mode (default)
                {
                    finalDetections = ApplyEnsembleConsensus(allYoloDetections);
                }
                
                TotalDetections += finalDetections.Count;

                return finalDetections.Select(d => (d.ToRectangle(), d.ClassId)).ToList();
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show(
                    string.Format(LanguageManager.Instance.GetString("Msg_EnsembleInferenceError") ?? "Error running ensemble AI inference:\n\n{0}\n\nImage size: {1}x{2}\n\nPlease check your models and image compatibility.", ex.Message, image.Width, image.Height),
                    LanguageManager.Instance.GetString("Msg_EnsembleInferenceErrorTitle") ?? "Ensemble Inference Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return new List<(Rectangle, int)>();
            }
        }

        public void LoadYoloModel()
        {
            OpenModelManager();
        }

        public void OpenModelManager(List<LabelClass> projectClasses = null, Dictionary<string, Dictionary<int, int>> savedMappings = null)
        {
            var dialog = new ModelManagerDialog(this, projectClasses, savedMappings);

            // Only set owner if MainWindow is available and shown
            if (Application.Current.MainWindow != null && Application.Current.MainWindow.IsLoaded)
            {
                dialog.Owner = Application.Current.MainWindow;
            }

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
            {
                string formatName = m.ModelInfo.Format switch
                {
                    YoloFormat.YoloV5 => "YOLOv5",
                    YoloFormat.YoloV8 => "YOLOv8",
                    _ => "Unknown"
                };
                return $"{m.Name} ({formatName})" == modelName;
            });

            if (model != null)
            {
                model.Session?.Dispose();
                loadedModels.Remove(model);
            }
        }

        public YoloModel LoadModelFromPath(string modelPath)
        {
            return LoadModelFromPath(modelPath, silent: false);
        }

        /// <summary>
        /// Loads a YOLO model from file path (silent mode for project restoration)
        /// </summary>
        public YoloModel LoadModelFromPathSilent(string modelPath)
        {
            return LoadModelFromPath(modelPath, silent: true);
        }

        private YoloModel LoadModelFromPath(string modelPath, bool silent)
        {
            try
            {
                if (loadedModels.Any(m => m.ModelPath == modelPath))
                {
                    if (!silent)
                    {
                        CustomMessageBox.Show(LanguageManager.Instance.GetString("Msg_ModelAlreadyLoaded") ?? "This model is already loaded.", LanguageManager.Instance.GetString("Msg_ModelAlreadyLoadedTitle") ?? "Model Already Loaded",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    return loadedModels.FirstOrDefault(m => m.ModelPath == modelPath);
                }

                var model = new YoloModel
                {
                    ModelPath = modelPath,
                    Name = Path.GetFileNameWithoutExtension(modelPath)
                };

                if (!TryInitializeSession(model, Properties.Settings.Default.UseGPU, out string errorMessage))
                {
                    if (!silent)
                    {
                        CustomMessageBox.Show(string.Format(LanguageManager.Instance.GetString("Msg_GPUInitFailed") ?? "GPU initialization failed: {0}\nFalling back to CPU.", errorMessage),
                                      LanguageManager.Instance.GetString("Msg_GPUWarning") ?? "GPU Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }

                    if (!TryInitializeSession(model, false, out errorMessage))
                    {
                        if (!silent)
                        {
                            CustomMessageBox.Show(string.Format(LanguageManager.Instance.GetString("Msg_ModelInitFailed") ?? "Failed to initialize model: {0}", errorMessage),
                                          LanguageManager.Instance.GetString("Msg_Error") ?? "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                        return null;
                    }
                }

                YoloModelInfo modelInfo = DetectYoloFormat(model.Session);
                if (modelInfo.Format == YoloFormat.Unknown)
                {
                    model.Session?.Dispose();
                    if (!silent)
                    {
                        CustomMessageBox.Show(LanguageManager.Instance.GetString("Msg_UnsupportedModel") ?? "Failed to detect YOLO model format or unsupported model type.\n\nSupported formats:\n- YOLOv5: [batch, detections, 5+classes]\n- YOLOv8: [batch, 4+classes, detections]\n\nNote: Segmentation models are not supported.",
                                      LanguageManager.Instance.GetString("Msg_UnsupportedModelTitle") ?? "Unsupported Model", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    return null;
                }

                // Try to get class names from metadata to verify/correct class count
                try
                {
                    var metadata = model.Session.ModelMetadata;
                    if (metadata != null)
                    {
                        var metadataType = metadata.GetType();
                        var customMetadataProp = metadataType.GetProperty("CustomMetadataMap");
                        
                        if (customMetadataProp != null)
                        {
                            var customMetadata = customMetadataProp.GetValue(metadata) as IReadOnlyDictionary<string, string>;
                            if (customMetadata != null)
                            {
                                var classNamesKeys = new[] { "names", "class_names", "classes", "labels", "class.names", "yolo.names" };
                                
                                foreach (var key in classNamesKeys)
                                {
                                    if (customMetadata.TryGetValue(key, out string classNamesValue))
                                    {
                                        var classNames = ParseClassNamesFromMetadata(classNamesValue);
                                        if (classNames != null && classNames.Count > 0)
                                        {
                                            // If metadata has more classes than detected, use metadata count
                                            // This handles cases where model output shape detection is incorrect
                                            if (classNames.Count > modelInfo.NumClasses)
                                            {
                                                Debug.WriteLine($"Metadata has {classNames.Count} classes, but model detected {modelInfo.NumClasses}. Using metadata count.");
                                                modelInfo.NumClasses = classNames.Count;
                                            }
                                            break; // Found class names, no need to check other keys
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error checking metadata for class count: {ex.Message}");
                    // Continue with detected class count
                }

                model.ModelInfo = modelInfo;
                loadedModels.Add(model);

                if (!silent)
                {
                    string formatName = modelInfo.Format switch
                    {
                        YoloFormat.YoloV5 => "YOLOv5",
                        YoloFormat.YoloV8 => "YOLOv8",
                        _ => "Unknown"
                    };

                    CustomMessageBox.Show(string.Format(LanguageManager.Instance.GetString("Msg_ModelLoadedSuccess") ?? "Model '{0}' loaded successfully.\nFormat: {1}\nClasses: {2}\nDetections: {3}\nInput Size: {4}x{4}\nTotal models: {5}", model.Name, formatName, modelInfo.NumClasses, modelInfo.NumDetections, modelInfo.ModelInputSize, loadedModels.Count),
                        LanguageManager.Instance.GetString("Msg_ModelLoaded") ?? "Model Loaded", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    Debug.WriteLine($"Model '{model.Name}' loaded silently (project restoration)");
                }

                return model;
            }
            catch (Exception ex)
            {
                if (!silent)
                {
                    CustomMessageBox.Show(string.Format(LanguageManager.Instance.GetString("Msg_ErrorLoadingModel") ?? "Error loading model: {0}", ex.Message),
                                  LanguageManager.Instance.GetString("Msg_Error") ?? "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                else
                {
                    Debug.WriteLine($"Error loading model silently: {ex.Message}");
                }
                return null;
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

        /// <summary>
        /// Gets class file search patterns from configuration or defaults
        /// </summary>
        private static List<string> GetClassFileSearchPatterns()
        {
            // Try to get from settings, fallback to defaults
            var patterns = new List<string>();
            
            // Check if there's a custom configuration (could be extended to read from settings file)
            // For now, use common YOLO patterns
            var defaultPatterns = new[]
            {
                "classes.txt",
                "names.txt",
                "data.names",
                "{model_name}.names",
                "{model_name}.txt",
                "{model_name}_classes.txt",
                "{model_name}_names.txt",
                "labels.txt",
                "label.txt"
            };
            
            patterns.AddRange(defaultPatterns);
            return patterns;
        }

        /// <summary>
        /// Gets subdirectory names to search for class files
        /// </summary>
        private static List<string> GetClassFileSubdirectories()
        {
            // Could be extended to read from configuration
            return new List<string> { "data", "labels", "classes", "config" };
        }

        /// <summary>
        /// Parses class names from metadata value (supports JSON, Python dict format, comma-separated, newline-separated formats)
        /// </summary>
        private static List<string> ParseClassNamesFromMetadata(string metadataValue)
        {
            if (string.IsNullOrWhiteSpace(metadataValue))
                return null;

            try
            {
                string trimmed = metadataValue.Trim();
                
                // Try Python dictionary format first (e.g., {0: 'player', 1: 'head'})
                if (trimmed.StartsWith("{") && trimmed.EndsWith("}"))
                {
                    // Check if it's Python dict format (single quotes, unquoted keys)
                    if (trimmed.Contains("'") || trimmed.Contains(":"))
                    {
                        var pythonDict = ParsePythonDictFormat(trimmed);
                        if (pythonDict != null && pythonDict.Count > 0)
                        {
                            return pythonDict.OrderBy(kvp => kvp.Key)
                                .Select(kvp => kvp.Value)
                                .ToList();
                        }
                    }
                    
                    // Try standard JSON format (e.g., {"0": "person", "1": "bicycle"})
                    try
                    {
                        var jsonDict = JsonSerializer.Deserialize<Dictionary<string, string>>(trimmed);
                        if (jsonDict != null && jsonDict.Count > 0)
                        {
                            return jsonDict.OrderBy(kvp => int.TryParse(kvp.Key, out int k) ? k : int.MaxValue)
                                .Select(kvp => kvp.Value)
                                .ToList();
                        }
                    }
                    catch
                    {
                        // Not valid JSON, continue to other formats
                    }
                }
                else if (trimmed.StartsWith("["))
                {
                    // Array format
                    try
                    {
                        var jsonArray = JsonSerializer.Deserialize<string[]>(trimmed);
                        if (jsonArray != null && jsonArray.Length > 0)
                        {
                            return jsonArray.ToList();
                        }
                    }
                    catch
                    {
                        // Not valid JSON array, continue
                    }
                }
                
                // Try comma-separated format
                if (metadataValue.Contains(","))
                {
                    return metadataValue.Split(',')
                        .Select(s => s.Trim().Trim('\'', '"'))
                        .Where(s => !string.IsNullOrEmpty(s))
                        .ToList();
                }
                
                // Try newline-separated format
                if (metadataValue.Contains("\n") || metadataValue.Contains("\r"))
                {
                    return metadataValue.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(s => s.Trim().Trim('\'', '"'))
                        .Where(s => !string.IsNullOrEmpty(s))
                        .ToList();
                }
                
                // Single value (unlikely but possible)
                return new List<string> { metadataValue.Trim().Trim('\'', '"') };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error parsing class names from metadata: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Parses Python dictionary format string (e.g., {0: 'player', 1: 'head'})
        /// </summary>
        private static Dictionary<int, string> ParsePythonDictFormat(string pythonDictStr)
        {
            try
            {
                var result = new Dictionary<int, string>();
                
                // Remove outer braces
                string content = pythonDictStr.Trim('{', '}').Trim();
                if (string.IsNullOrEmpty(content))
                    return null;

                // Split by comma, but be careful with commas inside quotes
                var entries = new List<string>();
                int depth = 0;
                int start = 0;
                bool inSingleQuote = false;
                bool inDoubleQuote = false;

                for (int i = 0; i < content.Length; i++)
                {
                    char c = content[i];
                    
                    if (c == '\'' && (i == 0 || content[i - 1] != '\\'))
                        inSingleQuote = !inSingleQuote;
                    else if (c == '"' && (i == 0 || content[i - 1] != '\\'))
                        inDoubleQuote = !inDoubleQuote;
                    else if (!inSingleQuote && !inDoubleQuote)
                    {
                        if (c == '[' || c == '(' || c == '{')
                            depth++;
                        else if (c == ']' || c == ')' || c == '}')
                            depth--;
                        else if (c == ',' && depth == 0)
                        {
                            entries.Add(content.Substring(start, i - start).Trim());
                            start = i + 1;
                        }
                    }
                }
                
                // Add last entry
                if (start < content.Length)
                    entries.Add(content.Substring(start).Trim());

                // Parse each entry
                foreach (var entry in entries)
                {
                    if (string.IsNullOrWhiteSpace(entry))
                        continue;

                    // Split by colon
                    int colonIndex = entry.IndexOf(':');
                    if (colonIndex < 0)
                        continue;

                    string keyStr = entry.Substring(0, colonIndex).Trim();
                    string valueStr = entry.Substring(colonIndex + 1).Trim();

                    // Parse key (remove quotes if any)
                    keyStr = keyStr.Trim('\'', '"');
                    if (int.TryParse(keyStr, out int key))
                    {
                        // Parse value (remove quotes)
                        valueStr = valueStr.Trim('\'', '"');
                        if (!string.IsNullOrEmpty(valueStr))
                        {
                            result[key] = valueStr;
                        }
                    }
                }

                return result.Count > 0 ? result : null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error parsing Python dict format: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Reads class names directly from ONNX file using ONNX library (if available)
        /// </summary>
        private static List<string> ReadClassNamesFromOnnxFile(string modelPath)
        {
            try
            {
                // Try to use ONNX library to read metadata_props
                // This requires the Microsoft.ML.OnnxRuntime package or ONNX library
                // For now, we'll try to read the file and parse it manually
                
                // Note: Direct ONNX file parsing would require the ONNX NuGet package
                // which may not be available. We'll use a simpler approach.
                
                return null; // Placeholder - can be extended if ONNX library is available
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Gets class names for a model (tries multiple file names and locations, falls back to COCO or generic names)
        /// </summary>
        public static List<string> GetModelClassNames(string modelPath, int numClasses)
        {
            if (string.IsNullOrEmpty(modelPath) || numClasses <= 0)
            {
                return Enumerable.Range(0, numClasses)
                    .Select(i => $"class_{i}")
                    .ToList();
            }

            string modelDir = Path.GetDirectoryName(modelPath);
            string modelNameWithoutExt = Path.GetFileNameWithoutExtension(modelPath);

            Debug.WriteLine($"GetModelClassNames: Looking for class files for model: {modelPath}");
            Debug.WriteLine($"Model directory: {modelDir}");
            Debug.WriteLine($"Model name (no ext): {modelNameWithoutExt}");
            Debug.WriteLine($"Expected classes: {numClasses}");

            // Get search patterns from configuration
            var searchPatterns = GetClassFileSearchPatterns();
            var subdirectoryNames = GetClassFileSubdirectories();
            var possibleClassFiles = new List<string>();

            // Build file paths from patterns
            foreach (var pattern in searchPatterns)
            {
                // Replace {model_name} placeholder
                string fileName = pattern.Replace("{model_name}", modelNameWithoutExt);
                possibleClassFiles.Add(Path.Combine(modelDir, fileName));
            }

            // Also try parent directory
            string parentDir = Path.GetDirectoryName(modelDir);
            if (!string.IsNullOrEmpty(parentDir))
            {
                foreach (var pattern in searchPatterns)
                {
                    string fileName = pattern.Replace("{model_name}", modelNameWithoutExt);
                    possibleClassFiles.Add(Path.Combine(parentDir, fileName));
                }
            }

            // Try subdirectories in model directory
            if (Directory.Exists(modelDir))
            {
                try
                {
                    var subDirs = Directory.GetDirectories(modelDir);
                    foreach (var subDir in subDirs)
                    {
                        var subDirName = Path.GetFileName(subDir).ToLower();
                        // Check if subdirectory matches configured names
                        if (subdirectoryNames.Contains(subDirName))
                        {
                            foreach (var pattern in searchPatterns)
                            {
                                string fileName = pattern.Replace("{model_name}", modelNameWithoutExt);
                                possibleClassFiles.Add(Path.Combine(subDir, fileName));
                            }
                        }
                    }
                }
                catch
                {
                    // Ignore errors when accessing subdirectories
                }
            }

            Debug.WriteLine($"Trying {possibleClassFiles.Count} possible class file locations:");
            foreach (var file in possibleClassFiles)
            {
                Debug.WriteLine($"  - {file} (exists: {File.Exists(file)})");
            }

            // Try each possible file
            foreach (string classFile in possibleClassFiles)
            {
                if (File.Exists(classFile))
                {
                    try
                    {
                        // Try reading with UTF-8 first, then fallback to default encoding
                        string[] fileLines = null;
                        try
                        {
                            fileLines = File.ReadAllLines(classFile, System.Text.Encoding.UTF8);
                        }
                        catch
                        {
                            fileLines = File.ReadAllLines(classFile);
                        }

                        var lines = fileLines
                        .Where(line => !string.IsNullOrWhiteSpace(line))
                        .Select(line => line.Trim())
                            .Where(line => !line.StartsWith("#") && !line.StartsWith("//")) // Skip comments
                        .ToList();
                    
                        if (lines.Count == 0)
                        {
                            Debug.WriteLine($"Class file {classFile} exists but contains no valid lines (after filtering comments/empty lines)");
                            continue;
                        }

                        Debug.WriteLine($"Reading class file: {classFile}");
                        Debug.WriteLine($"Found {lines.Count} non-empty lines");

                        // Try to parse as dictionary format (key: value or key=value)
                        Dictionary<int, string> classDict = new Dictionary<int, string>();
                        bool isDictFormat = false;
                        int dictEntries = 0;

                        foreach (var line in lines)
                        {
                            // Try "key: value" format (e.g., "0: body" or "0 : body")
                            if (line.Contains(":"))
                            {
                                var parts = line.Split(new[] { ':' }, 2);
                                if (parts.Length == 2 && int.TryParse(parts[0].Trim(), out int key))
                                {
                                    string value = parts[1].Trim();
                                    if (!string.IsNullOrEmpty(value))
                                    {
                                        classDict[key] = value;
                                        isDictFormat = true;
                                        dictEntries++;
                                    }
                                }
                            }
                            // Try "key=value" format (e.g., "0=body")
                            else if (line.Contains("="))
                            {
                                var parts = line.Split(new[] { '=' }, 2);
                                if (parts.Length == 2 && int.TryParse(parts[0].Trim(), out int key))
                                {
                                    string value = parts[1].Trim();
                                    if (!string.IsNullOrEmpty(value))
                                    {
                                        classDict[key] = value;
                                        isDictFormat = true;
                                        dictEntries++;
                                    }
                                }
                            }
                        }

                        if (isDictFormat)
                        {
                            Debug.WriteLine($"Detected dictionary format with {dictEntries} entries");
                        }
                        else
                        {
                            Debug.WriteLine($"Using simple list format (one class per line)");
                        }

                        List<string> classNames;

                        if (isDictFormat && classDict.Count > 0)
                        {
                            // Sort by key and extract values (like Python: [class_names[i] for i in sorted(class_names.keys())])
                            classNames = classDict.OrderBy(kvp => kvp.Key)
                                .Select(kvp => kvp.Value)
                                .ToList();
                        }
                        else
                        {
                            // Simple list format (one class name per line, index = line number)
                            classNames = lines;
                        }

                        // Validate that we have valid class names
                        if (classNames.Count == 0)
                        {
                            Debug.WriteLine($"Class file {classFile} exists but contains no valid class names");
                            continue;
                        }

                        // If we got the right number of classes, use them
                    if (classNames.Count == numClasses)
                    {
                            Debug.WriteLine($"✓ Successfully loaded {classNames.Count} class names from {classFile}");
                            Debug.WriteLine($"  Classes: {string.Join(", ", classNames)}");
                        return classNames;
                    }
                        // If we got more classes than needed, take the first numClasses
                        else if (classNames.Count > numClasses)
                        {
                            Debug.WriteLine($"Loaded {classNames.Count} class names from {classFile}, using first {numClasses}");
                            return classNames.Take(numClasses).ToList();
                        }
                        // If we got fewer classes, pad with generic names
                        else if (classNames.Count > 0 && classNames.Count < numClasses)
                        {
                            Debug.WriteLine($"Loaded {classNames.Count} class names from {classFile}, padding to {numClasses}");
                            var result = new List<string>(classNames);
                            for (int i = classNames.Count; i < numClasses; i++)
                            {
                                result.Add($"class_{i}");
                            }
                            return result;
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error reading class file {classFile}: {ex.Message}");
                        // Continue to next file
                    }
                }
            }

            // Try to read from ONNX model metadata (if available)
            // ONNX models can store class names in metadata_props
            try
            {
                // Use reflection to access CustomMetadataMap if available
                using (var session = new InferenceSession(modelPath))
                {
                    var metadata = session.ModelMetadata;
                    if (metadata != null)
                    {
                        Debug.WriteLine($"Model metadata found. Producer: {metadata.ProducerName}, Domain: {metadata.Domain}, Description: {metadata.Description}");

                        // Try to access custom metadata using reflection
                        // ONNX Runtime's ModelMetadata may store custom properties in different ways
                        var metadataType = metadata.GetType();
                        
                        // Try multiple possible property names
                        var possiblePropertyNames = new[] { "CustomMetadataMap", "MetadataProps", "CustomMetadata", "Properties" };
                        
                        foreach (var propName in possiblePropertyNames)
                        {
                            try
                            {
                                var prop = metadataType.GetProperty(propName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                                if (prop != null)
                                {
                                    var value = prop.GetValue(metadata);
                                    
                                    // Try as dictionary
                                    if (value is IReadOnlyDictionary<string, string> customMetadata)
                                    {
                                        if (customMetadata.Count > 0)
                                        {
                                            Debug.WriteLine($"Found {customMetadata.Count} custom metadata entries via {propName}");
                                            
                                            // Try common keys for class names
                                            var classNamesKeys = new[] { "names", "class_names", "classes", "labels", "class.names", "yolo.names" };
                                            
                                            foreach (var key in classNamesKeys)
                                            {
                                                if (customMetadata.TryGetValue(key, out string classNamesValue))
                                                {
                                                    Debug.WriteLine($"Found class names in metadata key '{key}': {classNamesValue}");
                                                    
                                                    var classNames = ParseClassNamesFromMetadata(classNamesValue);
                                                    
                                                    if (classNames != null && classNames.Count > 0)
                                                    {
                                                        // If metadata has more classes than detected, use all classes from metadata
                                                        // This handles cases where model output shape detection is incorrect
                                                        if (classNames.Count >= numClasses)
                                                        {
                                                            if (classNames.Count == numClasses)
                                                            {
                                                                Debug.WriteLine($"✓ Successfully loaded {classNames.Count} class names from model metadata");
                                                            }
                                                            else
                                                            {
                                                                Debug.WriteLine($"⚠ Metadata has {classNames.Count} classes, but model detected {numClasses}. Using all {classNames.Count} classes from metadata.");
                                                            }
                                                            return classNames;
                                                        }
                                                        else
                                                        {
                                                            // Pad with generic names if needed
                                                            Debug.WriteLine($"Loaded {classNames.Count} class names from metadata, but model expects {numClasses}. Padding with generic names.");
                                                            var result = new List<string>(classNames);
                                                            for (int i = classNames.Count; i < numClasses; i++)
                                                            {
                                                                result.Add($"class_{i}");
                                                            }
                                                            return result;
                                                        }
                                                    }
                                                }
                                            }
                                            
                                            // Log all available metadata keys for debugging
                                            Debug.WriteLine("Available metadata keys:");
                                            foreach (var kvp in customMetadata)
                                            {
                                                var preview = kvp.Value?.Length > 100 ? kvp.Value.Substring(0, 100) + "..." : kvp.Value;
                                                Debug.WriteLine($"  - {kvp.Key}: {preview}");
                                            }
                                        }
                                    }
                                    // Try as collection of metadata props
                                    else if (value is System.Collections.IEnumerable enumerable)
                                    {
                                        foreach (var item in enumerable)
                                        {
                                            // Try to extract key-value pairs from metadata prop objects
                                            var itemType = item.GetType();
                                            var keyProp = itemType.GetProperty("Key") ?? itemType.GetProperty("key");
                                            var valueProp = itemType.GetProperty("Value") ?? itemType.GetProperty("value");
                                            
                                            if (keyProp != null && valueProp != null)
                                            {
                                                var key = keyProp.GetValue(item)?.ToString();
                                                var val = valueProp.GetValue(item)?.ToString();
                                                
                                                if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(val))
                                                {
                                                    var classNamesKeys = new[] { "names", "class_names", "classes", "labels" };
                                                    if (classNamesKeys.Contains(key.ToLower()))
                                                    {
                                                        Debug.WriteLine($"Found class names in metadata '{key}': {val}");
                                                        var classNames = ParseClassNamesFromMetadata(val);
                                                        
                                                        if (classNames != null && classNames.Count > 0)
                                                        {
                                                            // If metadata has more classes than detected, use all classes from metadata
                                                            if (classNames.Count >= numClasses)
                                                            {
                                                                if (classNames.Count == numClasses)
                                                                {
                                                                    Debug.WriteLine($"✓ Successfully loaded {classNames.Count} class names from model metadata");
                                                                }
                                                                else
                                                                {
                                                                    Debug.WriteLine($"⚠ Metadata has {classNames.Count} classes, but model detected {numClasses}. Using all {classNames.Count} classes from metadata.");
                                                                }
                                                                return classNames;
                                                            }
                                                            else
                                                            {
                                                                // Pad with generic names if needed
                                                                var result = new List<string>(classNames);
                                                                for (int i = classNames.Count; i < numClasses; i++)
                                                                {
                                                                    result.Add($"class_{i}");
                                                                }
                                                                return result;
                                                            }
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"Error accessing metadata property {propName}: {ex.Message}");
                            }
                        }
                        
                        // Also try reading directly from ONNX file using ONNX library if available
                        // This requires the ONNX NuGet package
                        try
                        {
                            var classNamesFromFile = ReadClassNamesFromOnnxFile(modelPath);
                            if (classNamesFromFile != null && classNamesFromFile.Count > 0)
                            {
                                if (classNamesFromFile.Count == numClasses)
                                {
                                    Debug.WriteLine($"✓ Successfully loaded {classNamesFromFile.Count} class names from ONNX file metadata");
                                    return classNamesFromFile;
                                }
                                else if (classNamesFromFile.Count >= numClasses)
                                {
                                    Debug.WriteLine($"Loaded {classNamesFromFile.Count} class names from ONNX file, using first {numClasses}");
                                    return classNamesFromFile.Take(numClasses).ToList();
                                }
                            }
                        }
                        catch (Exception onnxEx)
                        {
                            Debug.WriteLine($"Could not read from ONNX file directly: {onnxEx.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error reading model metadata: {ex.Message}");
            }

            // Don't use COCO as fallback unless explicitly requested or numClasses exactly matches
            // This prevents incorrect class names when the model has a different class set
            Debug.WriteLine($"WARNING: Could not find class names file for model: {modelPath}");
            Debug.WriteLine($"No class file found in any of the {possibleClassFiles.Count} attempted locations.");
            Debug.WriteLine($"Generating generic class names (class_0, class_1, ...) for {numClasses} classes.");
            Debug.WriteLine($"Please use 'Load Class File' button in the mapping dialog to manually load the correct class names.");

            // Generate generic names - user should manually load the correct class file
            return Enumerable.Range(0, numClasses)
                .Select(i => $"class_{i}")
                .ToList();
        }
    }
}