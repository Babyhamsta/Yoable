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
        private ConcurrentQueue<string> duplicateImageFiles = new();
        private string currentImagePath = "";

        // Add settings for performance
        public int BatchSize { get; set; } = 100; // Configurable batch size for processing

        // Simple properties matching original
        public string CurrentImagePath
        {
            get => currentImagePath;
            set => currentImagePath = value;
        }

        public ConcurrentDictionary<string, ImageInfo> ImagePathMap => imagePathMap;
        public ConcurrentDictionary<string, ImageStatus> ImageStatuses => imageStatuses;

        private static readonly string[] SupportedExtensions = { ".jpg", ".jpeg", ".png" };

        private static bool IsSupportedImage(string filePath)
        {
            var ext = Path.GetExtension(filePath);
            return !string.IsNullOrEmpty(ext) &&
                   SupportedExtensions.Any(e => ext.Equals(e, StringComparison.OrdinalIgnoreCase));
        }

        public List<string> ConsumeDuplicateImageFiles()
        {
            var duplicates = new List<string>();
            while (duplicateImageFiles.TryDequeue(out var item))
            {
                duplicates.Add(item);
            }
            return duplicates;
        }

        // Original synchronous method for backward compatibility
        public string[] LoadImagesFromDirectory(string directoryPath)
        {
            if (!Directory.Exists(directoryPath)) return new string[0];

            string[] files = Directory.GetFiles(directoryPath, "*.*")
                                      .Where(IsSupportedImage)
                                      .ToArray();

            // Clear collections just like original
            imagePathMap.Clear();
            imageStatuses.Clear();
            while (duplicateImageFiles.TryDequeue(out _)) { }

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

            // Get all image files
            var files = Directory.GetFiles(directoryPath, "*.*")
                                .Where(IsSupportedImage)
                                .ToArray();

            await LoadImagesFromPathsAsync(files, progress, cancellationToken, enableParallelProcessing);

            return files;
        }

        // New async method for loading images from a list of paths
        public async Task LoadImagesFromPathsAsync(
            IEnumerable<string> files,
            IProgress<(int current, int total, string message)> progress = null,
            CancellationToken cancellationToken = default,
            bool enableParallelProcessing = true)
        {
            var fileArray = files?.Where(f => !string.IsNullOrWhiteSpace(f)).ToArray() ?? Array.Empty<string>();

            // Clear collections
            imagePathMap.Clear();
            imageStatuses.Clear();
            while (duplicateImageFiles.TryDequeue(out _)) { }

            int totalFiles = fileArray.Length;
            int processedFiles = 0;
            int batchSize = BatchSize > 0 ? BatchSize : 100;

            // Process files in batches
            for (int i = 0; i < fileArray.Length; i += batchSize)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var batch = fileArray.Skip(i).Take(batchSize).ToArray();

                if (enableParallelProcessing)
                {
                    var options = new ParallelOptions { CancellationToken = cancellationToken };
                    await Parallel.ForEachAsync(batch, options, (file, ct) =>
                    {
                        AddImageThreadSafe(file);
                        return ValueTask.CompletedTask;
                    });
                }
                else
                {
                    // Offload sequential processing to avoid UI blocking
                    await Task.Run(() =>
                    {
                        foreach (var file in batch)
                        {
                            AddImageThreadSafe(file);
                        }
                    }, cancellationToken);
                }

                processedFiles += batch.Length;

                // Report progress
                progress?.Report((processedFiles, totalFiles, $"Loading images... {processedFiles}/{totalFiles}"));
            }
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
                    if (!imagePathMap.TryAdd(fileName, new ImageInfo(filePath, dimensions)))
                    {
                        if (imagePathMap.TryGetValue(fileName, out var existing) &&
                            !string.Equals(existing.Path, filePath, StringComparison.OrdinalIgnoreCase))
                        {
                            duplicateImageFiles.Enqueue($"{fileName} -> {filePath}");
                        }
                        return false;
                    }
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
                    if (!imagePathMap.TryAdd(fileName, new ImageInfo(filePath, dimensions)))
                    {
                        if (imagePathMap.TryGetValue(fileName, out var existing) &&
                            !string.Equals(existing.Path, filePath, StringComparison.OrdinalIgnoreCase))
                        {
                            duplicateImageFiles.Enqueue($"{fileName} -> {filePath}");
                        }
                        return false;
                    }
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
            while (duplicateImageFiles.TryDequeue(out _)) { }
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
