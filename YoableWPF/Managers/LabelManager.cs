using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace YoableWPF.Managers
{
    public class LabelManager
    {
        // Use thread-safe dictionary for concurrent label loading
        private ConcurrentDictionary<string, List<LabelData>> labelStorage = new();

        public ConcurrentDictionary<string, List<LabelData>> LabelStorage => labelStorage;

        // Configurable batch size for label loading
        public int LabelLoadBatchSize { get; set; } = 500; // Default: process 500 files at a time

        // Track valid class IDs for orphan detection
        private HashSet<int> validClassIds = new HashSet<int> { 0 }; // Always include default class
        private int defaultClassId = 0;
        private static readonly CultureInfo CommaCulture = CultureInfo.GetCultureInfo("de-DE"); // Comma decimal separator

        /// <summary>
        /// Sets the valid class IDs from the project. Call this when project classes change.
        /// </summary>
        public void SetValidClassIds(IEnumerable<int> classIds)
        {
            validClassIds = new HashSet<int>(classIds);
            
            // Ensure we always have at least one valid class (default)
            if (validClassIds.Count == 0)
            {
                validClassIds.Add(0);
            }
            
            // Set default to 0 if it exists, otherwise use the first available class
            defaultClassId = validClassIds.Contains(0) ? 0 : validClassIds.First();
        }

        /// <summary>
        /// Validates and fixes a label's ClassId if it references a non-existent class
        /// </summary>
        private bool ValidateAndFixClassId(LabelData label)
        {
            if (!validClassIds.Contains(label.ClassId))
            {
                label.ClassId = defaultClassId;
                return true; // Indicates the label was fixed
            }
            return false; // No fix needed
        }

        /// <summary>
        /// Fixes all orphaned labels across all images
        /// </summary>
        public int FixAllOrphanedLabels()
        {
            int fixedCount = 0;

            foreach (var kvp in labelStorage.ToArray())
            {
                bool wasFixed = false;
                var updatedLabels = new List<LabelData>(kvp.Value.Count);

                foreach (var label in kvp.Value)
                {
                    var copy = new LabelData(label);
                    if (ValidateAndFixClassId(copy))
                    {
                        fixedCount++;
                        wasFixed = true;
                    }
                    updatedLabels.Add(copy);
                }

                if (wasFixed)
                {
                    SaveLabels(kvp.Key, updatedLabels);
                }
            }

            return fixedCount;
        }

        // Direct port of label management methods
        public List<LabelData> GetLabels(string fileName)
        {
            if (!labelStorage.TryGetValue(fileName, out var labels))
                return new List<LabelData>();

            // Work on a copy to avoid mutating storage from callers
            var labelCopies = labels.Select(label => new LabelData(label)).ToList();

            // Validate and fix any orphaned ClassIds before returning
            bool wasFixed = false;
            foreach (var label in labelCopies)
            {
                if (ValidateAndFixClassId(label))
                {
                    wasFixed = true;
                }
            }

            if (wasFixed)
            {
                SaveLabels(fileName, labelCopies);
            }

            return labelCopies;
        }

        public void SaveLabels(string fileName, List<LabelData> labels)
        {
            labelStorage.AddOrUpdate(fileName,
                new List<LabelData>(labels),
                (k, v) => new List<LabelData>(labels));
        }

        public void ClearAll()
        {
            labelStorage.Clear();
        }

        public bool RemoveLabels(string fileName)
        {
            return labelStorage.TryRemove(fileName, out _);
        }

        // Helper method to parse floats with either comma or period as decimal separator
        private float ParseFloat(string value)
        {
            // First try with invariant culture (period as decimal separator)
            if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float result))
            {
                return result;
            }

            // If that fails, try with a culture that uses comma as decimal separator
            if (float.TryParse(value, NumberStyles.Float, CommaCulture, out result))
            {
                return result;
            }

            // Last resort: replace comma with period and try again
            value = value.Replace(',', '.');
            if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result))
            {
                return result;
            }

            throw new FormatException($"Unable to parse '{value}' as a float with either comma or period decimal separator.");
        }

        /// <summary>
        /// NEW: High-performance async batch label loader for large datasets
        /// Loads labels in parallel batches with progress reporting
        /// </summary>
        public async Task<(int labelsLoaded, HashSet<int> foundClassIds)> LoadYoloLabelsBatchAsync(
            string labelsDirectory,
            ImageManager imageManager,
            IProgress<(int current, int total, string message)> progress = null,
            CancellationToken cancellationToken = default,
            bool enableParallelProcessing = true)
        {
            if (!Directory.Exists(labelsDirectory))
                return (0, new HashSet<int>());

            // Get all .txt label files
            var labelFiles = Directory.GetFiles(labelsDirectory, "*.txt").ToArray();

            return await LoadYoloLabelsBatchAsync(labelFiles, imageManager, progress, cancellationToken, enableParallelProcessing);
        }

        /// <summary>
        /// NEW: Batch label loader that accepts a list of label files (avoids temp directory copying)
        /// </summary>
        public async Task<(int labelsLoaded, HashSet<int> foundClassIds)> LoadYoloLabelsBatchAsync(
            IEnumerable<string> labelFiles,
            ImageManager imageManager,
            IProgress<(int current, int total, string message)> progress = null,
            CancellationToken cancellationToken = default,
            bool enableParallelProcessing = true)
        {
            var files = labelFiles?
                .Where(f => !string.IsNullOrWhiteSpace(f))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray() ?? Array.Empty<string>();

            if (files.Length == 0)
                return (0, new HashSet<int>());

            int totalLabelsAdded = 0;
            int processedFiles = 0;
            int totalFiles = files.Length;
            int batchSize = LabelLoadBatchSize > 0 ? LabelLoadBatchSize : 500;

            return await Task.Run(() =>
            {
                // Track all class IDs found in label files
                var foundClassIds = new ConcurrentDictionary<int, bool>();

                // Process files in batches
                for (int i = 0; i < files.Length; i += batchSize)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var batch = files.Skip(i).Take(batchSize).ToArray();
                    int batchLabelsAdded = 0;

                    if (enableParallelProcessing)
                    {
                        var options = new ParallelOptions { CancellationToken = cancellationToken };

                        Parallel.ForEach(batch, options, () => 0,
                            (labelFile, state, localCount) =>
                            {
                                localCount += LoadYoloLabelsOptimized(labelFile, imageManager, foundClassIds);
                                return localCount;
                            },
                            localCount => Interlocked.Add(ref batchLabelsAdded, localCount));
                    }
                    else
                    {
                        // Process sequentially
                        foreach (var labelFile in batch)
                        {
                            batchLabelsAdded += LoadYoloLabelsOptimized(labelFile, imageManager, foundClassIds);
                        }
                    }

                    totalLabelsAdded += batchLabelsAdded;
                    processedFiles += batch.Length;

                    // Report progress
                    progress?.Report((processedFiles, totalFiles,
                        $"Loading labels... {processedFiles}/{totalFiles} ({totalLabelsAdded} labels)"));
                }

                return (totalLabelsAdded, new HashSet<int>(foundClassIds.Keys));
            }, cancellationToken);
        }

        /// <summary>
        /// OPTIMIZED: Faster label loading that uses cached image dimensions
        /// This avoids reopening bitmap files which is extremely slow for large datasets
        /// </summary>
        private int LoadYoloLabelsOptimized(string labelFile, ImageManager imageManager, ConcurrentDictionary<int, bool> foundClassIds = null)
        {
            if (!File.Exists(labelFile))
                return 0;

            // Get image filename from label filename
            string labelFileName = Path.GetFileNameWithoutExtension(labelFile);

            // Try to find matching image with common extensions
            string[] possibleExtensions = { ".jpg", ".jpeg", ".png", ".JPG", ".JPEG", ".PNG" };
            ImageManager.ImageInfo imageInfo = null;
            string matchingImageFile = null;

            foreach (var ext in possibleExtensions)
            {
                string potentialImageFile = labelFileName + ext;
                if (imageManager.ImagePathMap.TryGetValue(potentialImageFile, out imageInfo))
                {
                    matchingImageFile = potentialImageFile;
                    break;
                }
            }

            if (imageInfo == null)
                return 0; // No matching image found

            int labelsAdded = 0;

            try
            {
                // Use cached dimensions from ImageManager - MUCH faster than opening bitmap
                int imgWidth = (int)imageInfo.OriginalDimensions.Width;
                int imgHeight = (int)imageInfo.OriginalDimensions.Height;

                // Initialize label list for this image if needed
                // Pre-allocate list with estimated capacity (most label files have < 50 labels)
                var newLabels = new List<LabelData>(50);

                // Use ReadLines instead of ReadAllLines for better memory efficiency
                foreach (var line in File.ReadLines(labelFile))
                {
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    var parts = line.AsSpan().Trim();

                    try
                    {
                        // Split on whitespace (handles spaces, tabs, multiple spaces, etc.)
                        var tokens = parts.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                        if (tokens.Length != 5)
                            continue;

                        // Fast path: try invariant culture first (most common)
                        if (!float.TryParse(tokens[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float xCenterNorm) ||
                            !float.TryParse(tokens[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float yCenterNorm) ||
                            !float.TryParse(tokens[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float widthNorm) ||
                            !float.TryParse(tokens[4], NumberStyles.Float, CultureInfo.InvariantCulture, out float heightNorm))
                        {
                            // Fallback to flexible parsing for comma decimal separators
                            xCenterNorm = ParseFloat(tokens[1]);
                            yCenterNorm = ParseFloat(tokens[2]);
                            widthNorm = ParseFloat(tokens[3]);
                            heightNorm = ParseFloat(tokens[4]);
                        }

                        // Parse ClassId from first token
                        int classId = 0;
                        int originalClassId = 0;
                        if (int.TryParse(tokens[0], out int parsedClassId))
                        {
                            classId = parsedClassId;
                            originalClassId = parsedClassId;
                        }
                        
                        // Track found class ID (before validation)
                        if (foundClassIds != null && originalClassId >= 0)
                        {
                            foundClassIds.TryAdd(originalClassId, true);
                        }
                        
                        // During import, keep original class ID even if it doesn't exist yet
                        // We'll create missing classes after import completes
                        // Only fix if we're not tracking found class IDs (legacy mode)
                        if (foundClassIds == null && !validClassIds.Contains(classId))
                        {
                            Debug.WriteLine($"LabelManager: Fixing orphaned ClassId {classId} -> {defaultClassId} during import");
                            classId = defaultClassId;
                        }

                        float xCenter = xCenterNorm * imgWidth;
                        float yCenter = yCenterNorm * imgHeight;
                        float width = widthNorm * imgWidth;
                        float height = heightNorm * imgHeight;

                        double x = xCenter - (width / 2);
                        double y = yCenter - (height / 2);

                        var existingCount = labelStorage.TryGetValue(matchingImageFile, out var existingLabels)
                            ? existingLabels.Count
                            : 0;
                        var label = new LabelData($"Imported Label {existingCount + newLabels.Count + 1}",
                            new Rect(x, y, width, height),
                            classId);
                        newLabels.Add(label);
                        labelsAdded++;
                    }
                    catch (FormatException)
                    {
                        // Skip lines that can't be parsed
                        continue;
                    }
                }

                // Add all labels at once (thread-safe)
                if (newLabels.Count > 0)
                {
                    labelStorage.AddOrUpdate(
                        matchingImageFile,
                        new List<LabelData>(newLabels),
                        (k, existing) =>
                        {
                            var merged = new List<LabelData>(existing.Count + newLabels.Count);
                            merged.AddRange(existing);
                            merged.AddRange(newLabels);
                            return merged;
                        });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading YOLO labels from {labelFile}: {ex.Message}");
                // Silent fail like original
            }

            return labelsAdded;
        }

        /// <summary>
        /// LEGACY: Original synchronous method for backward compatibility
        /// Use LoadYoloLabelsBatchAsync for better performance with large datasets
        /// </summary>
        public int LoadYoloLabels(string labelFile, string imagePath, ImageManager imageManager)
        {
            if (!File.Exists(labelFile)) return 0;

            string fileName = Path.GetFileName(imagePath);

            // Ensure the correct image path
            if (!imageManager.ImagePathMap.TryGetValue(Path.GetFileName(imagePath), out ImageManager.ImageInfo imageInfo))
            {
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

                    using StreamReader reader = new StreamReader(labelFile);
                    string? line;
                    var newLabels = new List<LabelData>();
                    while ((line = reader.ReadLine()) != null)
                    {
                        string[] parts = line.Trim().Split(' ');
                        if (parts.Length != 5) continue;

                        try
                        {
                            // Parse ClassId from first token
                            int classId = 0;
                            if (int.TryParse(parts[0], out int parsedClassId))
                            {
                                classId = parsedClassId;
                            }
                            
                            // Validate and fix ClassId if it doesn't exist in current project
                            if (!validClassIds.Contains(classId))
                            {
                                classId = defaultClassId;
                            }

                            // Use the flexible parsing method that supports both comma and period
                            float xCenter = ParseFloat(parts[1]) * imgWidth;
                            float yCenter = ParseFloat(parts[2]) * imgHeight;
                            float width = ParseFloat(parts[3]) * imgWidth;
                            float height = ParseFloat(parts[4]) * imgHeight;

                            double x = xCenter - (width / 2);
                            double y = yCenter - (height / 2);

                            var labelCount = newLabels.Count + 1;
                            var label = new LabelData($"Imported Label {labelCount}", new Rect(x, y, width, height), classId);
                            newLabels.Add(label);

                            labelsAdded++;
                        }
                        catch (FormatException)
                        {
                            // Skip lines that can't be parsed
                            continue;
                        }
                    }

                    if (newLabels.Count > 0)
                    {
                        labelStorage.AddOrUpdate(
                            fileName,
                            new List<LabelData>(newLabels),
                            (k, existing) =>
                            {
                                var merged = new List<LabelData>(existing.Count + newLabels.Count);
                                merged.AddRange(existing);
                                merged.AddRange(newLabels);
                                return merged;
                            });
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading YOLO labels (legacy) from {labelFile}: {ex.Message}");
            }

            return labelsAdded;
        }

        // Direct port of ExportLabelsToYolo
        public void ExportLabelsToYolo(string filePath, string imagePath, List<LabelData> labelsToExport)
        {
            using Bitmap image = new Bitmap(imagePath);
            int imageWidth = image.Width;
            int imageHeight = image.Height;

            ExportLabelsToYolo(filePath, new System.Windows.Size(imageWidth, imageHeight), labelsToExport);
        }

        /// <summary>
        /// Export labels without reopening the image file by using cached dimensions
        /// </summary>
        public void ExportLabelsToYolo(string filePath, System.Windows.Size imageSize, List<LabelData> labelsToExport)
        {
            int imageWidth = (int)imageSize.Width;
            int imageHeight = (int)imageSize.Height;

            if (imageWidth <= 0 || imageHeight <= 0)
            {
                Debug.WriteLine($"Invalid image dimensions for label export: {imageWidth}x{imageHeight}");
                return;
            }

            using StreamWriter writer = new(filePath);

            foreach (var label in labelsToExport)
            {
                float x_center = (float)((label.Rect.X + label.Rect.Width / 2f) / imageWidth);
                float y_center = (float)(label.Rect.Y + label.Rect.Height / 2f) / imageHeight;
                float width = (float)label.Rect.Width / (float)imageWidth;
                float height = (float)label.Rect.Height / (float)imageHeight;

                // Always export with period as decimal separator for maximum compatibility
                writer.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "{0} {1:F6} {2:F6} {3:F6} {4:F6}",
                    label.ClassId, x_center, y_center, width, height));
            }
        }

        /// <summary>
        /// NEW: Batch export labels for better performance
        /// </summary>
        public async Task ExportLabelsBatchAsync(
            string outputDirectory,
            ImageManager imageManager,
            IProgress<(int current, int total, string message)> progress = null,
            CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(outputDirectory))
                Directory.CreateDirectory(outputDirectory);

            var labelsToExport = labelStorage.Where(kvp => kvp.Value.Count > 0).ToArray();
            int totalFiles = labelsToExport.Length;
            int processedFiles = 0;

            await Task.Run(() =>
            {
                Parallel.ForEach(labelsToExport,
                    new ParallelOptions { CancellationToken = cancellationToken },
                    (kvp) =>
                    {
                        string fileName = kvp.Key;
                        var labels = kvp.Value;

                        if (!imageManager.ImagePathMap.TryGetValue(fileName, out var imageInfo))
                            return;

                        string labelFileName = Path.GetFileNameWithoutExtension(fileName) + ".txt";
                        string labelFilePath = Path.Combine(outputDirectory, labelFileName);

                        try
                        {
                            // Use cached dimensions instead of opening bitmap
                            ExportLabelsToYolo(labelFilePath, imageInfo.OriginalDimensions, labels);

                            Interlocked.Increment(ref processedFiles);
                            progress?.Report((processedFiles, totalFiles,
                                $"Exporting labels... {processedFiles}/{totalFiles}"));
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error exporting labels for {labelFilePath}: {ex.Message}");
                        }
                    });
            }, cancellationToken);
        }

        // Calculate IoU (Intersection over Union) of two rectangles
        private double ComputeIoU(Rect rect1, Rect rect2)
        {
            double x1 = Math.Max(rect1.Left, rect2.Left);
            double y1 = Math.Max(rect1.Top, rect2.Top);
            double x2 = Math.Min(rect1.Right, rect2.Right);
            double y2 = Math.Min(rect1.Bottom, rect2.Bottom);

            double intersectionWidth = Math.Max(0, x2 - x1);
            double intersectionHeight = Math.Max(0, y2 - y1);
            double intersectionArea = intersectionWidth * intersectionHeight;

            double area1 = rect1.Width * rect1.Height;
            double area2 = rect2.Width * rect2.Height;
            double unionArea = area1 + area2 - intersectionArea;

            return unionArea == 0 ? 0 : intersectionArea / unionArea;
        }

        // For AI labels - merge with existing labels
        public void AddAILabels(string fileName, List<(Rectangle box, int classId)> detectedBoxes)
        {
            var existingLabels = labelStorage.TryGetValue(fileName, out var labels)
                ? labels.Select(label => new LabelData(label)).ToList()
                : new List<LabelData>();
            float mergeIoUThreshold = Properties.Settings.Default.EnsembleIoUThreshold;

            foreach (var detection in detectedBoxes)
            {
                // Safety check to ensure positive dimensions
                if (detection.box.Width <= 0 || detection.box.Height <= 0)
                {
                    continue;
                }
                
                // Validate and fix ClassId
                int classId = detection.classId;
                if (!validClassIds.Contains(classId))
                {
                    classId = defaultClassId;
                }

                var newRect = new Rect(detection.box.X, detection.box.Y, detection.box.Width, detection.box.Height);

                // Check if it overlaps with existing labels
                bool merged = false;
                for (int i = 0; i < existingLabels.Count; i++)
                {
                    double iou = ComputeIoU(newRect, existingLabels[i].Rect);
                    if (iou >= mergeIoUThreshold)
                    {
                        // Merge labels: use the larger bounding box
                        var existingRect = existingLabels[i].Rect;
                        var mergedRect = new Rect(
                            Math.Min(newRect.Left, existingRect.Left),
                            Math.Min(newRect.Top, existingRect.Top),
                            Math.Max(newRect.Right, existingRect.Right) - Math.Min(newRect.Left, existingRect.Left),
                            Math.Max(newRect.Bottom, existingRect.Bottom) - Math.Min(newRect.Top, existingRect.Top)
                        );

                        // Update existing label
                        existingLabels[i] = new LabelData(existingLabels[i].Name, mergedRect, existingLabels[i].ClassId);
                        merged = true;
                        break;
                    }
                }

                // If not merged, add new label
                if (!merged)
                {
                    var labelCount = existingLabels.Count + 1;
                    var label = new LabelData($"AI Label {labelCount}", newRect, classId);
                    existingLabels.Add(label);
                }
            }

            // Update storage
            labelStorage.AddOrUpdate(
                fileName,
                new List<LabelData>(existingLabels),
                (k, existing) => new List<LabelData>(existingLabels));
        }
    }
}
