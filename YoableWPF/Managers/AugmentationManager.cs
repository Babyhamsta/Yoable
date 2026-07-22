using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenCvSharp;

namespace YoableWPF.Managers
{
    public enum AugmentationScope
    {
        AllImages,
        CurrentImage
    }

    /// <summary>
    /// Options for an offline data-augmentation run.
    /// </summary>
    public sealed class AugmentationOptions
    {
        public string OutputDirectory { get; set; }
        public AugmentationScope Scope { get; set; } = AugmentationScope.AllImages;

        // Number of augmented variants generated per source image.
        public int VariantsPerImage { get; set; } = 3;
        public int Seed { get; set; } = 0;

        // Also copy the untouched source image + labels into the output set.
        public bool IncludeOriginals { get; set; } = true;
        // Only process images that have labels (background/unlabeled images are skipped).
        public bool LabeledOnly { get; set; } = true;

        public bool EnableFlip { get; set; } = true;
        public bool EnableBrightnessContrast { get; set; } = true;
        public bool EnableHsv { get; set; } = true;
        public bool EnableNoise { get; set; } = false;
        public bool EnableBlur { get; set; } = false;

        public bool AnyAugmentationEnabled =>
            EnableFlip || EnableBrightnessContrast || EnableHsv || EnableNoise || EnableBlur;
    }

    public sealed class AugmentationResult
    {
        public int SourceImages { get; set; }
        public int GeneratedImages { get; set; }
        public int SkippedImages { get; set; }
        public string OutputDirectory { get; set; }
    }

    /// <summary>
    /// Generates augmented image/label copies for YOLO training. Pixel-only augmentations
    /// (brightness, HSV, noise, blur) leave boxes unchanged; horizontal flip remaps box
    /// coordinates. Output goes to images/ and labels/ subfolders of the chosen directory.
    /// Label serialization reuses <see cref="LabelManager.ExportLabelsToYolo"/>.
    /// </summary>
    public sealed class AugmentationManager
    {
        private const int MaxParallelWorkers = 4;

        public async Task<AugmentationResult> AugmentAsync(
            AugmentationOptions options,
            ImageManager imageManager,
            LabelManager labelManager,
            string currentFileName,
            IProgress<(int current, int total, string message)> progress = null,
            CancellationToken cancellationToken = default)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (!options.AnyAugmentationEnabled)
                throw new ArgumentException("At least one augmentation must be enabled.", nameof(options));

            string outputDirectory = ResolveOutputDirectory(
                options.OutputDirectory, imageManager, currentFileName);
            string imagesDir = Path.Combine(outputDirectory, "images");
            string labelsDir = Path.Combine(outputDirectory, "labels");
            Directory.CreateDirectory(imagesDir);
            Directory.CreateDirectory(labelsDir);

            // Build the work list.
            var sources = new List<(string fileName, ImageManager.ImageInfo info, List<LabelData> labels)>();
            foreach (var kvp in imageManager.ImagePathMap)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string fileName = kvp.Key;
                var info = kvp.Value;
                if (info == null) continue;

                if (options.Scope == AugmentationScope.CurrentImage &&
                    !string.Equals(fileName, currentFileName, StringComparison.OrdinalIgnoreCase))
                    continue;

                labelManager.LabelStorage.TryGetValue(fileName, out var labels);
                bool hasLabels = labels != null && labels.Count > 0;
                if (options.LabeledOnly && !hasLabels)
                    continue;

                sources.Add((fileName, info, labels ?? new List<LabelData>()));
            }

            var result = new AugmentationResult { OutputDirectory = outputDirectory };
            result.SourceImages = sources.Count;
            if (sources.Count == 0)
                return result;

            int total = sources.Count;
            int processed = 0;
            int generated = 0;
            int skipped = 0;

            await Task.Run(() =>
            {
                Parallel.ForEach(sources,
                    new ParallelOptions
                    {
                        CancellationToken = cancellationToken,
                        MaxDegreeOfParallelism = MaxParallelWorkers
                    },
                    (item) =>
                    {
                        try
                        {
                            int made = ProcessImage(item.fileName, item.info, item.labels, options,
                                imagesDir, labelsDir, labelManager);
                            Interlocked.Add(ref generated, made);
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            Interlocked.Increment(ref skipped);
                            Debug.WriteLine($"Augmentation error for {item.fileName}: {ex.Message}");
                        }
                        finally
                        {
                            int done = Interlocked.Increment(ref processed);
                            progress?.Report((done, total, $"Augmenting... {done}/{total}"));
                        }
                    });
            }, cancellationToken);

