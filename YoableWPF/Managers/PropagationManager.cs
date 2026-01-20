using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;

namespace YoableWPF.Managers
{
    public enum ImageSimilarityMode
    {
        Hash = 0,
        Histogram = 1
    }

    public class PropagationSummary
    {
        public int SuggestionsAdded { get; set; }
        public int LabelsAdded { get; set; }
        public int ImagesAffected { get; set; }
    }

    public class PropagationManager
    {
        public const string PhaseImageSimilarity = "image";
        public const string PhaseObjectRanking = "object-ranking";
        public const string PhaseObjectMatching = "object-matching";
        public const string PhaseTracking = "tracking";

        private const int ProgressInterval = 250;

        private readonly LabelManager labelManager;
        private readonly ImageManager imageManager;
        private readonly Dictionary<string, ulong> hashCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, float[]> histogramCache = new(StringComparer.OrdinalIgnoreCase);
        private string cacheFilePath;

        public PropagationManager(LabelManager labelManager, ImageManager imageManager)
        {
            this.labelManager = labelManager;
            this.imageManager = imageManager;
        }

        public void SetProjectFolder(string projectFolder)
        {
            if (string.IsNullOrWhiteSpace(projectFolder))
            {
                cacheFilePath = null;
                hashCache.Clear();
                histogramCache.Clear();
                return;
            }

            string cacheFolder = Path.Combine(projectFolder, ".yoable_cache");
            Directory.CreateDirectory(cacheFolder);
            cacheFilePath = Path.Combine(cacheFolder, "propagation_cache.json");
            LoadCache();
        }

        public double GetImageSimilarity(string pathA, string pathB, ImageSimilarityMode mode)
        {
            if (mode == ImageSimilarityMode.Histogram)
            {
                var histA = GetHistogram(pathA);
                var histB = GetHistogram(pathB);
                return CosineSimilarity(histA, histB);
            }

            ulong hashA = GetImageHash(pathA);
            ulong hashB = GetImageHash(pathB);
            int distance = HammingDistance(hashA, hashB);
            return 1.0 - (distance / 64.0);
        }

        public ulong GetImageHash(string imagePath)
        {
            if (hashCache.TryGetValue(imagePath, out var cached))
                return cached;

            using var bitmap = new Bitmap(imagePath);
            ulong hash = ComputeDHash(bitmap);
            hashCache[imagePath] = hash;
            SaveCache();
            return hash;
        }

        public float[] GetHistogram(string imagePath)
        {
            if (histogramCache.TryGetValue(imagePath, out var cached))
                return cached;

            using var bitmap = new Bitmap(imagePath);
            var hist = ComputeHistogram(bitmap, 16);
            histogramCache[imagePath] = hist;
            return hist;
        }

        public PropagationSummary RunImageSimilarity(
            IEnumerable<string> sourceFiles,
            IEnumerable<string> candidateFiles,
            double similarityThreshold,
            bool autoAccept,
            bool skipLabeled,
            int maxSuggestionsPerImage,
            double mergeIoUThreshold,
            IProgress<(string phase, int current, int total)> progress = null,
            CancellationToken cancellationToken = default)
        {
            var summary = new PropagationSummary();
            var sourceList = sourceFiles.ToList();
            var candidateList = candidateFiles.ToList();
            int totalComparisons = sourceList.Count * candidateList.Count;
            int processedComparisons = 0;
            var perImageAdded = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var sourceFile in sourceList)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                if (!imageManager.ImagePathMap.TryGetValue(sourceFile, out var sourceInfo))
                    continue;

                var sourceLabels = labelManager.GetLabels(sourceFile);
                if (sourceLabels.Count == 0)
                    continue;

                var sourceSize = sourceInfo.OriginalDimensions;

                foreach (var candidateFile in candidateList)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    processedComparisons++;
                    ReportProgress(progress, PhaseImageSimilarity, processedComparisons, totalComparisons);

