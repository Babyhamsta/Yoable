using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Linq;

namespace YoableWPF
{
    public class LabelData
    {
        public string Name { get; set; }
        public Rect Rect { get; set; }
        public int ClassId { get; set; } = 0; // Default class

        public LabelData(string name, Rect rect, int classId = 0)
        {
            Name = name;
            Rect = rect;
            ClassId = classId;
        }

        // Deep copy constructor
        public LabelData(LabelData source)
        {
            Name = source.Name;
            Rect = source.Rect;
            ClassId = source.ClassId;
        }
    }


    public class DrawingCanvas : Canvas
    {
        // Event fired whenever labels are modified
        public event EventHandler LabelsChanged;

        public ImageSource Image { get; set; }
        private Size originalImageDimensions;
        public List<LabelData> Labels { get; set; } = new();
        public LabelData SelectedLabel { get; set; }
        // Class management
        public int CurrentClassId { get; set; } = 0;
        public event EventHandler<int> CurrentClassChanged;
        private List<LabelClass> availableClasses = new List<LabelClass>();

        public void SetAvailableClasses(List<LabelClass> classes)
        {
            availableClasses = classes ?? new List<LabelClass>();

            // If current class doesn't exist in new list, switch to first class
            if (availableClasses.Any() && !availableClasses.Any(c => c.ClassId == CurrentClassId))
            {
                CurrentClassId = availableClasses.First().ClassId;
                OnCurrentClassChanged();
            }
        }

        public string GetCurrentClassColor()
        {
            var currentClass = availableClasses.FirstOrDefault(c => c.ClassId == CurrentClassId);
            return currentClass?.ColorHex ?? "#E57373"; // Fallback to red
        }

        protected virtual void OnCurrentClassChanged()
        {
            CurrentClassChanged?.Invoke(this, CurrentClassId);
            InvalidateVisual(); // Redraw to show new color while drawing
        }

        // Multi-selection support
        public HashSet<LabelData> SelectedLabels { get; set; } = new();

        // Copy/Paste support
        private static List<LabelData> clipboard = new();

        // Undo/Redo support
        private Stack<List<LabelData>> undoStack = new();
        private Stack<List<LabelData>> redoStack = new();
        private const int MaxUndoSteps = 20;

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
        private bool hasMovedOrResized = false; // Track if actual movement occurred

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

            // Use PreviewKeyDown to catch events before they're handled elsewhere
            PreviewKeyDown += DrawingCanvas_PreviewKeyDown;
        }

        /// <summary>
        /// Notifies listeners that labels have been modified
        /// </summary>
        protected virtual void OnLabelsChanged()
        {
            LabelsChanged?.Invoke(this, EventArgs.Empty);
        }

        // Save current state for undo
        private void SaveUndoState()
        {
            var state = Labels.Select(l => new LabelData(l)).ToList();
            undoStack.Push(state);

            // Limit undo stack size
            if (undoStack.Count > MaxUndoSteps)
            {
                var items = undoStack.ToArray();
                undoStack.Clear();
                foreach (var item in items.Take(MaxUndoSteps).Reverse())
                {
                    undoStack.Push(item);
                }
            }

            // Clear redo stack when new action is performed
            redoStack.Clear();
        }

        // Perform undo
        private void Undo()
        {
            if (undoStack.Count > 0)
            {
                // Save current state to redo stack
                var currentState = Labels.Select(l => new LabelData(l)).ToList();
                redoStack.Push(currentState);

                // Restore previous state
                var previousState = undoStack.Pop();
                Labels.Clear();
                Labels.AddRange(previousState);

                // Update UI
                RefreshLabelListBox();
                SelectedLabel = null;
                SelectedLabels.Clear();
                InvalidateVisual();

                OnLabelsChanged();
            }
        }

        // Perform redo
        private void Redo()
        {
            if (redoStack.Count > 0)
            {
                // Save current state to undo stack
                var currentState = Labels.Select(l => new LabelData(l)).ToList();
                undoStack.Push(currentState);

                // Restore next state
                var nextState = redoStack.Pop();
                Labels.Clear();
                Labels.AddRange(nextState);

                // Update UI
                RefreshLabelListBox();
                SelectedLabel = null;
                SelectedLabels.Clear();
                InvalidateVisual();

                OnLabelsChanged();
            }
        }

