using System.Collections.Concurrent;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace YoableWPF.Managers
{
    public enum RoiCropScope
    {
        AllImages,
        CurrentImage
    }

    public sealed record RoiCropSourceItem(
        string FileName,
        string SourcePath,
        List<LabelData> Labels,
        List<SuggestedLabel> Suggestions);

    public sealed record RoiCropProcessedItem(
        string FileName,
        string OutputPath,
        Size OutputSize,
        List<LabelData> Labels,
        List<SuggestedLabel> Suggestions,
        int RemovedLabelCount,
        int ClippedLabelCount);

    public sealed class RoiCropBatchResult
    {
        public List<RoiCropProcessedItem> ProcessedItems { get; } = new();
        public int SkippedImageCount { get; set; }
        public int RemovedLabelCount => ProcessedItems.Sum(item => item.RemovedLabelCount);
        public int ClippedLabelCount => ProcessedItems.Sum(item => item.ClippedLabelCount);
    }

public sealed class RoiCropManager
{
    private const int MaxParallelWorkers = 8;

    public async Task<RoiCropBatchResult> CropBatchAsync(
        IReadOnlyCollection<RoiCropSourceItem> images,
        string outputDirectory,
        int cropWidth,
            int cropHeight,
        IProgress<(int current, int total, string fileName)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(outputDirectory);

        var processedItems = new ConcurrentBag<RoiCropProcessedItem>();
        int skippedImageCount = 0;
        int completedImageCount = 0;
        int workerCount = Math.Clamp(Environment.ProcessorCount / 2, 2, MaxParallelWorkers);
        var parallelOptions = new ParallelOptions
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = workerCount
        };

        await Parallel.ForEachAsync(images, parallelOptions, (image, token) =>
        {
            token.ThrowIfCancellationRequested();

            var processed = CropImage(image, outputDirectory, cropWidth, cropHeight);
            if (processed == null)
            {
                Interlocked.Increment(ref skippedImageCount);
            }
            else
            {
                processedItems.Add(processed);
            }

            int completed = Interlocked.Increment(ref completedImageCount);
            progress?.Report((completed, images.Count, image.FileName));

            return ValueTask.CompletedTask;
        });

        var result = new RoiCropBatchResult
        {
            SkippedImageCount = skippedImageCount
        };
        result.ProcessedItems.AddRange(
            processedItems.OrderBy(item => item.FileName, StringComparer.OrdinalIgnoreCase));

        return result;
    }

        private static RoiCropProcessedItem? CropImage(
            RoiCropSourceItem image,
            string outputDirectory,
            int cropWidth,
            int cropHeight)
        {
            try
            {
                BitmapFrame frame;
                using (var input = new FileStream(
                           image.SourcePath,
                           FileMode.Open,
                           FileAccess.Read,
                           FileShare.Read,
                           1024 * 128,
                           FileOptions.SequentialScan))
                {
                    var decoder = BitmapDecoder.Create(
                        input,
                        BitmapCreateOptions.PreservePixelFormat,
                        BitmapCacheOption.OnLoad);
                    frame = decoder.Frames[0];
                }

                if (frame.PixelWidth < cropWidth || frame.PixelHeight < cropHeight)
                    return null;

                int cropX = (frame.PixelWidth - cropWidth) / 2;
                int cropY = (frame.PixelHeight - cropHeight) / 2;
                string outputPath = Path.Combine(outputDirectory, image.FileName);
                bool alreadyTargetSize = frame.PixelWidth == cropWidth &&
                                         frame.PixelHeight == cropHeight;
                bool alreadyInOutputDirectory = string.Equals(
                    Path.GetFullPath(image.SourcePath),
                    Path.GetFullPath(outputPath),
                    StringComparison.OrdinalIgnoreCase);

                if (!alreadyTargetSize || !alreadyInOutputDirectory)
                {
                    string tempPath = outputPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    try
                    {
                        if (alreadyTargetSize)
                        {
                            File.Copy(image.SourcePath, tempPath);
                        }
                        else
                        {
                            var croppedBitmap = new CroppedBitmap(
                                frame,
                                new Int32Rect(cropX, cropY, cropWidth, cropHeight));
                            croppedBitmap.Freeze();

                            using var output = new FileStream(
                                tempPath,
                                FileMode.CreateNew,
                                FileAccess.Write,
                                FileShare.None);
                            BitmapEncoder encoder = CreateEncoder(Path.GetExtension(image.FileName));
                            encoder.Frames.Add(BitmapFrame.Create(croppedBitmap));
                            encoder.Save(output);
                        }

                        File.Move(tempPath, outputPath, overwrite: true);
                    }
                    finally
                    {
                        if (File.Exists(tempPath))
                            File.Delete(tempPath);
                    }
                }

                var roi = new Rect(cropX, cropY, cropWidth, cropHeight);
                var labels = TransformLabels(image.Labels, roi, out int removed, out int clipped);
                var suggestions = TransformSuggestions(image.Suggestions, roi);

                return new RoiCropProcessedItem(
                    image.FileName,
                    outputPath,
                    new Size(cropWidth, cropHeight),
                    labels,
                    suggestions,
                    removed,
                    clipped);
            }
            catch (Exception ex) when (
                ex is IOException or
                UnauthorizedAccessException or
                NotSupportedException or
                FileFormatException)
            {
                System.Diagnostics.Debug.WriteLine($"ROI crop skipped {image.SourcePath}: {ex.Message}");
                return null;
            }
        }

        private static BitmapEncoder CreateEncoder(string extension)
        {
            if (extension.Equals(".png", StringComparison.OrdinalIgnoreCase))
                return new PngBitmapEncoder();

            return new JpegBitmapEncoder { QualityLevel = 95 };
        }

        private static List<LabelData> TransformLabels(
            IEnumerable<LabelData> labels,
            Rect roi,
            out int removedCount,
            out int clippedCount)
        {
            var transformed = new List<LabelData>();
            removedCount = 0;
            clippedCount = 0;

            foreach (var label in labels)
            {
                if (!TryTransformRect(label.Rect, roi, out var transformedRect, out bool clipped))
                {
                    removedCount++;
                    continue;
                }

                if (clipped)
                    clippedCount++;

                transformed.Add(new LabelData(label.Name, transformedRect, label.ClassId));
            }

            return transformed;
        }

        private static List<SuggestedLabel> TransformSuggestions(
            IEnumerable<SuggestedLabel> suggestions,
            Rect roi)
        {
            var transformed = new List<SuggestedLabel>();
            foreach (var suggestion in suggestions)
            {
                if (!TryTransformRect(suggestion.ToRect(), roi, out var rect, out _))
                    continue;

                transformed.Add(new SuggestedLabel
                {
                    Id = suggestion.Id,
                    X = rect.X,
                    Y = rect.Y,
                    Width = rect.Width,
                    Height = rect.Height,
                    ClassId = suggestion.ClassId,
                    Score = suggestion.Score,
                    Source = suggestion.Source,
                    SourceImage = suggestion.SourceImage,
                    SourceLabelId = suggestion.SourceLabelId
                });
            }
            return transformed;
        }

        private static bool TryTransformRect(
            Rect source,
            Rect roi,
            out Rect transformed,
            out bool clipped)
        {
            double left = Math.Max(source.Left, roi.Left);
            double top = Math.Max(source.Top, roi.Top);
            double right = Math.Min(source.Right, roi.Right);
            double bottom = Math.Min(source.Bottom, roi.Bottom);

            if (right <= left || bottom <= top)
            {
                transformed = Rect.Empty;
                clipped = false;
                return false;
            }

            clipped = left > source.Left ||
                      top > source.Top ||
                      right < source.Right ||
                      bottom < source.Bottom;
            transformed = new Rect(
                left - roi.Left,
                top - roi.Top,
                right - left,
                bottom - top);
            return true;
        }
    }
}