                    if (candidateFile.Equals(sourceFile, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (!imageManager.ImagePathMap.TryGetValue(candidateFile, out var candidateInfo))
                        continue;

                    if (skipLabeled &&
                        labelManager.LabelStorage.TryGetValue(candidateFile, out var existingLabels) &&
                        existingLabels.Count > 0)
                    {
                        continue;
                    }

                    double similarity = GetImageSimilarity(sourceInfo.Path, candidateInfo.Path, GetSimilarityMode());
                    if (similarity < similarityThreshold)
                        continue;

                    if (maxSuggestionsPerImage > 0 &&
                        perImageAdded.TryGetValue(candidateFile, out int existingCount) &&
                        existingCount >= maxSuggestionsPerImage)
                    {
                        continue;
                    }

                    var suggestions = new List<SuggestedLabel>();
                    foreach (var label in sourceLabels)
                    {
                        if (maxSuggestionsPerImage > 0 &&
                            perImageAdded.TryGetValue(candidateFile, out int addedCount) &&
                            addedCount + suggestions.Count >= maxSuggestionsPerImage)
                            break;

                        var rect = ScaleRect(label.Rect, sourceSize, candidateInfo.OriginalDimensions);
                        if (rect.Width <= 1 || rect.Height <= 1)
                            continue;

                        suggestions.Add(SuggestedLabel.FromRect(rect, label.ClassId, SuggestionSource.ImageSimilarity, similarity, sourceFile));
                    }

                    if (suggestions.Count == 0)
                        continue;

                    if (autoAccept)
                    {
                        int added = AddLabels(candidateFile, suggestions);
                        summary.LabelsAdded += added;
                        perImageAdded.TryGetValue(candidateFile, out int previousAdded);
                        perImageAdded[candidateFile] = previousAdded + added;
                    }
                    else
                    {
                        labelManager.AddSuggestions(candidateFile, suggestions, mergeIoUThreshold);
                        summary.SuggestionsAdded += suggestions.Count;
                        perImageAdded.TryGetValue(candidateFile, out int previousAdded);
                        perImageAdded[candidateFile] = previousAdded + suggestions.Count;
                    }

                    summary.ImagesAffected++;
                }
            }

            ReportProgress(progress, PhaseImageSimilarity, totalComparisons, totalComparisons, true);
            return summary;
        }

