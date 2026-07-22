using System.Windows;
using System.Windows.Controls;
using YoableWPF.Managers;
using YoableWPF.Models;

namespace YoableWPF
{
    public partial class DuplicateImageReviewControl : UserControl
    {
        private readonly DuplicateImageDetector detector = new();
        private readonly List<DuplicateImageGroup> duplicateGroups = new();
        private ImageManager? imageManager;
        private LabelManager? labelManager;
        private Func<IReadOnlyList<LabelClass>>? getProjectClasses;
        private Func<string, bool>? removeImage;
        private Func<string, bool>? openImageForEditing;
        private CancellationTokenSource? scanCancellation;
        private int currentGroupIndex;
        private bool hasScanned;
        private int lastImageSignature;
        private bool isUpdatingGroupSelection;
        private bool isResolvingDuplicate;
        private bool isGroupListDirty = true;
        private int lastAutoResolvedCount;

        public DuplicateImageReviewControl()
        {
            InitializeComponent();
        }

        public void Initialize(
            ImageManager manager,
            LabelManager labels,
            Func<string, bool> removeImageCallback,
            Func<string, bool> openImageForEditingCallback,
            Func<IReadOnlyList<LabelClass>> projectClassesProvider)
        {
            imageManager = manager;
            labelManager = labels;
            removeImage = removeImageCallback;
            openImageForEditing = openImageForEditingCallback;
            getProjectClasses = projectClassesProvider;
        }

        public async Task EnsureScannedAsync()
        {
            if (!hasScanned || lastImageSignature != CalculateImageSignature())
                await ScanAsync();
            else if (duplicateGroups.Count > 0)
                await ShowCurrentPairAsync();
        }

        public void InvalidateResults()
        {
            hasScanned = false;
            duplicateGroups.Clear();
            DuplicateGroupListBox.Items.Clear();
            isGroupListDirty = true;
            currentGroupIndex = 0;
            lastAutoResolvedCount = 0;
            ReviewPanel.Visibility = Visibility.Collapsed;
            EmptyPanel.Visibility = Visibility.Visible;
            EmptyText.Text = GetString("Duplicate_NoScan", "Scan the current project for duplicate images.");
            SummaryText.Text = "";
        }

        private async void ScanButton_Click(object sender, RoutedEventArgs e)
        {
            await ScanAsync();
        }

        private async Task ScanAsync()
        {
            if (imageManager == null || labelManager == null)
                return;

            scanCancellation?.Cancel();
            scanCancellation?.Dispose();
            scanCancellation = new CancellationTokenSource();
            var cancellationToken = scanCancellation.Token;

            ScanButton.IsEnabled = false;
            ReviewPanel.Visibility = Visibility.Collapsed;
            EmptyPanel.Visibility = Visibility.Visible;
            EmptyText.Text = GetString("Duplicate_Scanning", "Scanning images...");
            ScanProgressBar.Visibility = Visibility.Visible;
            ScanProgressBar.Value = 0;
            lastAutoResolvedCount = 0;

            var snapshot = imageManager.ImagePathMap.ToArray();
            if (snapshot.Length < 2)
            {
                FinishEmptyScan("Duplicate_NoImages", "Add at least two images before scanning.");
                return;
            }

            var progress = new Progress<(int current, int total)>(value =>
            {
                ScanProgressBar.Maximum = Math.Max(1, value.total);
                ScanProgressBar.Value = value.current;
            });

            try
            {
                var results = await detector.FindDuplicatesAsync(snapshot, progress, cancellationToken);
                duplicateGroups.Clear();
                duplicateGroups.AddRange(results);
                lastAutoResolvedCount = AutoResolveMatchingLabelGroups();
                isGroupListDirty = true;
                currentGroupIndex = 0;
                hasScanned = true;
                lastImageSignature = CalculateImageSignature();

                if (duplicateGroups.Count == 0)
                {
                    if (lastAutoResolvedCount > 0)
                    {
                        FinishEmptyScan(
                            "Duplicate_AutoResolvedOnly",
                            "Duplicate images with identical labels were automatically resolved.");
                        EmptyText.Text = string.Format(
                            GetString(
                                "Duplicate_AutoResolvedOnly",
                                "Automatically removed {0} duplicate image(s) with identical labels from the project."),
                            lastAutoResolvedCount);
                    }
                    else
                    {
                        FinishEmptyScan("Duplicate_NoMatches", "No duplicate images found.");
                    }
                    return;
                }

                ScanProgressBar.Visibility = Visibility.Collapsed;
                ScanButton.IsEnabled = true;
                EmptyPanel.Visibility = Visibility.Collapsed;
                ReviewPanel.Visibility = Visibility.Visible;
                await ShowCurrentPairAsync();
            }
            catch (OperationCanceledException)
            {
                ScanButton.IsEnabled = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Duplicate scan failed: {ex.Message}");
                FinishEmptyScan("Duplicate_ScanFailed", "Could not scan duplicate images.");
            }
        }

