using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;

namespace YoableWPF.Managers
{
    /// <summary>
    /// Per-class aggregate used by the dataset statistics dashboard.
    /// </summary>
    public sealed class ClassStat
    {
        public int ClassId { get; set; }
        public string Name { get; set; }
        public Brush ColorBrush { get; set; }
        public int BoxCount { get; set; }
        public int ImageCount { get; set; }
        public double BoxPercent { get; set; }
        // Highest per-class box count in the dataset, so a per-row bar can size itself.
        public int BarMax { get; set; }

        public string DisplayName => $"{Name} (ID: {ClassId})";
        public string CountText => $"{BoxCount}  ({BoxPercent:F1}%)  ·  {ImageCount} img";
    }

    /// <summary>
    /// Snapshot of dataset statistics computed from the current project's images and labels.
    /// </summary>
    public sealed class DatasetStatistics
    {
        public int TotalImages { get; set; }
        public int LabeledImages { get; set; }
        public int UnlabeledImages { get; set; }
        public int TotalBoxes { get; set; }
        public double AvgBoxesPerLabeledImage { get; set; }

        public List<ClassStat> ClassStats { get; } = new();

        // Box-size buckets by fraction of image area (small < 1%, medium < 5%, large otherwise).
        public int SmallBoxes { get; set; }
        public int MediumBoxes { get; set; }
        public int LargeBoxes { get; set; }
        public int SizedBoxes => SmallBoxes + MediumBoxes + LargeBoxes;

        // Image review status breakdown.
        public int VerifiedCount { get; set; }
        public int NeedsReviewCount { get; set; }
        public int NoLabelCount { get; set; }
        public int SuggestedCount { get; set; }
    }

    /// <summary>
    /// Computes dataset statistics from in-memory image and label state. Pure aggregation over
    /// <see cref="LabelManager.LabelStorage"/> and <see cref="ImageManager.ImagePathMap"/>; no I/O.
    /// </summary>
    public sealed class DatasetStatisticsManager
    {
        public DatasetStatistics Compute(
            ImageManager imageManager,
            LabelManager labelManager,
            IReadOnlyList<LabelClass> projectClasses)
        {
            var stats = new DatasetStatistics();
            if (imageManager == null || labelManager == null)
                return stats;

            var images = imageManager.ImagePathMap;
            stats.TotalImages = images.Count;

            var boxCountByClass = new Dictionary<int, int>();
            var imagesByClass = new Dictionary<int, int>();
            int totalBoxes = 0;
            int labeledImages = 0;

            foreach (var kvp in images)
            {
                string fileName = kvp.Key;
                var info = kvp.Value;
                double imgArea = info != null
                    ? info.OriginalDimensions.Width * info.OriginalDimensions.Height
                    : 0;

                if (!labelManager.LabelStorage.TryGetValue(fileName, out var labels) ||
                    labels == null || labels.Count == 0)
                    continue;

                labeledImages++;
                var classesInImage = new HashSet<int>();

                foreach (var label in labels)
                {
                    totalBoxes++;
                    boxCountByClass.TryGetValue(label.ClassId, out int existing);
                    boxCountByClass[label.ClassId] = existing + 1;
                    classesInImage.Add(label.ClassId);

                    if (imgArea > 0)
                    {
                        double frac = (label.Rect.Width * label.Rect.Height) / imgArea;
                        if (frac < 0.01) stats.SmallBoxes++;
                        else if (frac < 0.05) stats.MediumBoxes++;
                        else stats.LargeBoxes++;
                    }
                }

                foreach (int classId in classesInImage)
                {
                    imagesByClass.TryGetValue(classId, out int imgCount);
                    imagesByClass[classId] = imgCount + 1;
                }
            }

            stats.TotalBoxes = totalBoxes;
            stats.LabeledImages = labeledImages;
            stats.UnlabeledImages = stats.TotalImages - labeledImages;
            stats.AvgBoxesPerLabeledImage = labeledImages > 0
                ? (double)totalBoxes / labeledImages
                : 0;

            foreach (var status in imageManager.ImageStatuses.Values)
            {
                switch (status)
                {
                    case ImageStatus.Verified: stats.VerifiedCount++; break;
                    case ImageStatus.VerificationNeeded: stats.NeedsReviewCount++; break;
                    case ImageStatus.Suggested: stats.SuggestedCount++; break;
                    case ImageStatus.NoLabel: stats.NoLabelCount++; break;
                }
            }

            // Build one row per real project class (ordered by id), keeping zero-count classes so
            // an under-represented class is visible rather than silently missing.
            var classRows = (projectClasses ?? new List<LabelClass>())
                .Where(c => c != null && c.ClassId >= 0)
                .OrderBy(c => c.ClassId)
                .Select(c =>
                {
                    boxCountByClass.TryGetValue(c.ClassId, out int bc);
                    imagesByClass.TryGetValue(c.ClassId, out int ic);
                    return new ClassStat
                    {
                        ClassId = c.ClassId,
                        Name = string.IsNullOrWhiteSpace(c.Name) ? $"class_{c.ClassId}" : c.Name,
                        ColorBrush = c.ColorBrush,
                        BoxCount = bc,
                        ImageCount = ic,
                        BoxPercent = totalBoxes > 0 ? (double)bc / totalBoxes * 100 : 0
                    };
                })
                .ToList();

            int barMax = classRows.Count > 0 ? classRows.Max(r => r.BoxCount) : 0;
            foreach (var row in classRows)
                row.BarMax = barMax > 0 ? barMax : 1;

            stats.ClassStats.AddRange(classRows);
            return stats;
        }
    }
}