            result.GeneratedImages = generated;
            result.SkippedImages = skipped;
            return result;
        }

        private static string ResolveOutputDirectory(
            string configuredDirectory,
            ImageManager imageManager,
            string currentFileName)
        {
            if (!string.IsNullOrWhiteSpace(configuredDirectory))
                return configuredDirectory;

            ImageManager.ImageInfo sourceInfo = null;
            if (!string.IsNullOrWhiteSpace(currentFileName))
                imageManager.ImagePathMap.TryGetValue(currentFileName, out sourceInfo);

            sourceInfo ??= imageManager.ImagePathMap.Values.FirstOrDefault();
            string sourceDirectory = sourceInfo == null
                ? null
                : Path.GetDirectoryName(sourceInfo.Path);

            if (string.IsNullOrWhiteSpace(sourceDirectory))
                throw new ArgumentException(
                    "Output directory is required when no image source directory is available.",
                    nameof(configuredDirectory));

            return sourceDirectory;
        }

        private int ProcessImage(
            string fileName,
            ImageManager.ImageInfo info,
            List<LabelData> labels,
            AugmentationOptions options,
            string imagesDir,
            string labelsDir,
            LabelManager labelManager)
        {
            if (string.IsNullOrEmpty(info.Path) || !File.Exists(info.Path))
                return 0;

            byte[] bytes = File.ReadAllBytes(info.Path);
            using var source = Cv2.ImDecode(bytes, ImreadModes.Color);
            if (source.Empty())
                return 0;

            string ext = Path.GetExtension(fileName);
            if (string.IsNullOrEmpty(ext)) ext = ".jpg";
            string stem = Path.GetFileNameWithoutExtension(fileName);
            var imageSize = new System.Windows.Size(source.Width, source.Height);

            int made = 0;

            // Optionally keep the untouched original in the augmented set.
            if (options.IncludeOriginals)
            {
                WriteImage(source, Path.Combine(imagesDir, fileName), ext);
                labelManager.ExportLabelsToYolo(Path.Combine(labelsDir, stem + ".txt"), imageSize, labels);
                made++;
            }

            // Reproducible per-image RNG so the split/params don't depend on thread scheduling.
            var rng = new Random(options.Seed ^ StableHash(fileName));

            for (int v = 1; v <= options.VariantsPerImage; v++)
            {
                using var variant = source.Clone();
                bool flipped = false;

                if (options.EnableFlip && rng.NextDouble() < 0.5)
                {
                    Cv2.Flip(variant, variant, FlipMode.Y);
                    flipped = true;
                }

                if (options.EnableBrightnessContrast)
                {
                    double alpha = 0.8 + rng.NextDouble() * 0.4; // contrast 0.8..1.2
                    double beta = (rng.NextDouble() * 60) - 30;   // brightness -30..+30
                    variant.ConvertTo(variant, MatType.CV_8UC3, alpha, beta);
                }

                if (options.EnableHsv)
                    ApplyHsvJitter(variant, rng);

                if (options.EnableBlur && rng.NextDouble() < 0.5)
                {
                    int k = rng.Next(0, 2) == 0 ? 3 : 5;
                    Cv2.GaussianBlur(variant, variant, new OpenCvSharp.Size(k, k), 0);
                }

                if (options.EnableNoise && rng.NextDouble() < 0.5)
                    ApplyGaussianNoise(variant, rng);

                string variantName = $"{stem}_aug{v}{ext}";
                WriteImage(variant, Path.Combine(imagesDir, variantName), ext);

                var variantLabels = flipped
                    ? FlipLabels(labels, source.Width)
                    : labels;
                labelManager.ExportLabelsToYolo(
                    Path.Combine(labelsDir, $"{stem}_aug{v}.txt"), imageSize, variantLabels);

                made++;
            }

            return made;
        }

        private static void ApplyHsvJitter(Mat bgr, Random rng)
        {
            using var hsv = new Mat();
            Cv2.CvtColor(bgr, hsv, ColorConversionCodes.BGR2HSV);
            Mat[] channels = Cv2.Split(hsv);
            try
            {
                double hueShift = (rng.NextDouble() * 20) - 10;   // +/-10 (OpenCV hue is 0..180)
                double satScale = 0.7 + rng.NextDouble() * 0.6;    // 0.7..1.3
                Cv2.Add(channels[0], new Scalar(hueShift), channels[0]);
                channels[1].ConvertTo(channels[1], MatType.CV_8UC1, satScale, 0);
                Cv2.Merge(channels, hsv);
                Cv2.CvtColor(hsv, bgr, ColorConversionCodes.HSV2BGR);
            }
            finally
            {
                foreach (var c in channels)
                    c.Dispose();
            }
        }

        private static void ApplyGaussianNoise(Mat bgr, Random rng)
        {
            using var noise = new Mat(bgr.Size(), bgr.Type());
            double sigma = 5 + rng.NextDouble() * 15; // 5..20
            Cv2.Randn(noise, new Scalar(0, 0, 0), new Scalar(sigma, sigma, sigma));
            Cv2.Add(bgr, noise, bgr);
        }

        private static List<LabelData> FlipLabels(List<LabelData> labels, double imageWidth)
        {
            var flipped = new List<LabelData>(labels.Count);
            foreach (var label in labels)
            {
                var r = label.Rect;
                // Mirror both horizontal edges so the label follows the flipped object.
                double newX = imageWidth - (r.X + r.Width);
                flipped.Add(new LabelData(
                    label.Name,
                    new System.Windows.Rect(newX, r.Y, r.Width, r.Height),
                    label.ClassId));
            }
            return flipped;
        }

        private static void WriteImage(Mat mat, string path, string ext)
        {
            if (Cv2.ImEncode(ext, mat, out byte[] buffer))
                File.WriteAllBytes(path, buffer);
        }

        private static int StableHash(string s)
        {
            unchecked
            {
                int hash = 17;
                foreach (char c in s)
                    hash = hash * 31 + c;
                return hash;
            }
        }
    }
}