        private void FinishEmptyScan(string resourceKey, string fallback)
        {
            hasScanned = true;
            ScanProgressBar.Visibility = Visibility.Collapsed;
            ScanButton.IsEnabled = true;
            ReviewPanel.Visibility = Visibility.Collapsed;
            EmptyPanel.Visibility = Visibility.Visible;
            EmptyText.Text = GetString(resourceKey, fallback);
            SummaryText.Text = "";
            DuplicateGroupListBox.Items.Clear();
        }

        private async Task ShowCurrentPairAsync()
        {
            if (imageManager == null || duplicateGroups.Count == 0)
            {
                FinishEmptyScan("Duplicate_Resolved", "All duplicate images have been resolved.");
                return;
            }

            var group = duplicateGroups[currentGroupIndex];
            var left = group.Candidates[0];
            var right = group.Candidates[1];
            int remainingImages = duplicateGroups.Sum(item => item.Candidates.Count - 1);

            string summary = string.Format(
                GetString("Duplicate_Summary", "{0} duplicate group(s), {1} extra image(s) remaining"),
                duplicateGroups.Count,
                remainingImages);
            if (lastAutoResolvedCount > 0)
            {
                summary += "  |  " + string.Format(
                    GetString(
                        "Duplicate_AutoResolvedSummary",
                        "Automatically resolved: {0}"),
                    lastAutoResolvedCount);
            }
            SummaryText.Text = summary;
            GroupProgressText.Text = string.Format(
                GetString("Duplicate_GroupProgress", "Group {0} of {1}"),
                Math.Min(currentGroupIndex + 1, duplicateGroups.Count),
                duplicateGroups.Count);
            RefreshGroupList();

            LeftFileNameText.Text = left.FileName;
            RightFileNameText.Text = right.FileName;
            var leftLabels = labelManager?.GetLabels(left.FileName) ?? new List<LabelData>();
            var rightLabels = labelManager?.GetLabels(right.FileName) ?? new List<LabelData>();
            LeftDetailsText.Text = BuildDetails(left, leftLabels.Count);
            RightDetailsText.Text = BuildDetails(right, rightLabels.Count);

            int generation = Environment.TickCount;
            Tag = generation;
            var leftTask = Task.Run(() => imageManager.Cache.GetOrLoad(left.FullPath));
            var rightTask = Task.Run(() => imageManager.Cache.GetOrLoad(right.FullPath));
            await Task.WhenAll(leftTask, rightTask);

            if (Tag is int activeGeneration && activeGeneration == generation)
            {
                var projectClasses = getProjectClasses?.Invoke() ?? Array.Empty<LabelClass>();
                LeftImage.SetContent(leftTask.Result, leftLabels, projectClasses);
                RightImage.SetContent(rightTask.Result, rightLabels, projectClasses);
            }
        }

        private string BuildDetails(DuplicateImageCandidate candidate, int labelCount)
        {
            string labels = string.Format(
                GetString("Duplicate_LabelCount", "{0} label(s)"),
                labelCount);
            return $"{labels}  |  {FormatFileSize(candidate.FileSize)}";
        }

        private static string FormatFileSize(long bytes)
        {
            if (bytes >= 1024 * 1024)
                return $"{bytes / (1024d * 1024d):0.##} MB";
            if (bytes >= 1024)
                return $"{bytes / 1024d:0.##} KB";
            return $"{bytes} B";
        }

        private async void KeepLeftButton_Click(object sender, RoutedEventArgs e)
        {
            await KeepCandidateAsync(keepLeft: true);
        }

        private async void KeepRightButton_Click(object sender, RoutedEventArgs e)
        {
            await KeepCandidateAsync(keepLeft: false);
        }

        private void EditLeftButton_Click(object sender, RoutedEventArgs e)
        {
            OpenCandidateForEditing(candidateIndex: 0);
        }

        private void EditRightButton_Click(object sender, RoutedEventArgs e)
        {
            OpenCandidateForEditing(candidateIndex: 1);
        }