        public PropagationSummary RunObjectSimilarity(
            IEnumerable<string> sourceFiles,
            IEnumerable<string> candidateFiles,
            double objectThreshold,
            bool autoAccept,
            bool skipLabeled,
            bool restrictToSimilar,
            double imageSimilarityThreshold,
            int maxSuggestionsPerImage,
            int minBoxSize,
            int candidateLimit,
            int searchStride,
            double mergeIoUThreshold,
            IProgress<(string phase, int current, int total)> progress = null,
            CancellationToken cancellationToken = default)
        {
            var summary = new PropagationSummary();
            var sourceList = sourceFiles.ToList();
            var candidateList = candidateFiles.ToList();
            var similarityMode = GetSimilarityMode();
            var perImageAdded = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            int totalRankComparisons = sourceList.Count * candidateList.Count;
            int processedRankComparisons = 0;

            foreach (var sourceFile in sourceList)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                if (!imageManager.ImagePathMap.TryGetValue(sourceFile, out var sourceInfo))
                    continue;

                var sourceLabels = labelManager.GetLabels(sourceFile);
                if (sourceLabels.Count == 0)
                    continue;

                using var sourceBitmap = new Bitmap(sourceInfo.Path);

                var rankedCandidates = new List<(string file, double similarity)>();
                foreach (var candidateFile in candidateList)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    processedRankComparisons++;
                    ReportProgress(progress, PhaseObjectRanking, processedRankComparisons, totalRankComparisons);

                    if (candidateFile.Equals(sourceFile, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (!imageManager.ImagePathMap.TryGetValue(candidateFile, out var candidateInfo))
                        continue;

                    if (skipLabeled &&
                        labelManager.LabelStorage.TryGetValue(candidateFile, out var existingLabels) &&
                        existingLabels.Count > 0)
                    {
                        continue;
                    }

                    double similarity = 1.0;
                    if (restrictToSimilar)
                    {
                        similarity = GetImageSimilarity(sourceInfo.Path, candidateInfo.Path, similarityMode);
                        if (similarity < imageSimilarityThreshold)
                            continue;
                    }

                    rankedCandidates.Add((candidateFile, similarity));
                }

                if (restrictToSimilar)
                {
                    rankedCandidates = rankedCandidates
                        .OrderByDescending(c => c.similarity)
                        .Take(candidateLimit)
                        .ToList();
                }
                else
                {
                    rankedCandidates = rankedCandidates.Take(candidateLimit).ToList();
                }

                int totalMatchChecks = sourceLabels.Count * rankedCandidates.Count;
                int processedMatchChecks = 0;

                foreach (var label in sourceLabels)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    var templateRect = label.Rect;
                    if (templateRect.Width < minBoxSize || templateRect.Height < minBoxSize)
                        continue;

                    using var template = CropBitmap(sourceBitmap, templateRect);
                    if (template == null)
                        continue;

                    foreach (var candidate in rankedCandidates)
                    {
                        if (cancellationToken.IsCancellationRequested)
                            break;

                        processedMatchChecks++;
                        ReportProgress(progress, PhaseObjectMatching, processedMatchChecks, totalMatchChecks);

                        if (maxSuggestionsPerImage > 0 &&
                            perImageAdded.TryGetValue(candidate.file, out int addedCount) &&
                            addedCount >= maxSuggestionsPerImage)
                        {
                            continue;
                        }

                        if (!imageManager.ImagePathMap.TryGetValue(candidate.file, out var candidateInfo))
                            continue;

                        using var candidateBitmap = new Bitmap(candidateInfo.Path);
                        var match = FindBestTemplateMatch(candidateBitmap, template, searchStride);
                        if (match.Score < objectThreshold)
                            continue;

                        var suggestionRect = match.Rect;
                        if (suggestionRect.Width < minBoxSize || suggestionRect.Height < minBoxSize)
                            continue;

                        var suggestion = SuggestedLabel.FromRect(suggestionRect, label.ClassId, SuggestionSource.ObjectSimilarity, match.Score, sourceFile);
                        if (autoAccept)
                        {
                            int added = AddLabels(candidate.file, new List<SuggestedLabel> { suggestion });
                            summary.LabelsAdded += added;
                            perImageAdded.TryGetValue(candidate.file, out int previousAdded);
                            perImageAdded[candidate.file] = previousAdded + added;
                        }
                        else
                        {
                            labelManager.AddSuggestions(candidate.file, new List<SuggestedLabel> { suggestion }, mergeIoUThreshold);
                            summary.SuggestionsAdded += 1;
                            perImageAdded.TryGetValue(candidate.file, out int previousAdded);
                            perImageAdded[candidate.file] = previousAdded + 1;
                        }

                        summary.ImagesAffected++;

                        if (maxSuggestionsPerImage > 0 &&
                            perImageAdded.TryGetValue(candidate.file, out int currentAdded) &&
                            currentAdded >= maxSuggestionsPerImage)
                        {
                            break;
                        }
                    }
                }

                ReportProgress(progress, PhaseObjectMatching, totalMatchChecks, totalMatchChecks, true);
            }

