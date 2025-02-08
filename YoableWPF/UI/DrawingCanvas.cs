using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace YoableWPF
{
    public class LabelData
    {
        public string Name { get; set; }
        public Rect Rect { get; set; }

        public LabelData(string name, Rect rect)
        {
            Name = name;
            Rect = rect;
        }
    }

    public class DrawingCanvas : Canvas
    {
        public ImageSource Image { get; set; }
        private Size originalImageDimensions;
        public List<LabelData> Labels { get; set; } = new();
        public LabelData SelectedLabel { get; set; }
        public ListBox LabelListBox { get; set; }
        public Rect CurrentRect { get; set; }
        public bool IsDrawing { get; set; }
        public bool IsDragging { get; set; }
        public bool IsResizing { get; set; }
        public Point cursorPosition { get; set; } = new Point(0, 0);
        public Point StartPoint { get; set; }
        public Point DragStart { get; set; }
        public Matrix TransformMatrix { get; set; } = Matrix.Identity;

        private const double resizeHandleSize = 4;

        private double zoomFactor = 1.0;
        private const double zoomStep = 0.1;
        private Point zoomCenter = new Point(0, 0);
        private Matrix transformMatrix = Matrix.Identity;

        private ResizeHandleType resizeHandleType;

        private enum ResizeHandleType
        {
            None, TopLeft, TopRight, BottomLeft, BottomRight,
            Left, Right, Top, Bottom
        }

        public DrawingCanvas()
        {
            Background = Brushes.Transparent;
            ClipToBounds = true;
            Focusable = true;

            MouseWheel += DrawingCanvas_MouseWheel;
            MouseMove += DrawingCanvas_MouseMove;
            MouseUp += DrawingCanvas_MouseUp;
            MouseDown += DrawingCanvas_MouseDown;
            MouseLeave += DrawingCanvas_MouseLeave;
        }

        public void ResetZoom()
        {
            zoomFactor = 1.0;
            transformMatrix = Matrix.Identity;
            RenderTransform = new MatrixTransform(transformMatrix);
            InvalidateVisual(); // Force redraw
        }

        private Rect ScaleRectToCanvas(Rect originalRect)
        {
            if (Image == null) return originalRect;

            double scaleX = ActualWidth / originalImageDimensions.Width;
            double scaleY = ActualHeight / originalImageDimensions.Height;

            return new Rect(
                originalRect.X * scaleX,
                originalRect.Y * scaleY,
                originalRect.Width * scaleX,
                originalRect.Height * scaleY
            );
        }

        private Rect ScaleRectToOriginal(Rect canvasRect)
        {
            if (Image == null) return canvasRect;

            double scaleX = originalImageDimensions.Width / ActualWidth;
            double scaleY = originalImageDimensions.Height / ActualHeight;

            return new Rect(
                canvasRect.X * scaleX,
                canvasRect.Y * scaleY,
                canvasRect.Width * scaleX,
                canvasRect.Height * scaleY
            );
        }

        public void LoadImage(string imagePath, Size originalDimensions)
        {
            if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath)) return;

            BitmapImage bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(imagePath, UriKind.Absolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();

            Image = bitmap;
            originalImageDimensions = originalDimensions;

            Labels.Clear();
            SelectedLabel = null;
            LabelListBox?.Items.Clear();
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);

            if (Image != null)
            {
                dc.DrawImage(Image, new Rect(0, 0, ActualWidth, ActualHeight));
            }

            // Draw existing labels
            var labelBrush = (SolidColorBrush)(new BrushConverter().ConvertFrom(Properties.Settings.Default.LabelColor));
            double labelSize = Properties.Settings.Default.LabelThickness;

            Pen pen = new Pen(labelBrush, labelSize);
            foreach (var label in Labels)
            {
                // Convert stored image coordinates to canvas coordinates for display
                Rect scaledRect = ScaleRectToCanvas(label.Rect);
                dc.DrawRectangle(null, pen, scaledRect);
                if (label == SelectedLabel)
                {
                    DrawResizeHandles(dc, label.Rect);
                }
            }

            // Draw the current rectangle while drawing - use canvas coordinates directly
            if (IsDrawing)
            {
                dc.DrawRectangle(null, pen, CurrentRect);
            }

            if (Properties.Settings.Default.EnableCrosshair)
            {
                DrawCrosshair(dc);
            }
        }

        private void DrawResizeHandles(DrawingContext dc, Rect rect)
        {
            // Convert the rect to canvas coordinates before drawing handles
            Rect scaledRect = ScaleRectToCanvas(rect);

            // Adjust handle size based on zoom factor
            double s = resizeHandleSize * Math.Sqrt(zoomFactor) / zoomFactor;

            // Create handles with adjusted size
            Rect[] handles =
            {
                new Rect(scaledRect.Left - s, scaledRect.Top - s, s * 2, s * 2), // Top Left
                new Rect(scaledRect.Right - s, scaledRect.Top - s, s * 2, s * 2), // Top Right
                new Rect(scaledRect.Left - s, scaledRect.Bottom - s, s * 2, s * 2), // Bottom Left
                new Rect(scaledRect.Right - s, scaledRect.Bottom - s, s * 2, s * 2), // Bottom Right
                new Rect(scaledRect.Left - s, scaledRect.Top + scaledRect.Height / 2 - s, s * 2, s * 2), // Left
                new Rect(scaledRect.Right - s, scaledRect.Top + scaledRect.Height / 2 - s, s * 2, s * 2), // Right
                new Rect(scaledRect.Left + scaledRect.Width / 2 - s, scaledRect.Top - s, s * 2, s * 2), // Top
                new Rect(scaledRect.Left + scaledRect.Width / 2 - s, scaledRect.Bottom - s, s * 2, s * 2) // Bottom
            };

            foreach (var handle in handles)
            {
                dc.DrawRectangle(Brushes.Black, null, handle);
            }
        }

        private void DrawCrosshair(DrawingContext dc)
        {
            if (cursorPosition == new Point(0, 0)) return;

            var colorBrush = (SolidColorBrush)(new BrushConverter().ConvertFrom(Properties.Settings.Default.CrosshairColor));
            double crosshairSize = Properties.Settings.Default.CrosshairSize;

            Pen crosshairPen = new Pen(colorBrush, crosshairSize);
            dc.DrawLine(crosshairPen, new Point(0, cursorPosition.Y), new Point(ActualWidth, cursorPosition.Y));
            dc.DrawLine(crosshairPen, new Point(cursorPosition.X, 0), new Point(cursorPosition.X, ActualHeight));
        }

        private void DrawingCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))
            {
                // Existing zoom functionality
                double oldZoomFactor = zoomFactor;
                zoomCenter = e.GetPosition(this); // Get mouse position relative to the canvas

                if (e.Delta > 0)
                {
                    zoomFactor *= 1 + zoomStep; // Zoom In
                }
                else if (e.Delta < 0)
                {
                    zoomFactor /= 1 + zoomStep; // Zoom Out
                }

                // Prevent zooming out beyond original size
                zoomFactor = Math.Max(1.0, Math.Min(zoomFactor, 5.0));

                // Reset transformation if zoomFactor is back to normal (prevents drifting)
                if (zoomFactor == 1.0)
                {
                    transformMatrix = Matrix.Identity;
                    RenderTransform = new MatrixTransform(transformMatrix);
                    InvalidateVisual();
                    return;
                }

                // Get new mouse position after zoom
                Point newZoomCenter = e.GetPosition(this);

                // Adjust translation smoothly instead of jumping
                double offsetX = newZoomCenter.X - zoomCenter.X;
                double offsetY = newZoomCenter.Y - zoomCenter.Y;

                transformMatrix.Translate(-zoomCenter.X, -zoomCenter.Y);
                transformMatrix.Scale(zoomFactor / oldZoomFactor, zoomFactor / oldZoomFactor);
                transformMatrix.Translate(zoomCenter.X - offsetX, zoomCenter.Y - offsetY);

                RenderTransform = new MatrixTransform(transformMatrix);
                InvalidateVisual();
            }
            else
            {
                // Switch images when CTRL is NOT held
                if (Application.Current.MainWindow is MainWindow mainWindow)
                {
                    ListBox imageListBox = mainWindow.ImageListBox;
                    if (imageListBox.Items.Count == 0) return;

                    int newIndex = imageListBox.SelectedIndex + (e.Delta > 0 ? -1 : 1);

                    if (newIndex >= 0 && newIndex < imageListBox.Items.Count)
                    {
                        imageListBox.SelectedIndex = newIndex;
                        imageListBox.ScrollIntoView(imageListBox.SelectedItem);
                    }
                }
            }

            e.Handled = true; // Prevent default behavior
        }

        private void DrawingCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            cursorPosition = e.GetPosition(this);
            InvalidateVisual();

            if (IsDrawing)
            {
                var canvasRect = new Rect(
                    Math.Min(StartPoint.X, cursorPosition.X),
                    Math.Min(StartPoint.Y, cursorPosition.Y),
                    Math.Abs(cursorPosition.X - StartPoint.X),
                    Math.Abs(cursorPosition.Y - StartPoint.Y)
                );

                CurrentRect = canvasRect;
                return;
            }

            if (SelectedLabel == null) return;

            if (IsResizing)
            {
                ResizeLabel(SelectedLabel, resizeHandleType, e.GetPosition(this));
            }
            else if (IsDragging)
            {
                double dx = cursorPosition.X - DragStart.X;
                double dy = cursorPosition.Y - DragStart.Y;

                // Convert canvas movement to image space
                double scaleX = originalImageDimensions.Width / ActualWidth;
                double scaleY = originalImageDimensions.Height / ActualHeight;

                double imageDx = dx * scaleX;
                double imageDy = dy * scaleY;

                // Update label position in original image coordinates
                SelectedLabel.Rect = new Rect(
                    SelectedLabel.Rect.X + imageDx,
                    SelectedLabel.Rect.Y + imageDy,
                    SelectedLabel.Rect.Width,
                    SelectedLabel.Rect.Height
                );

                DragStart = cursorPosition; // Update start position
            }
        }

        private void DrawingCanvas_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (IsDrawing)
            {
                IsDrawing = false;
                if (CurrentRect.Width > 1 && CurrentRect.Height > 1)
                {
                    // Convert final canvas coordinates to image coordinates for storage
                    double scaleX = originalImageDimensions.Width / ActualWidth;
                    double scaleY = originalImageDimensions.Height / ActualHeight;

                    var imageRect = new Rect(
                        CurrentRect.X * scaleX,
                        CurrentRect.Y * scaleY,
                        CurrentRect.Width * scaleX,
                        CurrentRect.Height * scaleY
                    );

                    var newLabel = new LabelData($"Label {Labels.Count + 1}", imageRect);
                    Labels.Add(newLabel);

                    if (LabelListBox != null)
                    {
                        LabelListBox.Items.Add(newLabel.Name);
                    }

                    if (Application.Current.MainWindow is MainWindow mainWindow)
                    {
                        mainWindow.OnLabelsChanged();
                    }
                }
                CurrentRect = new Rect(0, 0, 0, 0);
            }
            IsDragging = false;
            IsResizing = false;
        }

        private void DrawingCanvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            Point mousePos = e.GetPosition(this);
            bool handled = false;

            // First check resize handles of selected label if any
            if (SelectedLabel != null)
            {
                resizeHandleType = GetResizeHandle(mousePos, SelectedLabel.Rect);
                if (resizeHandleType != ResizeHandleType.None)
                {
                    IsResizing = true;
                    DragStart = mousePos;
                    handled = true;
                    InvalidateVisual();
                    return;
                }
            }

            // If not resizing, check for label selection
            // Iterate through labels in reverse order (newest first)
            for (int i = Labels.Count - 1; i >= 0; i--)
            {
                var label = Labels[i];

                // First check if clicking on resize handles
                resizeHandleType = GetResizeHandle(mousePos, label.Rect);
                if (resizeHandleType != ResizeHandleType.None)
                {
                    SelectedLabel = label;
                    IsResizing = true;
                    DragStart = mousePos;
                    handled = true;
                    break;
                }

                // Then check if clicking on the label itself
                Rect scaledRect = ScaleRectToCanvas(label.Rect);
                if (scaledRect.Contains(mousePos))
                {
                    SelectedLabel = label;
                    IsDragging = true;
                    DragStart = mousePos;
                    handled = true;
                    break;
                }
            }

            // Update ListBox selection to match
            LabelListBox.SelectedItem = SelectedLabel?.Name;

            if (!handled)
            {
                // If we didn't click on any label or resize handle, start drawing
                IsDrawing = true;
                StartPoint = mousePos;
                CurrentRect = new Rect(StartPoint, new Size(0, 0));
                SelectedLabel = null;
            }

            InvalidateVisual();
            Keyboard.Focus(this);
        }

        private void DrawingCanvas_MouseLeave(object sender, MouseEventArgs e)
        {
            if (IsDrawing || IsResizing || IsDragging)
            {
                DrawingCanvas_MouseUp(sender, null);
            }
        }

        private ResizeHandleType GetResizeHandle(Point mousePos, Rect rect)
        {
            // Convert the rect to canvas coordinates for hit testing
            Rect scaledRect = ScaleRectToCanvas(rect);

            // Adjust handle size based on zoom factor but square it
            double s = resizeHandleSize * Math.Sqrt(zoomFactor) / zoomFactor;

            Rect[] handles =
            {
                new Rect(scaledRect.Left - s, scaledRect.Top - s, s * 2, s * 2), // Top Left
                new Rect(scaledRect.Right - s, scaledRect.Top - s, s * 2, s * 2), // Top Right
                new Rect(scaledRect.Left - s, scaledRect.Bottom - s, s * 2, s * 2), // Bottom Left
                new Rect(scaledRect.Right - s, scaledRect.Bottom - s, s * 2, s * 2), // Bottom Right
                new Rect(scaledRect.Left - s, scaledRect.Top + scaledRect.Height / 2 - s, s * 2, s * 2), // Left
                new Rect(scaledRect.Right - s, scaledRect.Top + scaledRect.Height / 2 - s, s * 2, s * 2), // Right
                new Rect(scaledRect.Left + scaledRect.Width / 2 - s, scaledRect.Top - s, s * 2, s * 2), // Top
                new Rect(scaledRect.Left + scaledRect.Width / 2 - s, scaledRect.Bottom - s, s * 2, s * 2) // Bottom
            };

            ResizeHandleType[] types = {
                ResizeHandleType.TopLeft, ResizeHandleType.TopRight,
                ResizeHandleType.BottomLeft, ResizeHandleType.BottomRight,
                ResizeHandleType.Left, ResizeHandleType.Right,
                ResizeHandleType.Top, ResizeHandleType.Bottom
            };

            for (int i = 0; i < handles.Length; i++)
            {
                if (handles[i].Contains(mousePos))
                    return types[i];
            }

            return ResizeHandleType.None;
        }

        private void ResizeLabel(LabelData label, ResizeHandleType handleType, Point mousePos)
        {
            // Convert canvas position to original image space
            Point imagePos = new Point(
                mousePos.X * (originalImageDimensions.Width / ActualWidth),
                mousePos.Y * (originalImageDimensions.Height / ActualHeight)
            );

            double x = label.Rect.X;
            double y = label.Rect.Y;
            double width = label.Rect.Width;
            double height = label.Rect.Height;

            switch (handleType)
            {
                case ResizeHandleType.TopLeft:
                    width += x - imagePos.X;
                    height += y - imagePos.Y;
                    x = imagePos.X;
                    y = imagePos.Y;
                    break;

                case ResizeHandleType.TopRight:
                    width = imagePos.X - x;
                    height += y - imagePos.Y;
                    y = imagePos.Y;
                    break;

                case ResizeHandleType.BottomLeft:
                    width += x - imagePos.X;
                    x = imagePos.X;
                    height = imagePos.Y - y;
                    break;

                case ResizeHandleType.BottomRight:
                    width = imagePos.X - x;
                    height = imagePos.Y - y;
                    break;

                case ResizeHandleType.Left:
                    width += x - imagePos.X;
                    x = imagePos.X;
                    break;

                case ResizeHandleType.Right:
                    width = imagePos.X - x;
                    break;

                case ResizeHandleType.Top:
                    height += y - imagePos.Y;
                    y = imagePos.Y;
                    break;

                case ResizeHandleType.Bottom:
                    height = imagePos.Y - y;
                    break;
            }

            // Ensure minimum size
            label.Rect = new Rect(x, y, Math.Max(1, width), Math.Max(1, height));
        }

        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            Point mousePos = e.GetPosition(this);

            bool clickedOnLabel = false;
            foreach (var label in Labels)
            {
                // Convert the rect to canvas coordinates for hit testing
                Rect scaledRect = ScaleRectToCanvas(label.Rect);
                if (scaledRect.Contains(mousePos))
                {
                    SelectedLabel = label;
                    clickedOnLabel = true;
                    break;
                }
            }

            if (!clickedOnLabel)
            {
                SelectedLabel = null;
                LabelListBox.SelectedItem = null;
            }

            InvalidateVisual();
            Keyboard.Focus(this);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            if (SelectedLabel != null && Labels.Contains(SelectedLabel))
            {
                int moveAmount = 1; // Amount of pixels to move

                switch (e.Key)
                {
                    case Key.Up:
                        SelectedLabel.Rect = new Rect(SelectedLabel.Rect.X, SelectedLabel.Rect.Y - moveAmount, SelectedLabel.Rect.Width, SelectedLabel.Rect.Height);
                        break;

                    case Key.Down:
                        SelectedLabel.Rect = new Rect(SelectedLabel.Rect.X, SelectedLabel.Rect.Y + moveAmount, SelectedLabel.Rect.Width, SelectedLabel.Rect.Height);
                        break;

                    case Key.Left:
                        SelectedLabel.Rect = new Rect(SelectedLabel.Rect.X - moveAmount, SelectedLabel.Rect.Y, SelectedLabel.Rect.Width, SelectedLabel.Rect.Height);
                        break;

                    case Key.Right:
                        SelectedLabel.Rect = new Rect(SelectedLabel.Rect.X + moveAmount, SelectedLabel.Rect.Y, SelectedLabel.Rect.Width, SelectedLabel.Rect.Height);
                        break;

                    case Key.Delete:
                        Labels.Remove(SelectedLabel);
                        if (LabelListBox != null)
                        {
                            LabelListBox.Items.Remove(SelectedLabel.Name);
                        }
                        SelectedLabel = null;
                        break;
                }

                InvalidateVisual(); // Refresh canvas
                e.Handled = true; // Prevent further event propagation
            }
        }
    }
}