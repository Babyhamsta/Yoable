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

        // Direct port of label management methods
        public List<LabelData> GetLabels(string fileName)
        {
            return labelStorage.ContainsKey(fileName) ?
                new List<LabelData>(labelStorage[fileName]) :
                new List<LabelData>();
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

        // Helper method to parse floats with either comma or period as decimal separator
        private float ParseFloat(string value)
        {
            // First try with invariant culture (period as decimal separator)
            if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float result))
            {
                return result;
            }

            // If that fails, try with a culture that uses comma as decimal separator
            var cultureWithComma = new CultureInfo("de-DE"); // German culture uses comma
            if (float.TryParse(value, NumberStyles.Float, cultureWithComma, out result))
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
        public async Task<int> LoadYoloLabelsBatchAsync(
            string labelsDirectory,
            ImageManager imageManager,
            IProgress<(int current, int total, string message)> progress = null,
            CancellationToken cancellationToken = default,
            bool enableParallelProcessing = true)
        {
            if (!Directory.Exists(labelsDirectory))
                return 0;

            // Get all .txt label files
            var labelFiles = Directory.GetFiles(labelsDirectory, "*.txt").ToArray();

            if (labelFiles.Length == 0)
                return 0;

            int totalLabelsAdded = 0;
            int processedFiles = 0;
            int totalFiles = labelFiles.Length;
            int batchSize = LabelLoadBatchSize;

            return await Task.Run(() =>
            {
                // Process files in batches
                for (int i = 0; i < labelFiles.Length; i += batchSize)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var batch = labelFiles.Skip(i).Take(batchSize).ToArray();
                    int batchLabelsAdded = 0;

                    if (enableParallelProcessing)
                    {
                        // Process batch in parallel
                        var results = batch.AsParallel()
                            .WithCancellation(cancellationToken)
                            .Select(labelFile =>
                            {
                                return LoadYoloLabelsOptimized(labelFile, imageManager);
                            })
                            .ToArray();

                        batchLabelsAdded = results.Sum();
                    }
                    else
                    {
                        // Process sequentially
                        foreach (var labelFile in batch)
                        {
                            batchLabelsAdded += LoadYoloLabelsOptimized(labelFile, imageManager);
                        }
                    }

                    totalLabelsAdded += batchLabelsAdded;
                    processedFiles += batch.Length;

                    // Report progress
                    progress?.Report((processedFiles, totalFiles,
                        $"Loading labels... {processedFiles}/{totalFiles} ({totalLabelsAdded} labels)"));
                }

                return totalLabelsAdded;
            }, cancellationToken);
        }

        /// <summary>
        /// OPTIMIZED: Faster label loading that uses cached image dimensions
        /// This avoids reopening bitmap files which is extremely slow for large datasets
        /// </summary>
        private int LoadYoloLabelsOptimized(string labelFile, ImageManager imageManager)
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
                if (!labelStorage.ContainsKey(matchingImageFile))
                    labelStorage.TryAdd(matchingImageFile, new List<LabelData>());

                // Pre-allocate list with estimated capacity (most label files have < 50 labels)
                var newLabels = new List<LabelData>(50);

                // Use ReadLines instead of ReadAllLines for better memory efficiency
                foreach (var line in File.ReadLines(labelFile))
                {
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    var parts = line.AsSpan().Trim();

                    // Fast path: count spaces to validate format
                    int spaceCount = 0;
                    for (int i = 0; i < parts.Length; i++)
                    {
                        if (parts[i] == ' ')
                            spaceCount++;
                    }

                    if (spaceCount != 4)
                        continue;

                    try
                    {
                        // Split on spaces manually for better performance
                        var tokens = parts.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
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

                        float xCenter = xCenterNorm * imgWidth;
                        float yCenter = yCenterNorm * imgHeight;
                        float width = widthNorm * imgWidth;
                        float height = heightNorm * imgHeight;

                        double x = xCenter - (width / 2);
                        double y = yCenter - (height / 2);

                        var existingCount = labelStorage[matchingImageFile].Count;
                        var label = new LabelData($"Imported Label {existingCount + newLabels.Count + 1}",
                            new Rect(x, y, width, height));
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
                        newLabels,
                        (k, existing) =>
                        {
                            existing.AddRange(newLabels);
                            return existing;
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

                    if (!labelStorage.ContainsKey(fileName))
                        labelStorage.TryAdd(fileName, new List<LabelData>());

                    using StreamReader reader = new StreamReader(labelFile);
                    string? line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        string[] parts = line.Trim().Split(' ');
                        if (parts.Length != 5) continue;

                        try
                        {
                            // Use the flexible parsing method that supports both comma and period
                            float xCenter = ParseFloat(parts[1]) * imgWidth;
                            float yCenter = ParseFloat(parts[2]) * imgHeight;
                            float width = ParseFloat(parts[3]) * imgWidth;
                            float height = ParseFloat(parts[4]) * imgHeight;

                            double x = xCenter - (width / 2);
                            double y = yCenter - (height / 2);

                            var labelCount = labelStorage[fileName].Count + 1;
                            var label = new LabelData($"Imported Label {labelCount}", new Rect(x, y, width, height));
                            labelStorage[fileName].Add(label);

                            labelsAdded++;
                        }
                        catch (FormatException)
                        {
                            // Skip lines that can't be parsed
                            Debug.WriteLine($"Warning: Could not parse line in {labelFile}: {line}");
                            continue;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading YOLO labels from {labelFile}: {ex.Message}");
                // Silent fail like original
            }

            return labelsAdded;
        }

        // Direct port of ExportLabelsToYolo
        public void ExportLabelsToYolo(string filePath, string imagePath, List<LabelData> labelsToExport)
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

                // Always export with period as decimal separator for maximum compatibility
                writer.WriteLine($"0 {x_center:F6} {y_center:F6} {width:F6} {height:F6}");
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
                            int imageWidth = (int)imageInfo.OriginalDimensions.Width;
                            int imageHeight = (int)imageInfo.OriginalDimensions.Height;

                            using StreamWriter writer = new(labelFilePath) { AutoFlush = true };

                            foreach (var label in labels)
                            {
                                float x_center = (float)((label.Rect.X + label.Rect.Width / 2f) / imageWidth);
                                float y_center = (float)(label.Rect.Y + label.Rect.Height / 2f) / imageHeight;
                                float width = (float)label.Rect.Width / (float)imageWidth;
                                float height = (float)label.Rect.Height / (float)imageHeight;

                                writer.WriteLine($"0 {x_center:F6} {y_center:F6} {width:F6} {height:F6}");
                            }

                            Interlocked.Increment(ref processedFiles);
                            progress?.Report((processedFiles, totalFiles,
                                $"Exporting labels... {processedFiles}/{totalFiles}"));
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error exporting labels for {fileName}: {ex.Message}");
                        }
                    });
            }, cancellationToken);
        }

        // For AI labels
        public void AddAILabels(string fileName, List<Rectangle> detectedBoxes)
        {
            if (!labelStorage.ContainsKey(fileName))
                labelStorage.TryAdd(fileName, new List<LabelData>());

            foreach (var box in detectedBoxes)
            {
                // Safety check to ensure positive dimensions
                if (box.Width <= 0 || box.Height <= 0)
                {
                    Debug.WriteLine($"Warning: Skipping invalid box with dimensions {box.Width}x{box.Height}");
                    continue;
                }

                var labelCount = labelStorage[fileName].Count + 1;
                var label = new LabelData($"AI Label {labelCount}", new Rect(box.X, box.Y, box.Width, box.Height));

                labelStorage.AddOrUpdate(
                    fileName,
                    new List<LabelData> { label },
                    (k, existing) =>
                    {
                        existing.Add(label);
                        return existing;
                    });
            }
        }
    }
}