            ReportProgress(progress, PhaseObjectRanking, totalRankComparisons, totalRankComparisons, true);
            return summary;
        }

        public PropagationSummary RunTracking(
            string startFile,
            IReadOnlyList<string> orderedFiles,
            int frameWindow,
            double trackingThreshold,
            bool autoAccept,
            bool skipLabeled,
            int maxSuggestionsPerImage,
            int minBoxSize,
            int searchStride,
            double mergeIoUThreshold,
            IProgress<(string phase, int current, int total)> progress = null,
            CancellationToken cancellationToken = default)
        {
            var summary = new PropagationSummary();
            var perImageAdded = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            int startIndex = -1;
            for (int i = 0; i < orderedFiles.Count; i++)
            {
                if (string.Equals(orderedFiles[i], startFile, StringComparison.OrdinalIgnoreCase))
                {
                    startIndex = i;
                    break;
                }
            }
            if (startIndex < 0)
                return summary;

            if (!imageManager.ImagePathMap.TryGetValue(startFile, out var startInfo))
                return summary;

            var startLabels = labelManager.GetLabels(startFile);
            if (startLabels.Count == 0)
                return summary;

            using var startBitmap = new Bitmap(startInfo.Path);

            int maxForward = Math.Min(frameWindow, orderedFiles.Count - startIndex - 1);
            int maxBackward = Math.Min(frameWindow, startIndex);
            int targetFrames = maxForward + maxBackward;
            int totalTrackChecks = targetFrames * startLabels.Count;
            int processedTrackChecks = 0;

            for (int offset = 1; offset <= frameWindow; offset++)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                int forwardIndex = startIndex + offset;
                int backwardIndex = startIndex - offset;

                if (forwardIndex < orderedFiles.Count)
                {
                    summary = TrackToFrame(startFile, orderedFiles[forwardIndex], startLabels, startBitmap,
                        trackingThreshold, autoAccept, skipLabeled, maxSuggestionsPerImage, minBoxSize, searchStride, mergeIoUThreshold, summary, perImageAdded,
                        ref processedTrackChecks, totalTrackChecks, progress, cancellationToken);
                }

                if (backwardIndex >= 0)
                {
                    summary = TrackToFrame(startFile, orderedFiles[backwardIndex], startLabels, startBitmap,
                        trackingThreshold, autoAccept, skipLabeled, maxSuggestionsPerImage, minBoxSize, searchStride, mergeIoUThreshold, summary, perImageAdded,
                        ref processedTrackChecks, totalTrackChecks, progress, cancellationToken);
                }
            }

            ReportProgress(progress, PhaseTracking, totalTrackChecks, totalTrackChecks, true);
            return summary;
        }

        private PropagationSummary TrackToFrame(
            string sourceFile,
            string targetFile,
            List<LabelData> startLabels,
            Bitmap startBitmap,
            double trackingThreshold,
            bool autoAccept,
            bool skipLabeled,
            int maxSuggestionsPerImage,
            int minBoxSize,
            int searchStride,
            double mergeIoUThreshold,
            PropagationSummary summary,
            Dictionary<string, int> perImageAdded,
            ref int processedTrackChecks,
            int totalTrackChecks,
            IProgress<(string phase, int current, int total)> progress,
            CancellationToken cancellationToken = default)
        {
            if (!imageManager.ImagePathMap.TryGetValue(targetFile, out var targetInfo))
                return summary;

            if (maxSuggestionsPerImage > 0 &&
                perImageAdded.TryGetValue(targetFile, out int addedCount) &&
                addedCount >= maxSuggestionsPerImage)
            {
                return summary;
            }

            if (skipLabeled &&
                labelManager.LabelStorage.TryGetValue(targetFile, out var existingLabels) &&
                existingLabels.Count > 0)
            {
                return summary;
            }

            using var targetBitmap = new Bitmap(targetInfo.Path);
            foreach (var label in startLabels)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                processedTrackChecks++;
                ReportProgress(progress, PhaseTracking, processedTrackChecks, totalTrackChecks);

                var templateRect = label.Rect;
                if (templateRect.Width < minBoxSize || templateRect.Height < minBoxSize)
                    continue;

                using var template = CropBitmap(startBitmap, templateRect);
                if (template == null)
                    continue;

                var match = FindBestTemplateMatch(targetBitmap, template, searchStride);
                if (match.Score < trackingThreshold)
                    continue;

                var suggestionRect = match.Rect;
                if (suggestionRect.Width < minBoxSize || suggestionRect.Height < minBoxSize)
                    continue;

                var suggestion = SuggestedLabel.FromRect(suggestionRect, label.ClassId, SuggestionSource.Tracking, match.Score, sourceFile);
                if (autoAccept)
                {
                    int added = AddLabels(targetFile, new List<SuggestedLabel> { suggestion });
                    summary.LabelsAdded += added;
                    perImageAdded.TryGetValue(targetFile, out int previousAdded);
                    perImageAdded[targetFile] = previousAdded + added;
                }
                else
                {
                    labelManager.AddSuggestions(targetFile, new List<SuggestedLabel> { suggestion }, mergeIoUThreshold);
                    summary.SuggestionsAdded += 1;
                    perImageAdded.TryGetValue(targetFile, out int previousAdded);
                    perImageAdded[targetFile] = previousAdded + 1;
                }

                summary.ImagesAffected++;

                if (maxSuggestionsPerImage > 0 &&
                    perImageAdded.TryGetValue(targetFile, out int currentAdded) &&
                    currentAdded >= maxSuggestionsPerImage)
                {
                    break;
                }
            }

            return summary;
        }

        private static void ReportProgress(IProgress<(string phase, int current, int total)> progress, string phase, int current, int total, bool force = false)
        {
            if (progress == null || total <= 0)
                return;

            if (!force && current % ProgressInterval != 0)
                return;

            progress.Report((phase, current, total));
        }

        private int AddLabels(string fileName, List<SuggestedLabel> suggestions)
        {
            if (suggestions == null || suggestions.Count == 0)
                return 0;

            var labels = labelManager.LabelStorage.TryGetValue(fileName, out var existing)
                ? existing.Select(l => new LabelData(l)).ToList()
                : new List<LabelData>();
            double mergeIoUThreshold = Properties.Settings.Default.EnsembleIoUThreshold;
            int added = 0;

            foreach (var suggestion in suggestions)
            {
                var rect = suggestion.ToRect();
                bool overlaps = labels.Any(l => ComputeIoU(l.Rect, rect) >= mergeIoUThreshold);
                if (overlaps)
                    continue;

                labels.Add(new LabelData($"Suggested Label {labels.Count + 1}", rect, suggestion.ClassId));
                added++;
            }

            labelManager.SaveLabels(fileName, labels);
            return added;
        }

        private static double ComputeIoU(System.Windows.Rect rect1, System.Windows.Rect rect2)
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

        private static System.Windows.Rect ScaleRect(System.Windows.Rect rect, System.Windows.Size sourceSize, System.Windows.Size targetSize)
        {
            if (sourceSize.Width <= 0 || sourceSize.Height <= 0)
                return rect;

            double x = rect.X / sourceSize.Width;
            double y = rect.Y / sourceSize.Height;
            double w = rect.Width / sourceSize.Width;
            double h = rect.Height / sourceSize.Height;

            return new System.Windows.Rect(
                x * targetSize.Width,
                y * targetSize.Height,
                w * targetSize.Width,
                h * targetSize.Height);
        }

        private ImageSimilarityMode GetSimilarityMode()
        {
            return (ImageSimilarityMode)Properties.Settings.Default.PropagationImageSimilarityMode;
        }

        private void LoadCache()
        {
            if (string.IsNullOrEmpty(cacheFilePath) || !File.Exists(cacheFilePath))
                return;

            try
            {
                string json = File.ReadAllText(cacheFilePath);
                var data = JsonSerializer.Deserialize<PropagationCache>(json);
                if (data?.ImageHashes != null)
                {
                    hashCache.Clear();
                    foreach (var kvp in data.ImageHashes)
                    {
                        hashCache[kvp.Key] = kvp.Value;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to load propagation cache: {ex.Message}");
            }
        }

        private void SaveCache()
        {
            if (string.IsNullOrEmpty(cacheFilePath))
                return;

            try
            {
                var data = new PropagationCache
                {
                    ImageHashes = new Dictionary<string, ulong>(hashCache)
                };
                string json = JsonSerializer.Serialize(data);
                File.WriteAllText(cacheFilePath, json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to save propagation cache: {ex.Message}");
            }
        }

        private static ulong ComputeDHash(Bitmap image)
        {
            const int size = 8;
            using var resized = new Bitmap(size + 1, size);
            using (var g = Graphics.FromImage(resized))
            {
                g.DrawImage(image, 0, 0, size + 1, size);
            }

            ulong hash = 0;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    var left = resized.GetPixel(x, y);
                    var right = resized.GetPixel(x + 1, y);
                    int leftGray = (left.R + left.G + left.B) / 3;
                    int rightGray = (right.R + right.G + right.B) / 3;
                    int bitIndex = y * size + x;
                    if (leftGray > rightGray)
                    {
                        hash |= 1UL << bitIndex;
                    }
                }
            }

            return hash;
        }

        private static int HammingDistance(ulong a, ulong b)
        {
            ulong x = a ^ b;
            int count = 0;
            while (x != 0)
            {
                x &= x - 1;
                count++;
            }
            return count;
        }

        private static float[] ComputeHistogram(Bitmap bitmap, int bins)
        {
            var hist = new float[bins];
            int width = bitmap.Width;
            int height = bitmap.Height;
            int total = width * height;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    var pixel = bitmap.GetPixel(x, y);
                    int gray = (pixel.R + pixel.G + pixel.B) / 3;
                    int index = (gray * bins) / 256;
                    hist[index]++;
                }
            }

            if (total > 0)
            {
                for (int i = 0; i < bins; i++)
                {
                    hist[i] /= total;
                }
            }

            return hist;
        }

        private static double CosineSimilarity(float[] a, float[] b)
        {
            if (a == null || b == null || a.Length != b.Length)
                return 0;

            double dot = 0;
            double normA = 0;
            double normB = 0;
            for (int i = 0; i < a.Length; i++)
            {
                dot += a[i] * b[i];
                normA += a[i] * a[i];
                normB += b[i] * b[i];
            }

            if (normA == 0 || normB == 0)
                return 0;

            return dot / (Math.Sqrt(normA) * Math.Sqrt(normB));
        }

        private static Bitmap CropBitmap(Bitmap bitmap, System.Windows.Rect rect)
        {
            int x = (int)Math.Max(0, rect.X);
            int y = (int)Math.Max(0, rect.Y);
            int width = (int)Math.Min(bitmap.Width - x, rect.Width);
            int height = (int)Math.Min(bitmap.Height - y, rect.Height);

            if (width <= 0 || height <= 0)
                return null;

            var crop = new Bitmap(width, height);
            using (var g = Graphics.FromImage(crop))
            {
                g.DrawImage(bitmap, new Rectangle(0, 0, width, height),
                    new Rectangle(x, y, width, height), GraphicsUnit.Pixel);
            }
            return crop;
        }

        private static TemplateMatchResult FindBestTemplateMatch(Bitmap image, Bitmap template, int stride)
        {
            stride = Math.Max(1, stride);
            int maxSize = 320;
            float scale = Math.Min(1f, maxSize / (float)Math.Max(image.Width, image.Height));
            int scaledWidth = Math.Max(1, (int)(image.Width * scale));
            int scaledHeight = Math.Max(1, (int)(image.Height * scale));
            int scaledTemplateWidth = Math.Max(1, (int)(template.Width * scale));
            int scaledTemplateHeight = Math.Max(1, (int)(template.Height * scale));

            using var scaledImage = new Bitmap(image, new Size(scaledWidth, scaledHeight));
            using var scaledTemplate = new Bitmap(template, new Size(scaledTemplateWidth, scaledTemplateHeight));

            var imageData = ToGrayscaleArray(scaledImage, out int imgW, out int imgH);
            var templateData = ToGrayscaleArray(scaledTemplate, out int tplW, out int tplH);

            var match = FindBestMatch(imageData, imgW, imgH, templateData, tplW, tplH, stride);

            if (match.Score <= 0)
                return new TemplateMatchResult { Score = match.Score, Rect = System.Windows.Rect.Empty };

            double invScale = 1.0 / scale;
            var rect = new System.Windows.Rect(match.X * invScale, match.Y * invScale, tplW * invScale, tplH * invScale);
            return new TemplateMatchResult { Score = match.Score, Rect = rect };
        }

        private static byte[] ToGrayscaleArray(Bitmap bitmap, out int width, out int height)
        {
            width = bitmap.Width;
            height = bitmap.Height;
            var data = new byte[width * height];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    var pixel = bitmap.GetPixel(x, y);
                    data[y * width + x] = (byte)((pixel.R + pixel.G + pixel.B) / 3);
                }
            }

            return data;
        }

        private static TemplateMatchRaw FindBestMatch(byte[] image, int imageW, int imageH, byte[] template, int tplW, int tplH, int stride)
        {
            stride = Math.Max(1, stride);
            double bestScore = -1;
            int bestX = 0;
            int bestY = 0;

            int maxX = imageW - tplW;
            int maxY = imageH - tplH;
            if (maxX < 0 || maxY < 0)
                return new TemplateMatchRaw();

            for (int y = 0; y <= maxY; y += stride)
            {
                for (int x = 0; x <= maxX; x += stride)
                {
                    double sad = 0;
                    for (int j = 0; j < tplH; j++)
                    {
                        int imageIndex = (y + j) * imageW + x;
                        int tplIndex = j * tplW;
                        for (int i = 0; i < tplW; i++)
                        {
                            sad += Math.Abs(image[imageIndex + i] - template[tplIndex + i]);
                        }
                    }
                    double score = 1.0 - (sad / (tplW * tplH * 255.0));
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestX = x;
                        bestY = y;
                    }
                }
            }

            return new TemplateMatchRaw { Score = bestScore, X = bestX, Y = bestY };
        }

        private class PropagationCache
        {
            public Dictionary<string, ulong> ImageHashes { get; set; } = new();
        }

        private class TemplateMatchRaw
        {
            public double Score { get; set; }
            public int X { get; set; }
            public int Y { get; set; }
        }

        private class TemplateMatchResult
        {
            public double Score { get; set; }
            public System.Windows.Rect Rect { get; set; }
        }
    }
}
