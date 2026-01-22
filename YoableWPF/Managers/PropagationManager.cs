using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

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
        private const int MaxMatchingSize = 640; // Increased from 320 for better quality
        private const int MinTemplateSize = 32;  // Don't shrink templates below this
        private const double MinMatchThreshold = 0.3; // Minimum NCC score to consider a match valid

        private readonly LabelManager labelManager;
        private readonly ImageManager imageManager;
        private readonly ConcurrentDictionary<string, ulong> hashCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, float[]> histogramCache = new(StringComparer.OrdinalIgnoreCase);
        private string cacheFilePath;
        private readonly object saveLock = new();

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
                // Keep in-memory caches for non-project mode
                return;
            }

            string cacheFolder = Path.Combine(projectFolder, ".yoable_cache");
            Directory.CreateDirectory(cacheFolder);
            cacheFilePath = Path.Combine(cacheFolder, "propagation_cache.json");
            LoadCache();
        }

        #region Bitmap Cache

        /// <summary>
        /// Thread-safe bitmap cache for sequential processing.
        /// Caches raw byte data and dimensions, returns new Bitmap instances.
        /// </summary>
        private class BitmapCache : IDisposable
        {
            private readonly Dictionary<string, (byte[] data, int width, int height, int stride)> cache
                = new(StringComparer.OrdinalIgnoreCase);
            private readonly object cacheLock = new();

            public Bitmap GetOrLoad(string path)
            {
                (byte[] data, int width, int height, int stride) cached;

                lock (cacheLock)
                {
                    if (!cache.TryGetValue(path, out cached))
                    {
                        // Load and convert to byte array
                        using var original = new Bitmap(path);
                        var format = System.Drawing.Imaging.PixelFormat.Format32bppArgb;
                        var rect = new Rectangle(0, 0, original.Width, original.Height);

                        using var converted = new Bitmap(original.Width, original.Height, format);
                        using (var g = Graphics.FromImage(converted))
                        {
                            g.DrawImage(original, 0, 0, original.Width, original.Height);
                        }

                        var bitmapData = converted.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly, format);
                        try
                        {
                            int stride = Math.Abs(bitmapData.Stride);
                            int byteCount = stride * converted.Height;
                            var data = new byte[byteCount];
                            System.Runtime.InteropServices.Marshal.Copy(bitmapData.Scan0, data, 0, byteCount);
                            cached = (data, original.Width, original.Height, stride);
                            cache[path] = cached;
                        }
                        finally
                        {
                            converted.UnlockBits(bitmapData);
                        }
                    }
                }

                // Create a new Bitmap from the cached data (outside lock for better parallelism)
                var format2 = System.Drawing.Imaging.PixelFormat.Format32bppArgb;
                var bitmap = new Bitmap(cached.width, cached.height, format2);
                var rect2 = new Rectangle(0, 0, cached.width, cached.height);
                var bmpData = bitmap.LockBits(rect2, System.Drawing.Imaging.ImageLockMode.WriteOnly, format2);
                try
                {
                    // Copy row by row to handle stride differences
                    int destStride = Math.Abs(bmpData.Stride);
                    int srcStride = cached.stride;
                    int rowBytes = cached.width * 4; // 4 bytes per pixel for 32bpp

                    if (destStride == srcStride)
                    {
                        System.Runtime.InteropServices.Marshal.Copy(cached.data, 0, bmpData.Scan0, cached.data.Length);
                    }
                    else
                    {
                        for (int y = 0; y < cached.height; y++)
                        {
                            System.Runtime.InteropServices.Marshal.Copy(
                                cached.data, y * srcStride,
                                bmpData.Scan0 + y * destStride, rowBytes);
                        }
                    }
                }
                finally
                {
                    bitmap.UnlockBits(bmpData);
                }
                return bitmap;
            }

            public void Dispose()
            {
                lock (cacheLock)
                {
                    cache.Clear();
                }
            }
        }

        #endregion

        #region Image Similarity

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
            return hashCache.GetOrAdd(imagePath, path =>
            {
                using var bitmap = new Bitmap(path);
                ulong hash = ComputeDHash(bitmap);
                SaveCacheDebounced();
                return hash;
            });
        }

        public float[] GetHistogram(string imagePath)
        {
            return histogramCache.GetOrAdd(imagePath, path =>
            {
                using var bitmap = new Bitmap(path);
                return ComputeHistogram(bitmap, 16);
            });
        }

        #endregion

        #region Run Image Similarity (Parallelized)

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
            var perImageAdded = new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var summaryLock = new object();

            // Prepare source data
            var sourcesWithLabels = sourceList
                .Where(f => imageManager.ImagePathMap.TryGetValue(f, out _))
                .Select(f => new
                {
                    File = f,
                    Info = imageManager.ImagePathMap[f],
                    Labels = labelManager.GetLabels(f)
                })
                .Where(s => s.Labels.Count > 0)
                .ToList();

            if (sourcesWithLabels.Count == 0)
                return summary;

            // Process candidates in parallel
            Parallel.ForEach(candidateList, new ParallelOptions { CancellationToken = cancellationToken }, candidateFile =>
            {
                if (cancellationToken.IsCancellationRequested)
                    return;

                if (!imageManager.ImagePathMap.TryGetValue(candidateFile, out var candidateInfo))
                    return;

                if (skipLabeled &&
                    labelManager.LabelStorage.TryGetValue(candidateFile, out var existingLabels) &&
                    existingLabels.Count > 0)
                {
                    return;
                }

                foreach (var source in sourcesWithLabels)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    Interlocked.Increment(ref processedComparisons);
                    ReportProgress(progress, PhaseImageSimilarity, processedComparisons, totalComparisons);

                    if (candidateFile.Equals(source.File, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (maxSuggestionsPerImage > 0 &&
                        perImageAdded.TryGetValue(candidateFile, out int existingCount) &&
                        existingCount >= maxSuggestionsPerImage)
                    {
                        break;
                    }

                    double similarity = GetImageSimilarity(source.Info.Path, candidateInfo.Path, GetSimilarityMode());
                    if (similarity < similarityThreshold)
                        continue;

                    var suggestions = new List<SuggestedLabel>();
                    foreach (var label in source.Labels)
                    {
                        if (maxSuggestionsPerImage > 0)
                        {
                            int currentCount = perImageAdded.GetOrAdd(candidateFile, 0);
                            if (currentCount + suggestions.Count >= maxSuggestionsPerImage)
                                break;
                        }

                        var rect = ScaleRect(label.Rect, source.Info.OriginalDimensions, candidateInfo.OriginalDimensions);
                        if (rect.Width <= 1 || rect.Height <= 1)
                            continue;

                        suggestions.Add(SuggestedLabel.FromRect(rect, label.ClassId, SuggestionSource.ImageSimilarity, similarity, source.File));
                    }

                    if (suggestions.Count == 0)
                        continue;

                    lock (summaryLock)
                    {
                        if (autoAccept)
                        {
                            int added = AddLabels(candidateFile, suggestions);
                            summary.LabelsAdded += added;
                            perImageAdded.AddOrUpdate(candidateFile, added, (k, v) => v + added);
                            if (added > 0) summary.ImagesAffected++;
                        }
                        else
                        {
                            int added = labelManager.AddSuggestions(candidateFile, suggestions, mergeIoUThreshold);
                            summary.SuggestionsAdded += added;
                            perImageAdded.AddOrUpdate(candidateFile, added, (k, v) => v + added);
                            if (added > 0) summary.ImagesAffected++;
                        }
                    }
                }
            });

            ReportProgress(progress, PhaseImageSimilarity, totalComparisons, totalComparisons, true);
            return summary;
        }

        #endregion

        #region Run Object Similarity (Parallelized with Bitmap Cache)

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
            var perImageAdded = new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var summaryLock = new object();
            int totalRankComparisons = sourceList.Count * candidateList.Count;
            int processedRankComparisons = 0;

            using var bitmapCache = new BitmapCache();

            foreach (var sourceFile in sourceList)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                if (!imageManager.ImagePathMap.TryGetValue(sourceFile, out var sourceInfo))
                    continue;

                var sourceLabels = labelManager.GetLabels(sourceFile);
                if (sourceLabels.Count == 0)
                    continue;

                using var sourceBitmap = bitmapCache.GetOrLoad(sourceInfo.Path);

                // Build ranked candidates
                var rankedCandidates = new ConcurrentBag<(string file, double similarity)>();
                Parallel.ForEach(candidateList, new ParallelOptions { CancellationToken = cancellationToken }, candidateFile =>
                {
                    if (cancellationToken.IsCancellationRequested)
                        return;

                    Interlocked.Increment(ref processedRankComparisons);
                    ReportProgress(progress, PhaseObjectRanking, processedRankComparisons, totalRankComparisons);

                    if (candidateFile.Equals(sourceFile, StringComparison.OrdinalIgnoreCase))
                        return;

                    if (!imageManager.ImagePathMap.TryGetValue(candidateFile, out var candidateInfo))
                        return;

                    if (skipLabeled &&
                        labelManager.LabelStorage.TryGetValue(candidateFile, out var existingLabels) &&
                        existingLabels.Count > 0)
                    {
                        return;
                    }

                    double similarity = 1.0;
                    if (restrictToSimilar)
                    {
                        similarity = GetImageSimilarity(sourceInfo.Path, candidateInfo.Path, similarityMode);
                        if (similarity < imageSimilarityThreshold)
                            return;
                    }

                    rankedCandidates.Add((candidateFile, similarity));
                });

                var sortedCandidates = restrictToSimilar
                    ? rankedCandidates.OrderByDescending(c => c.similarity).Take(candidateLimit).ToList()
                    : rankedCandidates.Take(candidateLimit).ToList();

                int totalMatchChecks = sourceLabels.Count * sortedCandidates.Count;
                int processedMatchChecks = 0;

                // Process labels against candidates (sequential to avoid GDI+ threading issues)
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

                    foreach (var candidate in sortedCandidates)
                    {
                        if (cancellationToken.IsCancellationRequested)
                            break;

                        Interlocked.Increment(ref processedMatchChecks);
                        ReportProgress(progress, PhaseObjectMatching, processedMatchChecks, totalMatchChecks);

                        if (maxSuggestionsPerImage > 0 &&
                            perImageAdded.TryGetValue(candidate.file, out int addedCount) &&
                            addedCount >= maxSuggestionsPerImage)
                        {
                            continue;
                        }

                        if (!imageManager.ImagePathMap.TryGetValue(candidate.file, out var candidateInfo))
                            continue;

                        using var candidateBitmap = bitmapCache.GetOrLoad(candidateInfo.Path);
                        var match = FindBestTemplateMatchNCC(candidateBitmap, template, searchStride);

                        // Skip if below minimum threshold (no valid match found)
                        if (match.Score < MinMatchThreshold || match.Score < objectThreshold)
                            continue;

                        var suggestionRect = match.Rect;
                        if (suggestionRect.Width < minBoxSize || suggestionRect.Height < minBoxSize)
                            continue;

                        var suggestion = SuggestedLabel.FromRect(suggestionRect, label.ClassId, SuggestionSource.ObjectSimilarity, match.Score, sourceFile);

                        if (autoAccept)
                        {
                            int added = AddLabels(candidate.file, new List<SuggestedLabel> { suggestion });
                            summary.LabelsAdded += added;
                            perImageAdded.AddOrUpdate(candidate.file, added, (k, v) => v + added);
                            if (added > 0) summary.ImagesAffected++;
                        }
                        else
                        {
                            int added = labelManager.AddSuggestions(candidate.file, new List<SuggestedLabel> { suggestion }, mergeIoUThreshold);
                            summary.SuggestionsAdded += added;
                            perImageAdded.AddOrUpdate(candidate.file, added, (k, v) => v + added);
                            if (added > 0) summary.ImagesAffected++;
                        }
                    }
                }

                ReportProgress(progress, PhaseObjectMatching, totalMatchChecks, totalMatchChecks, true);
            }

            ReportProgress(progress, PhaseObjectRanking, totalRankComparisons, totalRankComparisons, true);
            return summary;
        }

        #endregion

        #region Run Tracking (Improved with NCC and Constrained Search)

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
            var perImageAdded = new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);

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

            using var bitmapCache = new BitmapCache();
            using var startBitmap = bitmapCache.GetOrLoad(startInfo.Path);

            int maxForward = Math.Min(frameWindow, orderedFiles.Count - startIndex - 1);
            int maxBackward = Math.Min(frameWindow, startIndex);
            int targetFrames = maxForward + maxBackward;
            int totalTrackChecks = targetFrames * startLabels.Count;
            int processedTrackChecks = 0;

            // Track positions for constrained search
            var forwardPositions = new Dictionary<int, System.Windows.Rect>();
            var backwardPositions = new Dictionary<int, System.Windows.Rect>();

            // Initialize with start positions
            for (int i = 0; i < startLabels.Count; i++)
            {
                forwardPositions[i] = startLabels[i].Rect;
                backwardPositions[i] = startLabels[i].Rect;
            }

            // Process forward frames sequentially (GDI+ isn't thread-safe)
            for (int offset = 1; offset <= maxForward && !cancellationToken.IsCancellationRequested; offset++)
            {
                int targetIndex = startIndex + offset;
                TrackToFrame(
                    startFile, orderedFiles[targetIndex], startLabels, startBitmap, bitmapCache,
                    forwardPositions, trackingThreshold, autoAccept, skipLabeled,
                    maxSuggestionsPerImage, minBoxSize, searchStride, mergeIoUThreshold,
                    summary, perImageAdded,
                    ref processedTrackChecks, totalTrackChecks, progress, cancellationToken);
            }

            // Process backward frames sequentially
            for (int offset = 1; offset <= maxBackward && !cancellationToken.IsCancellationRequested; offset++)
            {
                int targetIndex = startIndex - offset;
                TrackToFrame(
                    startFile, orderedFiles[targetIndex], startLabels, startBitmap, bitmapCache,
                    backwardPositions, trackingThreshold, autoAccept, skipLabeled,
                    maxSuggestionsPerImage, minBoxSize, searchStride, mergeIoUThreshold,
                    summary, perImageAdded,
                    ref processedTrackChecks, totalTrackChecks, progress, cancellationToken);
            }

            ReportProgress(progress, PhaseTracking, totalTrackChecks, totalTrackChecks, true);
            return summary;
        }

        private void TrackToFrame(
            string sourceFile,
            string targetFile,
            List<LabelData> startLabels,
            Bitmap startBitmap,
            BitmapCache bitmapCache,
            Dictionary<int, System.Windows.Rect> lastPositions,
            double trackingThreshold,
            bool autoAccept,
            bool skipLabeled,
            int maxSuggestionsPerImage,
            int minBoxSize,
            int searchStride,
            double mergeIoUThreshold,
            PropagationSummary summary,
            ConcurrentDictionary<string, int> perImageAdded,
            ref int processedTrackChecks,
            int totalTrackChecks,
            IProgress<(string phase, int current, int total)> progress,
            CancellationToken cancellationToken)
        {
            if (!imageManager.ImagePathMap.TryGetValue(targetFile, out var targetInfo))
                return;

            if (maxSuggestionsPerImage > 0 &&
                perImageAdded.TryGetValue(targetFile, out int addedCount) &&
                addedCount >= maxSuggestionsPerImage)
            {
                return;
            }

            if (skipLabeled &&
                labelManager.LabelStorage.TryGetValue(targetFile, out var existingLabels) &&
                existingLabels.Count > 0)
            {
                return;
            }

            using var targetBitmap = bitmapCache.GetOrLoad(targetInfo.Path);

            for (int labelIndex = 0; labelIndex < startLabels.Count; labelIndex++)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                Interlocked.Increment(ref processedTrackChecks);
                ReportProgress(progress, PhaseTracking, processedTrackChecks, totalTrackChecks);

                var label = startLabels[labelIndex];
                var templateRect = label.Rect;
                if (templateRect.Width < minBoxSize || templateRect.Height < minBoxSize)
                    continue;

                using var template = CropBitmap(startBitmap, templateRect);
                if (template == null)
                    continue;

                // Get constrained search area based on last known position
                System.Windows.Rect? searchArea = null;
                if (lastPositions.TryGetValue(labelIndex, out var lastPos))
                {
                    // Search within 2x the object size around the last known position
                    double searchMarginX = lastPos.Width * 2;
                    double searchMarginY = lastPos.Height * 2;
                    searchArea = new System.Windows.Rect(
                        Math.Max(0, lastPos.X - searchMarginX),
                        Math.Max(0, lastPos.Y - searchMarginY),
                        lastPos.Width + searchMarginX * 2,
                        lastPos.Height + searchMarginY * 2);
                }

                var match = FindBestTemplateMatchNCC(targetBitmap, template, searchStride, searchArea);

                // Skip if below minimum threshold (no valid match found)
                if (match.Score < MinMatchThreshold || match.Score < trackingThreshold)
                    continue;

                var suggestionRect = match.Rect;
                if (suggestionRect.Width < minBoxSize || suggestionRect.Height < minBoxSize)
                    continue;

                // Update last known position for next frame
                lastPositions[labelIndex] = suggestionRect;

                var suggestion = SuggestedLabel.FromRect(suggestionRect, label.ClassId, SuggestionSource.Tracking, match.Score, sourceFile);

                if (autoAccept)
                {
                    int added = AddLabels(targetFile, new List<SuggestedLabel> { suggestion });
                    summary.LabelsAdded += added;
                    perImageAdded.AddOrUpdate(targetFile, added, (k, v) => v + added);
                    if (added > 0) summary.ImagesAffected++;
                }
                else
                {
                    int added = labelManager.AddSuggestions(targetFile, new List<SuggestedLabel> { suggestion }, mergeIoUThreshold);
                    summary.SuggestionsAdded += added;
                    perImageAdded.AddOrUpdate(targetFile, added, (k, v) => v + added);
                    if (added > 0) summary.ImagesAffected++;
                }

                if (maxSuggestionsPerImage > 0 &&
                    perImageAdded.TryGetValue(targetFile, out int currentAdded) &&
                    currentAdded >= maxSuggestionsPerImage)
                {
                    break;
                }
            }
        }

        #endregion

        #region Template Matching with NCC (Normalized Cross-Correlation)

        /// <summary>
        /// Find best template match using Normalized Cross-Correlation (NCC).
        /// NCC is invariant to brightness/contrast changes and gives 0-1 scores.
        /// </summary>
        private static TemplateMatchResult FindBestTemplateMatchNCC(
            Bitmap image, Bitmap template, int stride, System.Windows.Rect? constrainedSearchArea = null)
        {
            stride = Math.Max(1, stride);

            // Calculate scale - use higher resolution (640px) and don't shrink templates too small
            float scale = Math.Min(1f, MaxMatchingSize / (float)Math.Max(image.Width, image.Height));
            int scaledTemplateWidth = Math.Max(MinTemplateSize, (int)(template.Width * scale));
            int scaledTemplateHeight = Math.Max(MinTemplateSize, (int)(template.Height * scale));

            // Recalculate scale if template would be too small
            if (template.Width * scale < MinTemplateSize || template.Height * scale < MinTemplateSize)
            {
                scale = Math.Min((float)MinTemplateSize / template.Width, (float)MinTemplateSize / template.Height);
                scale = Math.Min(scale, 1f); // Don't upscale
            }

            int scaledWidth = Math.Max(1, (int)(image.Width * scale));
            int scaledHeight = Math.Max(1, (int)(image.Height * scale));
            scaledTemplateWidth = Math.Max(1, (int)(template.Width * scale));
            scaledTemplateHeight = Math.Max(1, (int)(template.Height * scale));

            using var scaledImage = new Bitmap(image, new Size(scaledWidth, scaledHeight));
            using var scaledTemplate = new Bitmap(template, new Size(scaledTemplateWidth, scaledTemplateHeight));

            // Convert to RGB arrays for color-based matching
            var imageData = ToRGBArrays(scaledImage, out int imgW, out int imgH);
            var templateData = ToRGBArrays(scaledTemplate, out int tplW, out int tplH);

            // Calculate scaled search area if constrained
            int searchMinX = 0, searchMinY = 0;
            int searchMaxX = imgW - tplW;
            int searchMaxY = imgH - tplH;

            if (constrainedSearchArea.HasValue)
            {
                var area = constrainedSearchArea.Value;
                searchMinX = Math.Max(0, (int)(area.X * scale));
                searchMinY = Math.Max(0, (int)(area.Y * scale));
                searchMaxX = Math.Min(searchMaxX, (int)((area.X + area.Width) * scale) - tplW);
                searchMaxY = Math.Min(searchMaxY, (int)((area.Y + area.Height) * scale) - tplH);
            }

            if (searchMaxX < searchMinX || searchMaxY < searchMinY)
                return new TemplateMatchResult { Score = 0, Rect = System.Windows.Rect.Empty };

            var match = FindBestMatchNCC(imageData, imgW, imgH, templateData, tplW, tplH, stride,
                searchMinX, searchMinY, searchMaxX, searchMaxY);

            if (match.Score < MinMatchThreshold)
                return new TemplateMatchResult { Score = match.Score, Rect = System.Windows.Rect.Empty };

            double invScale = 1.0 / scale;
            var rect = new System.Windows.Rect(match.X * invScale, match.Y * invScale, tplW * invScale, tplH * invScale);
            return new TemplateMatchResult { Score = match.Score, Rect = rect };
        }

        /// <summary>
        /// Convert bitmap to separate R, G, B arrays
        /// </summary>
        private static (byte[] r, byte[] g, byte[] b) ToRGBArrays(Bitmap bitmap, out int width, out int height)
        {
            width = bitmap.Width;
            height = bitmap.Height;
            int size = width * height;
            var r = new byte[size];
            var g = new byte[size];
            var b = new byte[size];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    var pixel = bitmap.GetPixel(x, y);
                    int idx = y * width + x;
                    r[idx] = pixel.R;
                    g[idx] = pixel.G;
                    b[idx] = pixel.B;
                }
            }

            return (r, g, b);
        }

        /// <summary>
        /// Find best match using Normalized Cross-Correlation (NCC) on RGB channels
        /// </summary>
        private static TemplateMatchRaw FindBestMatchNCC(
            (byte[] r, byte[] g, byte[] b) image, int imageW, int imageH,
            (byte[] r, byte[] g, byte[] b) template, int tplW, int tplH,
            int stride, int minX, int minY, int maxX, int maxY)
        {
            stride = Math.Max(1, stride);
            double bestScore = -1;
            int bestX = 0;
            int bestY = 0;

            if (maxX < minX || maxY < minY)
                return new TemplateMatchRaw { Score = 0 };

            // Pre-compute template statistics for NCC
            var templateStats = ComputeTemplateStats(template, tplW, tplH);

            object lockObj = new object();

            // Parallelize the search
            Parallel.For(minY, maxY + 1, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, y =>
            {
                if ((y - minY) % stride != 0)
                    return;

                for (int x = minX; x <= maxX; x += stride)
                {
                    double ncc = ComputeNCC(image, imageW, template, tplW, tplH, x, y, templateStats);

                    if (ncc > bestScore)
                    {
                        lock (lockObj)
                        {
                            if (ncc > bestScore)
                            {
                                bestScore = ncc;
                                bestX = x;
                                bestY = y;
                            }
                        }
                    }
                }
            });

            return new TemplateMatchRaw { Score = bestScore, X = bestX, Y = bestY };
        }

        private static (double meanR, double meanG, double meanB, double stdR, double stdG, double stdB) ComputeTemplateStats(
            (byte[] r, byte[] g, byte[] b) template, int tplW, int tplH)
        {
            int n = tplW * tplH;
            double sumR = 0, sumG = 0, sumB = 0;
            double sumSqR = 0, sumSqG = 0, sumSqB = 0;

            for (int i = 0; i < n; i++)
            {
                sumR += template.r[i];
                sumG += template.g[i];
                sumB += template.b[i];
                sumSqR += template.r[i] * template.r[i];
                sumSqG += template.g[i] * template.g[i];
                sumSqB += template.b[i] * template.b[i];
            }

            double meanR = sumR / n;
            double meanG = sumG / n;
            double meanB = sumB / n;
            double varR = (sumSqR / n) - (meanR * meanR);
            double varG = (sumSqG / n) - (meanG * meanG);
            double varB = (sumSqB / n) - (meanB * meanB);

            return (meanR, meanG, meanB, Math.Sqrt(Math.Max(0, varR)), Math.Sqrt(Math.Max(0, varG)), Math.Sqrt(Math.Max(0, varB)));
        }

        private static double ComputeNCC(
            (byte[] r, byte[] g, byte[] b) image, int imageW,
            (byte[] r, byte[] g, byte[] b) template, int tplW, int tplH,
            int offsetX, int offsetY,
            (double meanR, double meanG, double meanB, double stdR, double stdG, double stdB) tplStats)
        {
            int n = tplW * tplH;

            // Compute image patch statistics
            double sumR = 0, sumG = 0, sumB = 0;
            double sumSqR = 0, sumSqG = 0, sumSqB = 0;

            for (int j = 0; j < tplH; j++)
            {
                int imgRowIdx = (offsetY + j) * imageW + offsetX;
                for (int i = 0; i < tplW; i++)
                {
                    int imgIdx = imgRowIdx + i;
                    sumR += image.r[imgIdx];
                    sumG += image.g[imgIdx];
                    sumB += image.b[imgIdx];
                    sumSqR += image.r[imgIdx] * image.r[imgIdx];
                    sumSqG += image.g[imgIdx] * image.g[imgIdx];
                    sumSqB += image.b[imgIdx] * image.b[imgIdx];
                }
            }

            double imgMeanR = sumR / n;
            double imgMeanG = sumG / n;
            double imgMeanB = sumB / n;
            double imgVarR = (sumSqR / n) - (imgMeanR * imgMeanR);
            double imgVarG = (sumSqG / n) - (imgMeanG * imgMeanG);
            double imgVarB = (sumSqB / n) - (imgMeanB * imgMeanB);
            double imgStdR = Math.Sqrt(Math.Max(0, imgVarR));
            double imgStdG = Math.Sqrt(Math.Max(0, imgVarG));
            double imgStdB = Math.Sqrt(Math.Max(0, imgVarB));

            // Compute cross-correlation for each channel
            double crossCorrR = 0, crossCorrG = 0, crossCorrB = 0;
            for (int j = 0; j < tplH; j++)
            {
                int imgRowIdx = (offsetY + j) * imageW + offsetX;
                int tplRowIdx = j * tplW;
                for (int i = 0; i < tplW; i++)
                {
                    int imgIdx = imgRowIdx + i;
                    int tplIdx = tplRowIdx + i;
                    crossCorrR += (image.r[imgIdx] - imgMeanR) * (template.r[tplIdx] - tplStats.meanR);
                    crossCorrG += (image.g[imgIdx] - imgMeanG) * (template.g[tplIdx] - tplStats.meanG);
                    crossCorrB += (image.b[imgIdx] - imgMeanB) * (template.b[tplIdx] - tplStats.meanB);
                }
            }

            // Compute NCC for each channel
            double denomR = n * imgStdR * tplStats.stdR;
            double denomG = n * imgStdG * tplStats.stdG;
            double denomB = n * imgStdB * tplStats.stdB;

            double nccR = denomR > 1e-10 ? crossCorrR / denomR : 0;
            double nccG = denomG > 1e-10 ? crossCorrG / denomG : 0;
            double nccB = denomB > 1e-10 ? crossCorrB / denomB : 0;

            // Average NCC across channels (gives better discrimination for colored objects)
            double avgNcc = (nccR + nccG + nccB) / 3.0;

            // Clamp to 0-1 range (NCC can be negative for anti-correlated regions)
            return Math.Max(0, Math.Min(1, (avgNcc + 1) / 2.0));
        }

        #endregion

        #region Helper Methods

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

        #endregion

        #region Cache Management

        private DateTime lastCacheSave = DateTime.MinValue;
        private const int CacheSaveDebounceMs = 5000;

        private void SaveCacheDebounced()
        {
            if (string.IsNullOrEmpty(cacheFilePath))
                return;

            if ((DateTime.Now - lastCacheSave).TotalMilliseconds < CacheSaveDebounceMs)
                return;

            SaveCache();
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

            lock (saveLock)
            {
                try
                {
                    lastCacheSave = DateTime.Now;
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
        }

        #endregion

        #region Hash/Histogram Computation

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

        #endregion

        #region Internal Classes

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

        #endregion
    }
}