        // Copy selected labels
        private void CopyLabels()
        {
            clipboard.Clear();

            if (SelectedLabels.Count > 0)
            {
                // Copy all selected labels
                foreach (var label in SelectedLabels)
                {
                    clipboard.Add(new LabelData(label));
                }

                // Optional: Show feedback
                if (Application.Current.MainWindow is MainWindow mainWindow)
                {
                    mainWindow.Title = $"YoableWPF - Copied {clipboard.Count} label(s)";
                }
            }
            else if (SelectedLabel != null)
            {
                // Copy single selected label
                clipboard.Add(new LabelData(SelectedLabel));

                // Optional: Show feedback
                if (Application.Current.MainWindow is MainWindow mainWindow)
                {
                    mainWindow.Title = "YoableWPF - Copied 1 label";
                }
            }
        }

        // Paste labels
        private void PasteLabels()
        {
            if (clipboard.Count == 0) return;

            SaveUndoState();

            // Clear current selection
            SelectedLabels.Clear();

            // Calculate offset for pasted labels (slight offset from original position)
            double offsetX = 20;
            double offsetY = 20;

            foreach (var clipboardLabel in clipboard)
            {
                // Create new label with unique name
                var newLabel = new LabelData($"Label {Labels.Count + 1}",
                    new Rect(
                        clipboardLabel.Rect.X + offsetX,
                        clipboardLabel.Rect.Y + offsetY,
                        clipboardLabel.Rect.Width,
                        clipboardLabel.Rect.Height
                    ),
                    CurrentClassId); // Use current class

                Labels.Add(newLabel);
                SelectedLabels.Add(newLabel);
            }

            // Update UI
            RefreshLabelListBox();
            InvalidateVisual();

            // Notify that labels changed
            OnLabelsChanged();

            // Update title
            if (Application.Current.MainWindow is MainWindow mainWindow)
            {
                mainWindow.Title = $"YoableWPF - Pasted {clipboard.Count} label(s)";
            }
        }

        // Refresh label list box
        private void RefreshLabelListBox()
        {
            if (LabelListBox != null)
            {
                LabelListBox.Items.Clear();
                foreach (var label in Labels)
                {
                    LabelListBox.Items.Add(label.Name);
                }
            }
        }

        public void ResetZoom()
        {
            zoomFactor = 1.0;
            transformMatrix = Matrix.Identity;
            RenderTransform = new MatrixTransform(transformMatrix);
            InvalidateVisual();
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
            SelectedLabels.Clear();
            undoStack.Clear();
            redoStack.Clear();
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

            // Draw existing labels with their class colors
            double thickness = Properties.Settings.Default.LabelThickness;
            
            foreach (var label in Labels)
            {
                // Get color for this label's class
                var labelClass = availableClasses.FirstOrDefault(c => c.ClassId == label.ClassId);
                string colorHex = labelClass?.ColorHex ?? "#E57373"; // Fallback

                var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(colorHex);
                
                // Check if this label is selected
                bool isSelected = SelectedLabels.Contains(label) || label == SelectedLabel;
                
                // FIXED: For selected labels, brighten the color instead of using yellow
                if (isSelected)
                {
                    // Brighten the color by increasing RGB values
                    color.R = (byte)Math.Min(255, color.R + 60);
                    color.G = (byte)Math.Min(255, color.G + 60);
                    color.B = (byte)Math.Min(255, color.B + 60);
                }
                
                var brush = new SolidColorBrush(color);
                var pen = new Pen(brush, isSelected ? thickness + 1 : thickness);

                // Convert stored image coordinates to canvas coordinates for display
                Rect scaledRect = ScaleRectToCanvas(label.Rect);

                // Draw the rectangle
                dc.DrawRectangle(null, pen, scaledRect);

                if (isSelected)
                {
                    DrawResizeHandles(dc, label.Rect);
                }
            }

            // Draw the current rectangle being drawn with current class color
            if (IsDrawing)
            {
                string currentColorHex = GetCurrentClassColor();
                var currentBrush = new SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(currentColorHex));
                var currentPen = new Pen(currentBrush, thickness);
                dc.DrawRectangle(null, currentPen, CurrentRect);
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
            // PRIORITY 1: If shift is held and labels are selected, cycle through classes
            if ((Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift)) && 
                (SelectedLabel != null || SelectedLabels.Count > 0) && 
                availableClasses.Count > 1)
            {
                // Get the current class of the selected label
                int currentClassId = SelectedLabel?.ClassId ?? SelectedLabels.FirstOrDefault()?.ClassId ?? CurrentClassId;
                int currentIndex = availableClasses.FindIndex(c => c.ClassId == currentClassId);
                
                if (currentIndex < 0) currentIndex = 0;

                if (e.Delta > 0) // Scroll up - previous class
                {
                    currentIndex = currentIndex <= 0 ? availableClasses.Count - 1 : currentIndex - 1;
                }
                else // Scroll down - next class
                {
                    currentIndex = currentIndex >= availableClasses.Count - 1 ? 0 : currentIndex + 1;
                }

                SaveUndoState();
                var newClassId = availableClasses[currentIndex].ClassId;
                
                // Update all selected labels
                if (SelectedLabels.Count > 0)
                {
                    foreach (var label in SelectedLabels)
                    {
                        label.ClassId = newClassId;
                    }
                }
                else if (SelectedLabel != null)
                {
                    SelectedLabel.ClassId = newClassId;
                }
                
                // Refresh UI to show new class colors
                if (Application.Current.MainWindow is MainWindow mainWindow)
                {
                    mainWindow.RefreshLabelListFromCanvas();
                }
                
                OnLabelsChanged();
                InvalidateVisual();
                e.Handled = true;
                return;
            }

