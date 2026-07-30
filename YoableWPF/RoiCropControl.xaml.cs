using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using YoableWPF.Managers;

namespace YoableWPF
{
    public sealed record RoiCropPreviewData(
        ImageSource Image,
        Size ImageSize,
        string FileName);

    public partial class RoiCropControl : UserControl
    {
        private const int DefaultOutputSize = 640;
        private Func<Task<RoiCropPreviewData?>>? loadPreview;
        private Func<int, int, RoiCropScope, IProgress<(int current, int total, string fileName)>, CancellationToken, Task<RoiCropBatchResult>>? applyCrop;
        private Func<int, int, IReadOnlyList<string>>? findSmallImages;
        private Func<IReadOnlyCollection<string>, int>? removeImages;
        private CancellationTokenSource? cropCancellation;
        private Size previewImageSize;

        public RoiCropControl()
        {
            InitializeComponent();
        }

        public void Initialize(
            Func<Task<RoiCropPreviewData?>> previewCallback,
            Func<int, int, RoiCropScope, IProgress<(int current, int total, string fileName)>, CancellationToken, Task<RoiCropBatchResult>> applyCallback,
            Func<int, int, IReadOnlyList<string>> findSmallImagesCallback,
            Func<IReadOnlyCollection<string>, int> removeImagesCallback)
        {
            loadPreview = previewCallback;
            applyCrop = applyCallback;
            findSmallImages = findSmallImagesCallback;
            removeImages = removeImagesCallback;
        }

        public async Task RefreshPreviewAsync()
        {
            if (loadPreview == null)
                return;

            var preview = await loadPreview();
            if (preview == null)
            {
                PreviewImage.Source = null;
                PreviewFileNameText.Text = "";
                NoPreviewText.Visibility = Visibility.Visible;
                RoiPreviewBorder.Visibility = Visibility.Collapsed;
                return;
            }

            PreviewImage.Source = preview.Image;
            previewImageSize = preview.ImageSize;
            PreviewFileNameText.Text = preview.FileName;
            NoPreviewText.Visibility = Visibility.Collapsed;
            RoiPreviewBorder.Visibility = Visibility.Visible;
            UpdateRoiPreview();
        }

        private void PreviewHost_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateRoiPreview();
        }

