using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace YoableWPF
{
    public sealed class DuplicateLabelPreview : FrameworkElement
    {
        private ImageSource? image;
        private IReadOnlyList<LabelData> labels = Array.Empty<LabelData>();
        private IReadOnlyDictionary<int, LabelClass> classes =
            new Dictionary<int, LabelClass>();

        public DuplicateLabelPreview()
        {
            SnapsToDevicePixels = true;
            UseLayoutRounding = true;
        }

        public void SetContent(
            ImageSource? imageSource,
            IReadOnlyList<LabelData>? imageLabels,
            IReadOnlyList<LabelClass>? projectClasses)
        {
            image = imageSource;
            labels = imageLabels ?? Array.Empty<LabelData>();
            classes = (projectClasses ?? Array.Empty<LabelClass>())
                .GroupBy(item => item.ClassId)
                .ToDictionary(group => group.Key, group => group.First());
            InvalidateVisual();
        }

        public void Clear()
        {
            image = null;
            labels = Array.Empty<LabelData>();
            classes = new Dictionary<int, LabelClass>();
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);
            drawingContext.DrawRectangle(Brushes.Black, null, new Rect(RenderSize));

            if (image == null || image.Width <= 0 || image.Height <= 0 ||
                ActualWidth <= 0 || ActualHeight <= 0)
            {
                return;
            }

            double scale = Math.Min(ActualWidth / image.Width, ActualHeight / image.Height);
            double renderedWidth = image.Width * scale;
            double renderedHeight = image.Height * scale;
            double offsetX = (ActualWidth - renderedWidth) / 2;
            double offsetY = (ActualHeight - renderedHeight) / 2;
            var imageRect = new Rect(offsetX, offsetY, renderedWidth, renderedHeight);

            drawingContext.DrawImage(image, imageRect);
            drawingContext.PushClip(new RectangleGeometry(imageRect));

            var imageBounds = new Rect(0, 0, image.Width, image.Height);
            double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            double lineThickness = Math.Clamp(scale * 2, 1.5, 3.5);

            foreach (var label in labels)
            {
                Rect clippedLabel = Rect.Intersect(label.Rect, imageBounds);
                if (clippedLabel.IsEmpty || clippedLabel.Width <= 0 || clippedLabel.Height <= 0)
                    continue;

                var color = classes.TryGetValue(label.ClassId, out var labelClass)
                    ? labelClass.ColorBrush.Color
                    : Colors.LightCoral;
                color.A = 245;
                var brush = new SolidColorBrush(color);
                brush.Freeze();

                var displayRect = new Rect(
                    offsetX + clippedLabel.X * scale,
                    offsetY + clippedLabel.Y * scale,
                    clippedLabel.Width * scale,
                    clippedLabel.Height * scale);
                drawingContext.DrawRectangle(null, new Pen(brush, lineThickness), displayRect);

                string className = labelClass?.Name ?? $"Class {label.ClassId}";
                var text = new FormattedText(
                    className,
                    CultureInfo.CurrentUICulture,
                    FlowDirection.LeftToRight,
                    new Typeface("Segoe UI Semibold"),
                    11,
                    Brushes.White,
                    pixelsPerDip)
                {
                    MaxTextWidth = Math.Max(1, Math.Min(180, displayRect.Width))
                };

                double textX = displayRect.Left;
                double textY = Math.Max(imageRect.Top, displayRect.Top - text.Height - 3);
                var textBackground = new SolidColorBrush(Color.FromArgb(220, color.R, color.G, color.B));
                textBackground.Freeze();
                drawingContext.DrawRectangle(
                    textBackground,
                    null,
                    new Rect(textX, textY, text.Width + 6, text.Height + 2));
                drawingContext.DrawText(text, new Point(textX + 3, textY + 1));
            }

            drawingContext.Pop();
        }
    }

    public sealed class DuplicateThumbnailConverter : IValueConverter
    {
        public object? Convert(
            object value,
            Type targetType,
            object parameter,
            CultureInfo culture)
        {
            if (value is not string path || !File.Exists(path))
                return null;

            try
            {
                var thumbnail = new BitmapImage();
                thumbnail.BeginInit();
                thumbnail.CacheOption = BitmapCacheOption.OnLoad;
                thumbnail.DecodePixelWidth = 180;
                thumbnail.UriSource = new Uri(path, UriKind.Absolute);
                thumbnail.EndInit();
                thumbnail.Freeze();
                return thumbnail;
            }
            catch
            {
                return null;
            }
        }

        public object ConvertBack(
            object value,
            Type targetType,
            object parameter,
            CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
