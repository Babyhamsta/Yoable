﻿using Microsoft.ML.OnnxRuntime;
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

    public enum YoloFormat
    {
        Unknown,
        YoloV5,      // [batch, num_detections, 5+num_classes]
        YoloV8       // [batch, 4+num_classes, num_detections]
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
                        else if (i == shape.Length - 1) shape[i] = 8400; // Common detection count
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
                        info.ModelInputSize = 640; // Default

                        // Reject segmentation models (they have extra outputs beyond standard detection)
                        if (shape[1] > 84) // Standard COCO is 80 classes, so 84 = 4 box coords + 80 classes
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
                        info.ModelInputSize = 640; // Default
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
                        int height = inputShape[2] == -1 ? 640 : inputShape[2];
                        int width = inputShape[3] == -1 ? 640 : inputShape[3];
                        info.ModelInputSize = Math.Max(height, width);
                    }
                }

                return info;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error detecting YOLO format: {ex.Message}\n\nModel may not be compatible.",
                    "Model Detection Error", MessageBoxButton.OK, MessageBoxImage.Error);
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

        private YoloDetection MergeYoloDetections(List<YoloDetection> detections, bool useWeightedAverage)
        {
            if (detections.Count == 0) return null;
            if (detections.Count == 1) return detections[0];

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
                    ClassId = detections[0].ClassId,
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
                    ClassId = detections[0].ClassId,
                    ModelIndex = -1
                };
            }
        }

        private List<YoloDetection> PostProcessYoloOutputDynamic(Tensor<float> outputTensor, YoloModelInfo modelInfo, int imgWidth, int imgHeight)
        {
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
                    int classId = 0;
                    for (int c = 0; c < modelInfo.NumClasses; c++)
                    {
                        float classConf = outputTensor[0, i, 5 + c];
                        if (classConf > maxClassConf)
                        {
                            maxClassConf = classConf;
                            classId = c;
                        }
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
                        ClassId = classId
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
                    int classId = 0;
                    for (int c = 0; c < modelInfo.NumClasses; c++)
                    {
                        float classConf = outputTensor[0, 4 + c, i];
                        if (classConf > maxClassConf)
                        {
                            maxClassConf = classConf;
                            classId = c;
                        }
                    }

                    // YOLOv8 uses class confidence as the score (no separate objectness)
                    if (maxClassConf < confidenceThreshold) continue;

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
                        ClassId = classId
                    });
                }
            }

            return detections;
        }

        private List<Detection> PostProcessYoloOutput(Tensor<float> outputTensor, YoloModelInfo modelInfo, int imgWidth, int imgHeight)
        {
            var yoloDetections = PostProcessYoloOutputDynamic(outputTensor, modelInfo, imgWidth, imgHeight);
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
                var detections = PostProcessYoloOutput(outputTensor, model.ModelInfo, image.Width, image.Height);
                var finalDetections = ApplyImprovedNMS(detections);
                TotalDetections += finalDetections.Count;

                // Clean up temporary bitmaps
                if (processedImage != image) processedImage.Dispose();
                resizedImage.Dispose();

                return finalDetections.Select(d => d.Box).ToList();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Error running AI inference on image:\n\n{ex.Message}\n\n" +
                    $"Image size: {image.Width}x{image.Height}\n" +
                    $"Model expected size: {model.ModelInfo.ModelInputSize}x{model.ModelInfo.ModelInputSize}\n\n" +
                    $"Please ensure your model is compatible with the image.",
                    "AI Inference Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return new List<Rectangle>();
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
                        var detections = PostProcessYoloOutputDynamic(outputTensor, model.ModelInfo, image.Width, image.Height);
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
                            MessageBox.Show(
                                $"Warning: Model '{model.Name}' failed to process the image.\n\n" +
                                $"Error: {modelEx.Message}\n\n" +
                                $"Continuing with remaining models...",
                                "Model Processing Warning",
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

                var consensusDetections = ApplyEnsembleConsensus(allYoloDetections);
                TotalDetections += consensusDetections.Count;

                return consensusDetections.Select(d => d.ToRectangle()).ToList();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Error running ensemble AI inference:\n\n{ex.Message}\n\n" +
                    $"Image size: {image.Width}x{image.Height}\n\n" +
                    $"Please check your models and image compatibility.",
                    "Ensemble Inference Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return new List<Rectangle>();
            }
        }

        public void LoadYoloModel()
        {
            OpenModelManager();
        }

        public void OpenModelManager()
        {
            var dialog = new ModelManagerDialog(this);

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

                YoloModelInfo modelInfo = DetectYoloFormat(model.Session);
                if (modelInfo.Format == YoloFormat.Unknown)
                {
                    model.Session?.Dispose();
                    MessageBox.Show("Failed to detect YOLO model format or unsupported model type.\n\n" +
                                  "Supported formats:\n" +
                                  "- YOLOv5: [batch, detections, 5+classes]\n" +
                                  "- YOLOv8: [batch, 4+classes, detections]\n\n" +
                                  "Note: Segmentation models are not supported.",
                                  "Unsupported Model", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                model.ModelInfo = modelInfo;
                loadedModels.Add(model);

                string formatName = modelInfo.Format switch
                {
                    YoloFormat.YoloV5 => "YOLOv5",
                    YoloFormat.YoloV8 => "YOLOv8",
                    _ => "Unknown"
                };

                MessageBox.Show($"Model '{model.Name}' loaded successfully.\n" +
                              $"Format: {formatName}\n" +
                              $"Classes: {modelInfo.NumClasses}\n" +
                              $"Detections: {modelInfo.NumDetections}\n" +
                              $"Input Size: {modelInfo.ModelInputSize}x{modelInfo.ModelInputSize}\n" +
                              $"Total models: {loadedModels.Count}",
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