        private void CropSizeTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateRoiPreview();
        }

        private void UpdateRoiPreview()
        {
            if (PreviewHost == null ||
                RoiPreviewBorder == null ||
                previewImageSize.Width <= 0 ||
                previewImageSize.Height <= 0 ||
                !TryGetCropSize(out int cropWidth, out int cropHeight))
            {
                if (RoiPreviewBorder != null)
                    RoiPreviewBorder.Visibility = Visibility.Collapsed;
                return;
            }

            RoiPreviewBorder.Visibility = Visibility.Visible;

            double hostWidth = PreviewHost.ActualWidth;
            double hostHeight = PreviewHost.ActualHeight;
            if (hostWidth <= 0 || hostHeight <= 0)
                return;

            double scale = Math.Min(
                hostWidth / previewImageSize.Width,
                hostHeight / previewImageSize.Height);
            double displayedWidth = previewImageSize.Width * scale;
            double displayedHeight = previewImageSize.Height * scale;
            double offsetX = (hostWidth - displayedWidth) / 2;
            double offsetY = (hostHeight - displayedHeight) / 2;
            double visibleCropWidth = Math.Min(cropWidth, previewImageSize.Width);
            double visibleCropHeight = Math.Min(cropHeight, previewImageSize.Height);

            RoiPreviewBorder.Width = visibleCropWidth * scale;
            RoiPreviewBorder.Height = visibleCropHeight * scale;
            Canvas.SetLeft(
                RoiPreviewBorder,
                offsetX + ((previewImageSize.Width - visibleCropWidth) / 2) * scale);
            Canvas.SetTop(
                RoiPreviewBorder,
                offsetY + ((previewImageSize.Height - visibleCropHeight) / 2) * scale);
        }

        private async void ApplyCropButton_Click(object sender, RoutedEventArgs e)
        {
            if (applyCrop == null || !TryGetCropSize(out int cropWidth, out int cropHeight))
            {
                ShowMessage("Roi_InvalidSize", "Enter a valid ROI width and height.", MessageBoxImage.Warning);
                return;
            }

            var confirmation = CustomMessageBox.Show(
                string.Format(
                    GetString("Roi_Confirm", "Create a centered {0} x {1} ROI crop and update labels?"),
                    cropWidth,
                    cropHeight),
                GetString("Roi_Title", "Center ROI Crop"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (confirmation != MessageBoxResult.Yes)
                return;

            cropCancellation?.Dispose();
            cropCancellation = new CancellationTokenSource();
            ApplyCropButton.IsEnabled = false;
            RemoveSmallImagesButton.IsEnabled = false;
            CancelCropButton.Visibility = Visibility.Visible;
            CropProgressBar.Visibility = Visibility.Visible;
            CropProgressBar.Value = 0;
            StatusText.Text = GetString("Roi_Processing", "Cropping images...");

            var progress = new Progress<(int current, int total, string fileName)>(value =>
            {
                CropProgressBar.Maximum = Math.Max(1, value.total);
                CropProgressBar.Value = value.current;
                StatusText.Text = string.Format(
                    GetString("Roi_Progress", "{0} / {1} - {2}"),
                    value.current,
                    value.total,
                    value.fileName);
            });

            try
            {
                var scope = ScopeComboBox.SelectedIndex == 1
                    ? RoiCropScope.CurrentImage
                    : RoiCropScope.AllImages;
                var result = await applyCrop(
                    cropWidth,
                    cropHeight,
                    scope,
                    progress,
                    cropCancellation.Token);

                StatusText.Text = string.Format(
                    GetString("Roi_Complete", "Cropped {0} image(s), removed {1} label(s), clipped {2} label(s), skipped {3} image(s)."),
                    result.ProcessedItems.Count,
                    result.RemovedLabelCount,
                    result.ClippedLabelCount,
                    result.SkippedImageCount);
                await RefreshPreviewAsync();
            }
            catch (OperationCanceledException)
            {
                StatusText.Text = GetString("Roi_Cancelled", "ROI crop cancelled.");
            }
            catch (InvalidOperationException ex)
            {
                CustomMessageBox.Show(
                    ex.Message,
                    GetString("Roi_Title", "Center ROI Crop"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ROI crop failed: {ex.Message}");
                ShowMessage("Roi_Failed", "ROI crop failed.", MessageBoxImage.Error);
            }
            finally
            {
                ApplyCropButton.IsEnabled = true;
                RemoveSmallImagesButton.IsEnabled = true;
                CancelCropButton.Visibility = Visibility.Collapsed;
                CropProgressBar.Visibility = Visibility.Collapsed;
            }
        }

        private void CancelCropButton_Click(object sender, RoutedEventArgs e)
        {
            cropCancellation?.Cancel();
        }

        private async void RemoveSmallImagesButton_Click(object sender, RoutedEventArgs e)
        {
            if (findSmallImages == null ||
                removeImages == null ||
                !TryGetCropSize(out int cropWidth, out int cropHeight))
            {
                ShowMessage("Roi_InvalidSize", "Enter a valid ROI width and height.", MessageBoxImage.Warning);
                return;
            }

            var candidates = findSmallImages(cropWidth, cropHeight);
            if (candidates.Count == 0)
            {
                CustomMessageBox.Show(
                    string.Format(
                        GetString("Roi_NoSmallImages", "No project images are smaller than the {0} x {1} ROI."),
                        cropWidth,
                        cropHeight),
                    GetString("Roi_Title", "Center ROI Crop"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var confirmation = CustomMessageBox.Show(
                string.Format(
                    GetString(
                        "Roi_RemoveSmallConfirm",
                        "Found {0} image(s) smaller than the {1} x {2} ROI. Remove them and their labels from this project? Original files will remain on disk."),
                    candidates.Count,
                    cropWidth,
                    cropHeight),
                GetString("Roi_Title", "Center ROI Crop"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (confirmation != MessageBoxResult.Yes)
                return;

            RemoveSmallImagesButton.IsEnabled = false;
            ApplyCropButton.IsEnabled = false;
            try
            {
                int removedCount = removeImages(candidates);
                StatusText.Text = string.Format(
                    GetString(
                        "Roi_RemoveSmallComplete",
                        "Removed {0} undersized image(s) and their labels from the project."),
                    removedCount);
                await RefreshPreviewAsync();
            }
            finally
            {
                RemoveSmallImagesButton.IsEnabled = true;
                ApplyCropButton.IsEnabled = true;
            }
        }

        private bool TryGetCropSize(out int width, out int height)
        {
            width = DefaultOutputSize;
            height = DefaultOutputSize;

            return CropWidthTextBox != null &&
                   CropHeightTextBox != null &&
                   int.TryParse(CropWidthTextBox.Text, out width) &&
                   int.TryParse(CropHeightTextBox.Text, out height) &&
                   width > 0 &&
                   height > 0;
        }

        private void ShowMessage(string key, string fallback, MessageBoxImage image)
        {
            CustomMessageBox.Show(
                GetString(key, fallback),
                GetString("Roi_Title", "Center ROI Crop"),
                MessageBoxButton.OK,
                image);
        }

        private static string GetString(string key, string fallback)
        {
            return LanguageManager.Instance.GetString(key) ?? fallback;
        }
    }
}
