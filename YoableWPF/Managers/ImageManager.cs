using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;

namespace YoableWPF.Managers
{
    public class ImageManager
    {
        // Use thread-safe collections for parallel processing
        private ConcurrentDictionary<string, ImageInfo> imagePathMap = new();
        private ConcurrentDictionary<string, ImageStatus> imageStatuses = new();
        private string currentImagePath = "";

        // Add settings for performance
        public int BatchSize { get; set; } = 100; // Configurable batch size for processing

        // Simple properties matching original
        public string CurrentImagePath
        {
            get => currentImagePath;
            set => currentImagePath = value;
        }

        public Dictionary<string, ImageInfo> ImagePathMap => imagePathMap.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        public Dictionary<string, ImageStatus> ImageStatuses => imageStatuses.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        // Original synchronous method for backward compatibility
        public string[] LoadImagesFromDirectory(string directoryPath)
        {
            if (!Directory.Exists(directoryPath)) return new string[0];

            string[] files = Directory.GetFiles(directoryPath, "*.*")
                                      .Where(f => f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                                                 f.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                                      .ToArray();

            // Clear collections just like original
            imagePathMap.Clear();
            imageStatuses.Clear();

            foreach (string file in files)
            {
                AddImage(file);
            }

            return files;
        }

        // New async method with progress reporting
        public async Task<string[]> LoadImagesFromDirectoryAsync(
            string directoryPath,
            IProgress<(int current, int total, string message)> progress = null,
            CancellationToken cancellationToken = default,
            bool enableParallelProcessing = true)
        {
            if (!Directory.Exists(directoryPath)) return new string[0];

            return await Task.Run(async () =>
            {
                // Get all image files
                var files = Directory.GetFiles(directoryPath, "*.*")
                                    .Where(f => f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                                               f.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                                    .ToArray();

                // Clear collections
                imagePathMap.Clear();
                imageStatuses.Clear();

                int totalFiles = files.Length;
                int processedFiles = 0;
                int batchSize = BatchSize; // Use configurable batch size

                // Process files in batches
                for (int i = 0; i < files.Length; i += batchSize)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var batch = files.Skip(i).Take(batchSize).ToArray();

                    // Check if parallel processing is enabled
                    if (enableParallelProcessing)
                    {
                        // Process batch in parallel for better performance
                        var tasks = batch.Select(async file =>
                        {
                            await Task.Run(() => AddImageThreadSafe(file), cancellationToken);
                            return file;
                        }).ToArray();

                        await Task.WhenAll(tasks);
                    }
                    else
                    {
                        // Process sequentially
                        foreach (var file in batch)
                        {
                            AddImageThreadSafe(file);
                        }
                    }

                    processedFiles += batch.Length;

                    // Report progress
                    progress?.Report((processedFiles, totalFiles, $"Loading images... {processedFiles}/{totalFiles}"));
                }

                return files;
            }, cancellationToken);
        }

        // Direct port of AddImage
        public bool AddImage(string filePath)
        {
            if (!File.Exists(filePath)) return false;

            string fileName = Path.GetFileName(filePath);

            try
            {
                using (var imageStream = File.OpenRead(filePath))
                {
                    var decoder = BitmapDecoder.Create(
                        imageStream,
                        BitmapCreateOptions.None,
                        BitmapCacheOption.None);

                    var dimensions = new Size(decoder.Frames[0].PixelWidth, decoder.Frames[0].PixelHeight);
                    imagePathMap.TryAdd(fileName, new ImageInfo(filePath, dimensions));
                    imageStatuses.TryAdd(fileName, ImageStatus.NoLabel);
                }

                return true;
            }
            catch
            {
                // If we fail to read the image, don't add it
                return false;
            }
        }

        // Thread-safe version for parallel processing
        private bool AddImageThreadSafe(string filePath)
        {
            if (!File.Exists(filePath)) return false;

            string fileName = Path.GetFileName(filePath);

            try
            {
                using (var imageStream = File.OpenRead(filePath))
                {
                    var decoder = BitmapDecoder.Create(
                        imageStream,
                        BitmapCreateOptions.DelayCreation,
                        BitmapCacheOption.None);

                    var dimensions = new Size(decoder.Frames[0].PixelWidth, decoder.Frames[0].PixelHeight);
                    imagePathMap.TryAdd(fileName, new ImageInfo(filePath, dimensions));
                    imageStatuses.TryAdd(fileName, ImageStatus.NoLabel);
                }

                return true;
            }
            catch
            {
                // If we fail to read the image, don't add it
                return false;
            }
        }

        // Direct port of UpdateImageStatus
        public void UpdateImageStatus(string fileName)
        {
            if (!imagePathMap.ContainsKey(fileName)) return;
            // Status update logic will be handled by MainWindow as it needs label info
        }

        // Thread-safe method to update image status
        public void UpdateImageStatusValue(string fileName, ImageStatus status)
        {
            imageStatuses.AddOrUpdate(fileName, status, (k, v) => status);
        }

        // Thread-safe method to get image status
        public ImageStatus GetImageStatus(string fileName)
        {
            return imageStatuses.TryGetValue(fileName, out ImageStatus status) ? status : ImageStatus.NoLabel;
        }

        public void ClearAll()
        {
            imagePathMap.Clear();
            imageStatuses.Clear();
            currentImagePath = "";
        }

        public List<string> GetAllImagePaths()
        {
            return imagePathMap.Values.Select(img => img.Path).ToList();
        }

        // Keep the simple ImageInfo class here since it was private in original
        public class ImageInfo
        {
            public string Path { get; set; }
            public Size OriginalDimensions { get; set; }

            public ImageInfo(string path, Size dimensions)
            {
                Path = path;
                OriginalDimensions = dimensions;
            }
        }
    }
}