            // PRIORITY 2: If we're currently drawing, cycle through classes
            if (IsDrawing && availableClasses.Count > 1)
            {
                int currentIndex = availableClasses.FindIndex(c => c.ClassId == CurrentClassId);

                if (e.Delta > 0) // Scroll up - previous class
                {
                    currentIndex = currentIndex <= 0 ? availableClasses.Count - 1 : currentIndex - 1;
                }
                else // Scroll down - next class
                {
                    currentIndex = currentIndex >= availableClasses.Count - 1 ? 0 : currentIndex + 1;
                }

                CurrentClassId = availableClasses[currentIndex].ClassId;
                OnCurrentClassChanged();
                e.Handled = true;
                return;
            }

            if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))
            {
                // Existing zoom functionality
                double oldZoomFactor = zoomFactor;
                zoomCenter = e.GetPosition(this);

                if (e.Delta > 0)
                {
                    zoomFactor *= 1 + zoomStep;
                }
                else if (e.Delta < 0)
                {
                    zoomFactor /= 1 + zoomStep;
                }

                zoomFactor = Math.Max(1.0, Math.Min(zoomFactor, 5.0));

                if (zoomFactor == 1.0)
                {
                    transformMatrix = Matrix.Identity;
                    RenderTransform = new MatrixTransform(transformMatrix);
                    InvalidateVisual();
                    return;
                }

                Point newZoomCenter = e.GetPosition(this);
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

            e.Handled = true;
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

            if (SelectedLabel == null && SelectedLabels.Count == 0) return;

            if (IsResizing && SelectedLabel != null)
            {
                // Save undo state on first movement
                if (!hasMovedOrResized)
                {
                    SaveUndoState();
                    hasMovedOrResized = true;
                }
                ResizeLabel(SelectedLabel, resizeHandleType, e.GetPosition(this));
            }
            else if (IsDragging)
            {
                double dx = cursorPosition.X - DragStart.X;
                double dy = cursorPosition.Y - DragStart.Y;

                // Only process if there's actual movement
                if (Math.Abs(dx) > 0.1 || Math.Abs(dy) > 0.1)
                {
                    // Save undo state on first movement
                    if (!hasMovedOrResized)
                    {
                        SaveUndoState();
                        hasMovedOrResized = true;
                    }

                    // Convert canvas movement to image space
                    double scaleX = originalImageDimensions.Width / ActualWidth;
                    double scaleY = originalImageDimensions.Height / ActualHeight;

                    double imageDx = dx * scaleX;
                    double imageDy = dy * scaleY;

                    // Move all selected labels
                    if (SelectedLabels.Count > 0)
                    {
                        foreach (var label in SelectedLabels)
                        {
                            label.Rect = new Rect(
                                label.Rect.X + imageDx,
                                label.Rect.Y + imageDy,
                                label.Rect.Width,
                                label.Rect.Height
                            );
                        }
                    }
                    else if (SelectedLabel != null)
                    {
                        SelectedLabel.Rect = new Rect(
                            SelectedLabel.Rect.X + imageDx,
                            SelectedLabel.Rect.Y + imageDy,
                            SelectedLabel.Rect.Width,
                            SelectedLabel.Rect.Height
                        );
                    }

                    DragStart = cursorPosition;
                }
            }
        }

        private void DrawingCanvas_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (IsDrawing)
            {
                IsDrawing = false;
                if (CurrentRect.Width > 1 && CurrentRect.Height > 1)
                {
                    SaveUndoState();

                    // Convert final canvas coordinates to image coordinates for storage
                    double scaleX = originalImageDimensions.Width / ActualWidth;
                    double scaleY = originalImageDimensions.Height / ActualHeight;

                    var imageRect = new Rect(
                        CurrentRect.X * scaleX,
                        CurrentRect.Y * scaleY,
                        CurrentRect.Width * scaleX,
                        CurrentRect.Height * scaleY
                    );

                    var newLabel = new LabelData($"Label {Labels.Count + 1}", imageRect, CurrentClassId);
                    Labels.Add(newLabel);

                    // FIXED: Use proper UI refresh that includes class colors
                    if (Application.Current.MainWindow is MainWindow mainWindow)
                    {
                        mainWindow.RefreshLabelListFromCanvas();
                    }

                    OnLabelsChanged();
                }
                CurrentRect = new Rect(0, 0, 0, 0);
            }

            // Only call OnLabelsChanged if we actually moved or resized something
            if ((IsDragging || IsResizing) && hasMovedOrResized)
            {
                OnLabelsChanged();
            }

            IsDragging = false;
            IsResizing = false;
            hasMovedOrResized = false;
        }

        private void DrawingCanvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            // Ensure this canvas has keyboard focus
            Focus();
            Keyboard.Focus(this);

            Point mousePos = e.GetPosition(this);
            bool handled = false;
            bool ctrlPressed = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);

            // Reset movement flag
            hasMovedOrResized = false;

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
            for (int i = Labels.Count - 1; i >= 0; i--)
            {
                var label = Labels[i];

                // First check if clicking on resize handles
                resizeHandleType = GetResizeHandle(mousePos, label.Rect);
                if (resizeHandleType != ResizeHandleType.None)
                {
                    SelectedLabel = label;
                    if (!ctrlPressed)
                    {
                        SelectedLabels.Clear();
                    }
                    SelectedLabels.Add(label);
                    IsResizing = true;
                    DragStart = mousePos;
                    handled = true;
                    break;
                }

                // Then check if clicking on the label itself
                Rect scaledRect = ScaleRectToCanvas(label.Rect);
                if (scaledRect.Contains(mousePos))
                {
                    if (ctrlPressed)
                    {
                        // Toggle selection
                        if (SelectedLabels.Contains(label))
                        {
                            SelectedLabels.Remove(label);
                            SelectedLabel = SelectedLabels.FirstOrDefault();
                        }
                        else
                        {
                            SelectedLabels.Add(label);
                            SelectedLabel = label;
                        }
                    }
                    else
                    {
                        // Single selection
                        SelectedLabels.Clear();
                        SelectedLabels.Add(label);
                        SelectedLabel = label;
                    }

                    IsDragging = true;
                    DragStart = mousePos;
                    handled = true;
                    break;
                }
            }

            // Update ListBox selection to match
            if (!ctrlPressed && SelectedLabel != null)
            {
                // For single selection, update the ListBox
                LabelListBox.SelectedItem = SelectedLabel.Name;
            }
            else if (ctrlPressed && SelectedLabels.Count == 1 && SelectedLabel != null)
            {
                // If only one label is selected after Ctrl+click, update ListBox
                LabelListBox.SelectedItem = SelectedLabel.Name;
            }
            else if (SelectedLabels.Count == 0 || SelectedLabels.Count > 1)
            {
                // Clear ListBox selection for no selection or multi-selection
                LabelListBox.SelectedItem = null;
            }

            if (!handled)
            {
                // If we didn't click on any label or resize handle, start drawing
                IsDrawing = true;
                StartPoint = mousePos;
                CurrentRect = new Rect(StartPoint, new Size(0, 0));
                SelectedLabel = null;
                SelectedLabels.Clear();
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

            // Adjust handle size based on zoom factor
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

                    // Clear multi-selection and add only this label
                    SelectedLabels.Clear();
                    SelectedLabels.Add(label);

                    clickedOnLabel = true;
                    break;
                }
            }

            if (!clickedOnLabel)
            {
                SelectedLabel = null;
                SelectedLabels.Clear();
                LabelListBox.SelectedItem = null;
            }

            InvalidateVisual();
            Keyboard.Focus(this);
        }

        private void DrawingCanvas_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Handle copy/paste/undo/redo shortcuts
            if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))
            {
                switch (e.Key)
                {
                    case Key.C:
                        CopyLabels();
                        e.Handled = true;
                        return;

                    case Key.V:
                        PasteLabels();
                        e.Handled = true;
                        return;

                    case Key.Z:
                        Undo();
                        e.Handled = true;
                        return;

                    case Key.Y:
                        Redo();
                        e.Handled = true;
                        return;

                    case Key.A:
                        // Select all
                        SelectedLabels.Clear();
                        foreach (var label in Labels)
                        {
                            SelectedLabels.Add(label);
                        }
                        SelectedLabel = Labels.FirstOrDefault();
                        InvalidateVisual();
                        e.Handled = true;
                        return;
                }
            }

            // Handle movement and deletion for selected labels
            if ((SelectedLabel != null && Labels.Contains(SelectedLabel)) || SelectedLabels.Count > 0)
            {
                int moveAmount = 1; // Amount of pixels to move
                bool needsUndo = false;

                switch (e.Key)
                {
                    case Key.Up:
                    case Key.Down:
                    case Key.Left:
                    case Key.Right:
                        // Only save undo state once per movement action
                        if (!e.IsRepeat)
                        {
                            SaveUndoState();
                        }
                        needsUndo = true;
                        break;
                }

                switch (e.Key)
                {
                    case Key.Up:
                        if (SelectedLabels.Count > 0)
                        {
                            foreach (var label in SelectedLabels)
                            {
                                label.Rect = new Rect(label.Rect.X, label.Rect.Y - moveAmount, label.Rect.Width, label.Rect.Height);
                            }
                        }
                        else if (SelectedLabel != null)
                        {
                            SelectedLabel.Rect = new Rect(SelectedLabel.Rect.X, SelectedLabel.Rect.Y - moveAmount, SelectedLabel.Rect.Width, SelectedLabel.Rect.Height);
                        }
                        break;

                    case Key.Down:
                        if (SelectedLabels.Count > 0)
                        {
                            foreach (var label in SelectedLabels)
                            {
                                label.Rect = new Rect(label.Rect.X, label.Rect.Y + moveAmount, label.Rect.Width, label.Rect.Height);
                            }
                        }
                        else if (SelectedLabel != null)
                        {
                            SelectedLabel.Rect = new Rect(SelectedLabel.Rect.X, SelectedLabel.Rect.Y + moveAmount, SelectedLabel.Rect.Width, SelectedLabel.Rect.Height);
                        }
                        break;

                    case Key.Left:
                        if (SelectedLabels.Count > 0)
                        {
                            foreach (var label in SelectedLabels)
                            {
                                label.Rect = new Rect(label.Rect.X - moveAmount, label.Rect.Y, label.Rect.Width, label.Rect.Height);
                            }
                        }
                        else if (SelectedLabel != null)
                        {
                            SelectedLabel.Rect = new Rect(SelectedLabel.Rect.X - moveAmount, SelectedLabel.Rect.Y, SelectedLabel.Rect.Width, SelectedLabel.Rect.Height);
                        }
                        break;

                    case Key.Right:
                        if (SelectedLabels.Count > 0)
                        {
                            foreach (var label in SelectedLabels)
                            {
                                label.Rect = new Rect(label.Rect.X + moveAmount, label.Rect.Y, label.Rect.Width, label.Rect.Height);
                            }
                        }
                        else if (SelectedLabel != null)
                        {
                            SelectedLabel.Rect = new Rect(SelectedLabel.Rect.X + moveAmount, SelectedLabel.Rect.Y, SelectedLabel.Rect.Width, SelectedLabel.Rect.Height);
                        }
                        break;

                    case Key.Delete:
                        SaveUndoState();
                        if (SelectedLabels.Count > 0)
                        {
                            // Create a copy of the collection to avoid modification during iteration
                            var labelsToDelete = SelectedLabels.ToList();
                            foreach (var label in labelsToDelete)
                            {
                                Labels.Remove(label);
                            }
                            SelectedLabels.Clear();
                        }
                        else if (SelectedLabel != null)
                        {
                            Labels.Remove(SelectedLabel);
                        }
                        SelectedLabel = null;

                        // FIXED: Refresh the UI properly
                        if (Application.Current.MainWindow is MainWindow mainWindow)
                        {
                            mainWindow.RefreshLabelListFromCanvas();
                        }

                        OnLabelsChanged();
                        break;
                }

                if (needsUndo)
                {
                    OnLabelsChanged();
                }

                InvalidateVisual();
                e.Handled = true;
            }
        }
    }
}
