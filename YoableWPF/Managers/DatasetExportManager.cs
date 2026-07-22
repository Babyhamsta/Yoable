using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace YoableWPF.Managers
{
    /// <summary>
    /// Options controlling a training-dataset export.
    /// </summary>
    public class TrainingExportOptions
    {
        public string OutputDirectory { get; set; }

        // Split ratios expressed as fractions (0-1). Normalized defensively at export time,
        // so callers may pass either fractions or any proportional weights.
        public double TrainRatio { get; set; } = 0.8;
        public double ValRatio { get; set; } = 0.2;
        public double TestRatio { get; set; } = 0.0;

        // Fixed seed so the same dataset always splits the same way.
        public int Seed { get; set; } = 0;

        // Include images that have no labels as background samples (image copied, empty .txt written).
        public bool IncludeUnlabeledAsBackground { get; set; } = false;

        // Only export images the user has marked Verified.
        public bool VerifiedOnly { get; set; } = false;
    }

    /// <summary>
    /// Outcome of a training-dataset export, used to build the completion message.
    /// </summary>
    public class TrainingExportResult
    {
        public int TrainCount { get; set; }
        public int ValCount { get; set; }
        public int TestCount { get; set; }
        public int SkippedCount { get; set; }
        public string OutputDirectory { get; set; }

        public int TotalExported => TrainCount + ValCount + TestCount;
    }

    /// <summary>
    /// Produces an Ultralytics-style training dataset (images/{train,val,test} + labels/{...} +
    /// classes.txt + data.yaml) from the current project's images and labels. Reuses
    /// <see cref="LabelManager.ExportLabelsToYolo(string, System.Windows.Size, List{LabelData})"/>
    /// for the actual label serialization so YOLO coordinate normalization stays in one place.
    /// </summary>
    public class DatasetExportManager
    {
        public async Task<TrainingExportResult> ExportAsync(
            TrainingExportOptions options,
            ImageManager imageManager,
            LabelManager labelManager,
            IReadOnlyList<LabelClass> projectClasses,
            IProgress<(int current, int total, string message)> progress = null,
            CancellationToken cancellationToken = default)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (string.IsNullOrWhiteSpace(options.OutputDirectory))
                throw new ArgumentException("Output directory is required.", nameof(options));

            // 1. Collect the images that qualify for export.
            var candidates = new List<(string fileName, ImageManager.ImageInfo info, List<LabelData> labels)>();
            foreach (var kvp in imageManager.ImagePathMap)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string fileName = kvp.Key;
                var info = kvp.Value;
                if (info == null) continue;

                if (options.VerifiedOnly)
                {
                    if (!imageManager.ImageStatuses.TryGetValue(fileName, out var status) ||
                        status != ImageStatus.Verified)
                        continue;
                }

                bool hasLabels = labelManager.LabelStorage.TryGetValue(fileName, out var labels) &&
                                 labels != null && labels.Count > 0;
                if (!hasLabels)
                {
                    if (!options.IncludeUnlabeledAsBackground)
                        continue;
                    labels = new List<LabelData>();
                }

                candidates.Add((fileName, info, labels));
            }

            var result = new TrainingExportResult { OutputDirectory = options.OutputDirectory };
            if (candidates.Count == 0)
                return result;

            // 2. Deterministic order, then seeded Fisher-Yates shuffle so Seed fully determines the split.
            var ordered = candidates
                .OrderBy(c => c.fileName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var rng = new Random(options.Seed);
            for (int i = ordered.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (ordered[i], ordered[j]) = (ordered[j], ordered[i]);
            }

            int total = ordered.Count;

            // Normalize ratios; guard against a degenerate all-zero configuration.
            double sum = options.TrainRatio + options.ValRatio + options.TestRatio;
            double valFrac, testFrac;
            if (sum <= 0)
            {
                valFrac = 0;
                testFrac = 0;
            }
            else
            {
                valFrac = options.ValRatio / sum;
                testFrac = options.TestRatio / sum;
            }

            int valCount = (int)Math.Floor(total * valFrac);
            int testCount = (int)Math.Floor(total * testFrac);
            int trainCount = total - valCount - testCount;
            // Train must never be empty; if rounding ever left it non-positive, fold everything back into train.
            if (trainCount <= 0)
            {
                trainCount = total;
                valCount = 0;
                testCount = 0;
            }

            bool useTest = testCount > 0;

            var assignments = new List<(string fileName, ImageManager.ImageInfo info, List<LabelData> labels, string split)>(total);
            for (int i = 0; i < total; i++)
            {
                string split = i < trainCount ? "train"
                    : i < trainCount + valCount ? "val"
                    : "test";
                assignments.Add((ordered[i].fileName, ordered[i].info, ordered[i].labels, split));
            }

            // 3. Create the folder structure. train/val always exist; test only when used.
            var splits = new List<string> { "train", "val" };
            if (useTest) splits.Add("test");
            foreach (var split in splits)
            {
                Directory.CreateDirectory(Path.Combine(options.OutputDirectory, "images", split));
                Directory.CreateDirectory(Path.Combine(options.OutputDirectory, "labels", split));
            }

            // 4. Copy images and write labels in parallel (mirrors LabelManager.ExportLabelsBatchAsync).
            int processed = 0;
            int skipped = 0;
            await Task.Run(() =>
            {
                Parallel.ForEach(assignments,
                    new ParallelOptions { CancellationToken = cancellationToken },
                    (item) =>
                    {
                        try
                        {
                            string srcImage = item.info.Path;
                            if (string.IsNullOrEmpty(srcImage) || !File.Exists(srcImage))
                            {
                                Interlocked.Increment(ref skipped);
                                return;
                            }

                            string destImage = Path.Combine(options.OutputDirectory, "images", item.split, item.fileName);
                            File.Copy(srcImage, destImage, overwrite: true);

                            string labelName = Path.GetFileNameWithoutExtension(item.fileName) + ".txt";
                            string destLabel = Path.Combine(options.OutputDirectory, "labels", item.split, labelName);

                            if (item.labels.Count > 0)
                                labelManager.ExportLabelsToYolo(destLabel, item.info.OriginalDimensions, item.labels);
                            else
                                File.WriteAllText(destLabel, string.Empty); // background sample

                            int done = Interlocked.Increment(ref processed);
                            progress?.Report((done, total, $"Exporting training dataset... {done}/{total}"));
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            Interlocked.Increment(ref skipped);
                            Debug.WriteLine($"Training export error for {item.fileName}: {ex.Message}");
                        }
                    });
            }, cancellationToken);

            // 5. classes.txt + data.yaml (with split paths).
            WriteClassFiles(options.OutputDirectory, projectClasses, useTest);

            // 6. Report intended split sizes; source files that went missing are counted as skipped.
            result.TrainCount = assignments.Count(a => a.split == "train");
            result.ValCount = assignments.Count(a => a.split == "val");
            result.TestCount = assignments.Count(a => a.split == "test");
            result.SkippedCount = skipped;
            return result;
        }

        /// <summary>
        /// Builds a class-name array indexed so that array index == ClassId. Gaps in the id
        /// sequence are filled with "class_N" placeholders. Shared by the flat label export and
        /// the training-dataset export so both produce identical class ordering/naming.
        /// </summary>
        public static string[] BuildClassNames(IReadOnlyList<LabelClass> classes)
        {
            var real = (classes ?? Array.Empty<LabelClass>() as IReadOnlyList<LabelClass>)
                .Where(c => c != null && c.ClassId >= 0)
                .OrderBy(c => c.ClassId)
                .ToList();

            if (real.Count == 0)
                return Array.Empty<string>();

            int maxClassId = real.Max(c => c.ClassId);
            var names = new string[maxClassId + 1];
            for (int i = 0; i < names.Length; i++)
                names[i] = $"class_{i}";

            foreach (var c in real)
                names[c.ClassId] = string.IsNullOrWhiteSpace(c.Name) ? $"class_{c.ClassId}" : c.Name.Trim();

            return names;
        }

        /// <summary>
        /// Writes classes.txt and a training-ready data.yaml (with path/train/val[/test]) to the
        /// output directory.
        /// </summary>
        private static void WriteClassFiles(string outputDirectory, IReadOnlyList<LabelClass> projectClasses, bool useTest)
        {
            try
            {
                var names = BuildClassNames(projectClasses);
                if (names.Length == 0)
                    return;

                File.WriteAllLines(Path.Combine(outputDirectory, "classes.txt"), names);

                var yaml = new StringBuilder();
                // Absolute path with forward slashes keeps the config portable to Linux training boxes.
                string absPath = Path.GetFullPath(outputDirectory).Replace('\\', '/');
                yaml.AppendLine($"path: {absPath}");
                yaml.AppendLine("train: images/train");
                yaml.AppendLine("val: images/val");
                if (useTest)
                    yaml.AppendLine("test: images/test");
                yaml.AppendLine();
                yaml.AppendLine($"nc: {names.Length}");
                yaml.Append("names: [");
                yaml.Append(string.Join(", ", names.Select(n => $"'{n.Replace("'", "''")}'")));
                yaml.AppendLine("]");

                File.WriteAllText(Path.Combine(outputDirectory, "data.yaml"), yaml.ToString());
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to write class files: {ex.Message}");
            }
        }
    }
}