        private void OpenCandidateForEditing(int candidateIndex)
        {
            if (openImageForEditing == null ||
                duplicateGroups.Count == 0 ||
                candidateIndex < 0 ||
                candidateIndex >= duplicateGroups[currentGroupIndex].Candidates.Count)
                return;

            DuplicateImageCandidate candidate =
                duplicateGroups[currentGroupIndex].Candidates[candidateIndex];
            if (!openImageForEditing(candidate.FileName))
            {
                CustomMessageBox.Show(
                    GetString(
                        "Duplicate_OpenEditFailed",
                        "Could not open the image for editing."),
                    GetString("Duplicate_Title", "Duplicate Images"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private async void PreviousGroupButton_Click(object sender, RoutedEventArgs e)
        {
            if (currentGroupIndex <= 0)
                return;

            currentGroupIndex--;
            await ShowCurrentPairAsync();
        }

        private async void NextGroupButton_Click(object sender, RoutedEventArgs e)
        {
            if (currentGroupIndex >= duplicateGroups.Count - 1)
                return;

            currentGroupIndex++;
            await ShowCurrentPairAsync();
        }

        private async void DuplicateGroupListBox_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            if (isUpdatingGroupSelection ||
                DuplicateGroupListBox.SelectedItem is not DuplicateGroupListItem selected ||
                selected.GroupIndex == currentGroupIndex)
            {
                return;
            }

            currentGroupIndex = selected.GroupIndex;
            await ShowCurrentPairAsync();
        }

        private async Task KeepCandidateAsync(bool keepLeft)
        {
            if (removeImage == null || duplicateGroups.Count == 0 || isResolvingDuplicate)
                return;

            isResolvingDuplicate = true;
            KeepLeftButton.IsEnabled = false;
            KeepRightButton.IsEnabled = false;

            try
            {
                var group = duplicateGroups[currentGroupIndex];
                int removeIndex = keepLeft ? 1 : 0;
                var candidate = group.Candidates[removeIndex];

                if (!removeImage(candidate.FileName))
                {
                    CustomMessageBox.Show(
                        GetString("Duplicate_RemoveFailed", "The duplicate image could not be removed from the project."),
                        GetString("Duplicate_Title", "Duplicate Images"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                lastImageSignature = CalculateImageSignature();

                group.Candidates.RemoveAt(removeIndex);
                isGroupListDirty = true;
                if (group.Candidates.Count < 2)
                {
                    duplicateGroups.RemoveAt(currentGroupIndex);
                    if (currentGroupIndex >= duplicateGroups.Count)
                        currentGroupIndex = Math.Max(0, duplicateGroups.Count - 1);
                }

                if (duplicateGroups.Count == 0)
                {
                    LeftImage.Clear();
                    RightImage.Clear();
                    FinishEmptyScan("Duplicate_Resolved", "All duplicate images have been resolved.");
                    return;
                }

                await ShowCurrentPairAsync();
            }
            finally
            {
                isResolvingDuplicate = false;
                KeepLeftButton.IsEnabled = true;
                KeepRightButton.IsEnabled = true;
            }
        }

        private void RefreshGroupList()
        {
            isUpdatingGroupSelection = true;
            try
            {
                if (isGroupListDirty || DuplicateGroupListBox.Items.Count != duplicateGroups.Count)
                {
                    DuplicateGroupListBox.Items.Clear();
                    for (int index = 0; index < duplicateGroups.Count; index++)
                    {
                        var group = duplicateGroups[index];
                        var left = group.Candidates[0];
                        var right = group.Candidates[1];
                        int leftLabelCount = labelManager?.GetLabels(left.FileName).Count ?? 0;
                        int rightLabelCount = labelManager?.GetLabels(right.FileName).Count ?? 0;
                        string leftCountText = string.Format(
                            GetString("Duplicate_LabelCount", "{0} label(s)"),
                            leftLabelCount);
                        string rightCountText = string.Format(
                            GetString("Duplicate_LabelCount", "{0} label(s)"),
                            rightLabelCount);
                        string groupTitle = string.Format(
                            GetString("Duplicate_GroupTitle", "Group {0}"),
                            index + 1);
                        string extraCount = string.Format(
                            GetString("Duplicate_ExtraCount", "{0} extra image(s)"),
                            group.Candidates.Count - 1);
                        DuplicateGroupListBox.Items.Add(new DuplicateGroupListItem(
                            index,
                            $"{groupTitle}  |  {extraCount}",
                            left.FullPath,
                            right.FullPath,
                            $"{left.FileName} ({leftLabelCount})",
                            $"{right.FileName} ({rightLabelCount})",
                            $"{left.FileName} - {leftCountText}",
                            $"{right.FileName} - {rightCountText}"));
                    }

                    isGroupListDirty = false;
                }

                DuplicateGroupListBox.SelectedIndex = currentGroupIndex;
                if (DuplicateGroupListBox.SelectedItem != null)
                    DuplicateGroupListBox.ScrollIntoView(DuplicateGroupListBox.SelectedItem);
            }
            finally
            {
                isUpdatingGroupSelection = false;
            }

            PreviousGroupButton.IsEnabled = currentGroupIndex > 0;
            NextGroupButton.IsEnabled = currentGroupIndex < duplicateGroups.Count - 1;
        }

        private int AutoResolveMatchingLabelGroups()
        {
            if (labelManager == null || removeImage == null)
                return 0;

            int removedCount = 0;
            foreach (DuplicateImageGroup group in duplicateGroups.ToArray())
            {
                var candidates = group.Candidates.ToArray();
                for (int keepIndex = 0; keepIndex < candidates.Length; keepIndex++)
                {
                    DuplicateImageCandidate keepCandidate = candidates[keepIndex];
                    if (!group.Candidates.Contains(keepCandidate))
                        continue;

                    List<LabelData> keepLabels = labelManager.GetLabels(keepCandidate.FileName);
                    for (int compareIndex = keepIndex + 1;
                         compareIndex < candidates.Length;
                         compareIndex++)
                    {
                        DuplicateImageCandidate compareCandidate = candidates[compareIndex];
                        if (!group.Candidates.Contains(compareCandidate))
                            continue;

                        List<LabelData> compareLabels = labelManager.GetLabels(compareCandidate.FileName);
                        if (!HaveEquivalentLabels(keepLabels, compareLabels))
                            continue;

                        if (removeImage(compareCandidate.FileName))
                        {
                            group.Candidates.Remove(compareCandidate);
                            removedCount++;
                        }
                    }
                }
            }

            duplicateGroups.RemoveAll(group => group.Candidates.Count < 2);
            return removedCount;
        }

        private static bool HaveEquivalentLabels(
            IReadOnlyCollection<LabelData> leftLabels,
            IReadOnlyCollection<LabelData> rightLabels)
        {
            const double coordinateTolerance = 0.01;
            if (leftLabels.Count != rightLabels.Count)
                return false;

            var left = leftLabels
                .OrderBy(label => label.ClassId)
                .ThenBy(label => label.Rect.X)
                .ThenBy(label => label.Rect.Y)
                .ThenBy(label => label.Rect.Width)
                .ThenBy(label => label.Rect.Height)
                .ToArray();
            var right = rightLabels
                .OrderBy(label => label.ClassId)
                .ThenBy(label => label.Rect.X)
                .ThenBy(label => label.Rect.Y)
                .ThenBy(label => label.Rect.Width)
                .ThenBy(label => label.Rect.Height)
                .ToArray();

            for (int index = 0; index < left.Length; index++)
            {
                if (left[index].ClassId != right[index].ClassId ||
                    Math.Abs(left[index].Rect.X - right[index].Rect.X) > coordinateTolerance ||
                    Math.Abs(left[index].Rect.Y - right[index].Rect.Y) > coordinateTolerance ||
                    Math.Abs(left[index].Rect.Width - right[index].Rect.Width) > coordinateTolerance ||
                    Math.Abs(left[index].Rect.Height - right[index].Rect.Height) > coordinateTolerance)
                {
                    return false;
                }
            }

            return true;
        }

        private static string GetString(string key, string fallback)
        {
            return LanguageManager.Instance.GetString(key) ?? fallback;
        }

        private int CalculateImageSignature()
        {
            if (imageManager == null)
                return 0;

            var hash = new HashCode();
            foreach (var pair in imageManager.ImagePathMap.OrderBy(
                         item => item.Key,
                         StringComparer.OrdinalIgnoreCase))
            {
                hash.Add(pair.Key, StringComparer.OrdinalIgnoreCase);
                hash.Add(pair.Value.FileLength);
                hash.Add(pair.Value.LastWriteTimeUtcTicks);
            }
            return hash.ToHashCode();
        }
    }

    internal sealed record DuplicateGroupListItem(
        int GroupIndex,
        string HeaderText,
        string LeftFullPath,
        string RightFullPath,
        string LeftCaption,
        string RightCaption,
        string LeftToolTip,
        string RightToolTip);
}
