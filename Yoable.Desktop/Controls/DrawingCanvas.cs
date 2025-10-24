using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Yoable.Models;

namespace Yoable.Desktop.Controls;

/// <summary>
/// Avalonia custom canvas control for image display and bounding box labeling
/// </summary>
public class DrawingCanvas : Control
{
    // Event fired whenever labels are modified
    public event EventHandler? LabelsChanged;

    // Image properties
    public Bitmap? Image { get; set; }
    private Size _originalImageDimensions;

    // Label data
    public List<LabelData> Labels { get; set; } = new();
    public LabelData? SelectedLabel { get; set; }

    // Multi-selection support
    public HashSet<LabelData> SelectedLabels { get; set; } = new();

    // Copy/Paste support
    private static List<LabelData> _clipboard = new();

    // Undo/Redo support
    private Stack<List<LabelData>> _undoStack = new();
    private Stack<List<LabelData>> _redoStack = new();
    private const int MaxUndoSteps = 20;

    // Reference to label ListBox for UI updates
    public ListBox? LabelListBox { get; set; }

    // Drawing state
    public LabelRect CurrentRect { get; set; }
    public bool IsDrawing { get; set; }
    public bool IsDragging { get; set; }
    public bool IsResizing { get; set; }
    public Point CursorPosition { get; set; } = new Point(0, 0);
    public Point StartPoint { get; set; }
    public Point DragStart { get; set; }

    // Zoom and pan
    private double _zoomFactor = 1.0;
    private const double ZoomStep = 0.1;
    private Point _zoomCenter = new Point(0, 0);
    private Matrix _transformMatrix = Matrix.Identity;

    // Resize handling
    private const double ResizeHandleSize = 8;
    private ResizeHandleType _resizeHandleType;
    private bool _hasMovedOrResized = false;

    private enum ResizeHandleType
    {
        None, TopLeft, TopRight, BottomLeft, BottomRight,
        Left, Right, Top, Bottom
    }

    // Brushes and pens (Avalonia uses immutable objects)
    private readonly IBrush _boxBrush = new SolidColorBrush(Color.FromArgb(100, 0, 255, 0));
    private readonly IPen _boxPen = new Pen(Brushes.LimeGreen, 2);
    private readonly IPen _selectedBoxPen = new Pen(Brushes.Yellow, 3);
    private readonly IBrush _handleBrush = Brushes.White;
    private readonly IPen _handlePen = new Pen(Brushes.Black, 1);
    private readonly IBrush _textBackground = new SolidColorBrush(Color.FromArgb(200, 0, 0, 0));
    private readonly IBrush _textForeground = Brushes.White;

    public DrawingCanvas()
    {
        // Note: Control doesn't have Background property, use rendering instead
        ClipToBounds = true;
        Focusable = true;

        // Subscribe to pointer events
        PointerWheelChanged += DrawingCanvas_PointerWheelChanged;
        PointerMoved += DrawingCanvas_PointerMoved;
        PointerPressed += DrawingCanvas_PointerPressed;
        PointerReleased += DrawingCanvas_PointerReleased;
        PointerExited += DrawingCanvas_PointerExited;

        // Subscribe to key events
        KeyDown += DrawingCanvas_KeyDown;
    }

    #region Undo/Redo/Copy/Paste

    private void SaveUndoState()
    {
        var state = Labels.Select(l => new LabelData(l)).ToList();
        _undoStack.Push(state);

        // Limit undo stack size
        if (_undoStack.Count > MaxUndoSteps)
        {
            var items = _undoStack.ToArray();
            _undoStack.Clear();
            foreach (var item in items.Take(MaxUndoSteps))
            {
                _undoStack.Push(item);
            }
        }

        // Clear redo stack when new action is performed
        _redoStack.Clear();
    }

    private void Undo()
    {
        if (_undoStack.Count > 0)
        {
            // Save current state to redo stack
            var currentState = Labels.Select(l => new LabelData(l)).ToList();
            _redoStack.Push(currentState);

            // Restore previous state
            var previousState = _undoStack.Pop();
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

    private void Redo()
    {
        if (_redoStack.Count > 0)
        {
            // Save current state to undo stack
            var currentState = Labels.Select(l => new LabelData(l)).ToList();
            _undoStack.Push(currentState);

            // Restore next state
            var nextState = _redoStack.Pop();
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

    private void CopyLabels()
    {
        _clipboard.Clear();

        if (SelectedLabels.Count > 0)
        {
            foreach (var label in SelectedLabels)
            {
                _clipboard.Add(new LabelData(label));
            }
        }
        else if (SelectedLabel != null)
        {
            _clipboard.Add(new LabelData(SelectedLabel));
        }
    }

    private void PasteLabels()
    {
        if (_clipboard.Count == 0) return;

        SaveUndoState();

        SelectedLabels.Clear();

        double offsetX = 20;
        double offsetY = 20;

        foreach (var clipboardLabel in _clipboard)
        {
            var newLabel = new LabelData(
                $"Label {Labels.Count + 1}",
                new LabelRect(
                    clipboardLabel.Rect.X + offsetX,
                    clipboardLabel.Rect.Y + offsetY,
                    clipboardLabel.Rect.Width,
                    clipboardLabel.Rect.Height
                ));

            Labels.Add(newLabel);
            SelectedLabels.Add(newLabel);
        }

        RefreshLabelListBox();
        InvalidateVisual();
        OnLabelsChanged();
    }

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

    #endregion

    #region Image Loading

    public void LoadImage(string imagePath, Size originalDimensions)
    {
        try
        {
            if (!File.Exists(imagePath))
                return;

            Image = new Bitmap(imagePath);
            _originalImageDimensions = originalDimensions;

            InvalidateVisual();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading image: {ex.Message}");
        }
    }

    public void ResetZoom()
    {
        _zoomFactor = 1.0;
        _transformMatrix = Matrix.Identity;
        InvalidateVisual();
    }

    #endregion

    #region Rendering

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        // Apply transform for zoom/pan
        using (context.PushTransform(_transformMatrix))
        {
            // Draw image
            if (Image != null)
            {
                var imageRect = new Rect(0, 0, Image.PixelSize.Width, Image.PixelSize.Height);
                context.DrawImage(Image, imageRect);
            }

            // Draw all labels
            foreach (var label in Labels)
            {
                bool isSelected = label == SelectedLabel || SelectedLabels.Contains(label);
                DrawLabel(context, label, isSelected);
            }

            // Draw current drawing rectangle
            if (IsDrawing)
            {
                DrawCurrentRect(context);
            }
        }
    }

    private void DrawLabel(DrawingContext context, LabelData label, bool isSelected)
    {
        var rect = new Rect(label.Rect.X, label.Rect.Y, label.Rect.Width, label.Rect.Height);

        // Draw filled rectangle
        context.FillRectangle(_boxBrush, rect);

        // Draw border
        context.DrawRectangle(isSelected ? _selectedBoxPen : _boxPen, rect);

        // Draw resize handles if selected
        if (isSelected)
        {
            DrawResizeHandles(context, rect);
        }

        // Draw label text
        DrawLabelText(context, label.Name, rect);
    }

    private void DrawResizeHandles(DrawingContext context, Rect rect)
    {
        double handleSize = ResizeHandleSize;

        // Corner handles
        DrawHandle(context, new Point(rect.Left, rect.Top)); // Top-left
        DrawHandle(context, new Point(rect.Right, rect.Top)); // Top-right
        DrawHandle(context, new Point(rect.Left, rect.Bottom)); // Bottom-left
        DrawHandle(context, new Point(rect.Right, rect.Bottom)); // Bottom-right

        // Edge handles
        DrawHandle(context, new Point(rect.Left, rect.Top + rect.Height / 2)); // Left
        DrawHandle(context, new Point(rect.Right, rect.Top + rect.Height / 2)); // Right
        DrawHandle(context, new Point(rect.Left + rect.Width / 2, rect.Top)); // Top
        DrawHandle(context, new Point(rect.Left + rect.Width / 2, rect.Bottom)); // Bottom
    }

    private void DrawHandle(DrawingContext context, Point center)
    {
        double halfSize = ResizeHandleSize / 2;
        var handleRect = new Rect(center.X - halfSize, center.Y - halfSize, ResizeHandleSize, ResizeHandleSize);
        context.FillRectangle(_handleBrush, handleRect);
        context.DrawRectangle(_handlePen, handleRect);
    }

    private void DrawLabelText(DrawingContext context, string text, Rect rect)
    {
        var typeface = new Typeface("Arial");
        var formattedText = new FormattedText(
            text,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            12,
            _textForeground);

        // Draw background for text
        var textRect = new Rect(rect.Left, rect.Top - 20, formattedText.Width + 4, formattedText.Height + 2);
        context.FillRectangle(_textBackground, textRect);

        // Draw text
        context.DrawText(formattedText, new Point(rect.Left + 2, rect.Top - 18));
    }

    private void DrawCurrentRect(DrawingContext context)
    {
        var rect = new Rect(CurrentRect.X, CurrentRect.Y, CurrentRect.Width, CurrentRect.Height);
        context.FillRectangle(_boxBrush, rect);
        context.DrawRectangle(_boxPen, rect);
    }

    #endregion

    #region Mouse/Pointer Handling

    private void DrawingCanvas_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        // Zoom with mouse wheel
        var delta = e.Delta.Y;

        if (delta > 0)
        {
            _zoomFactor += ZoomStep;
        }
        else if (delta < 0 && _zoomFactor > ZoomStep)
        {
            _zoomFactor -= ZoomStep;
        }

        // Update transform matrix for zoom
        _transformMatrix = Matrix.CreateScale(_zoomFactor, _zoomFactor);

        InvalidateVisual();
        e.Handled = true;
    }

    private void DrawingCanvas_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetPosition(this);
        var props = e.GetCurrentPoint(this).Properties;

        // Transform point by inverse matrix to get canvas coordinates
        var transformedPoint = point * _transformMatrix.Invert();

        // Check if clicking on a resize handle of selected label
        if (SelectedLabel != null)
        {
            var handleType = GetResizeHandleAt(transformedPoint, SelectedLabel);
            if (handleType != ResizeHandleType.None)
            {
                IsResizing = true;
                _resizeHandleType = handleType;
                StartPoint = transformedPoint;
                _hasMovedOrResized = false;
                e.Handled = true;
                return;
            }
        }

        // Check if clicking on existing label
        var clickedLabel = GetLabelAt(transformedPoint);
        if (clickedLabel != null)
        {
            // Handle multi-selection with Ctrl/Cmd
            if (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta))
            {
                if (SelectedLabels.Contains(clickedLabel))
                {
                    SelectedLabels.Remove(clickedLabel);
                    if (SelectedLabel == clickedLabel)
                    {
                        SelectedLabel = SelectedLabels.FirstOrDefault();
                    }
                }
                else
                {
                    SelectedLabels.Add(clickedLabel);
                    SelectedLabel = clickedLabel;
                }
            }
            else
            {
                // Single selection
                if (!SelectedLabels.Contains(clickedLabel))
                {
                    SelectedLabels.Clear();
                }
                SelectedLabel = clickedLabel;
                SelectedLabels.Add(clickedLabel);
            }

            IsDragging = true;
            DragStart = transformedPoint;
            _hasMovedOrResized = false;
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        // Start drawing new rectangle
        SelectedLabel = null;
        SelectedLabels.Clear();
        IsDrawing = true;
        StartPoint = transformedPoint;
        CurrentRect = new LabelRect(transformedPoint.X, transformedPoint.Y, 0, 0);

        InvalidateVisual();
        e.Handled = true;
    }

    private void DrawingCanvas_PointerMoved(object? sender, PointerEventArgs e)
    {
        var point = e.GetPosition(this);
        var transformedPoint = point * _transformMatrix.Invert();
        CursorPosition = transformedPoint;

        if (IsDrawing)
        {
            // Update current rectangle
            double x = Math.Min(StartPoint.X, transformedPoint.X);
            double y = Math.Min(StartPoint.Y, transformedPoint.Y);
            double width = Math.Abs(transformedPoint.X - StartPoint.X);
            double height = Math.Abs(transformedPoint.Y - StartPoint.Y);

            CurrentRect = new LabelRect(x, y, width, height);
            InvalidateVisual();
        }
        else if (IsResizing && SelectedLabel != null)
        {
            _hasMovedOrResized = true;

            // Calculate new rectangle based on resize handle
            var newRect = CalculateResizedRect(SelectedLabel.Rect, transformedPoint, _resizeHandleType);
            SelectedLabel.Rect = newRect;

            InvalidateVisual();
        }
        else if (IsDragging)
        {
            _hasMovedOrResized = true;

            double deltaX = transformedPoint.X - DragStart.X;
            double deltaY = transformedPoint.Y - DragStart.Y;

            if (SelectedLabels.Count > 0)
            {
                foreach (var label in SelectedLabels)
                {
                    label.Rect = new LabelRect(
                        label.Rect.X + deltaX,
                        label.Rect.Y + deltaY,
                        label.Rect.Width,
                        label.Rect.Height);
                }
            }
            else if (SelectedLabel != null)
            {
                SelectedLabel.Rect = new LabelRect(
                    SelectedLabel.Rect.X + deltaX,
                    SelectedLabel.Rect.Y + deltaY,
                    SelectedLabel.Rect.Width,
                    SelectedLabel.Rect.Height);
            }

            DragStart = transformedPoint;
            InvalidateVisual();
        }
        else
        {
            // Update cursor based on position
            UpdateCursor(transformedPoint);
        }
    }

    private void DrawingCanvas_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (IsDrawing)
        {
            IsDrawing = false;

            // Only create label if rectangle has meaningful size
            if (CurrentRect.Width > 5 && CurrentRect.Height > 5)
            {
                SaveUndoState();

                var newLabel = new LabelData($"Label {Labels.Count + 1}", CurrentRect);
                Labels.Add(newLabel);
                SelectedLabel = newLabel;
                SelectedLabels.Clear();
                SelectedLabels.Add(newLabel);

                RefreshLabelListBox();
                OnLabelsChanged();
            }

            CurrentRect = new LabelRect(0, 0, 0, 0);
            InvalidateVisual();
        }
        else if (IsResizing || IsDragging)
        {
            if (_hasMovedOrResized)
            {
                SaveUndoState();
                OnLabelsChanged();
            }

            IsResizing = false;
            IsDragging = false;
            _resizeHandleType = ResizeHandleType.None;
            _hasMovedOrResized = false;
        }

        e.Handled = true;
    }

    private void DrawingCanvas_PointerExited(object? sender, PointerEventArgs e)
    {
        // Reset cursor when leaving canvas
        Cursor = new Cursor(StandardCursorType.Arrow);
    }

    private void UpdateCursor(Point point)
    {
        if (SelectedLabel != null)
        {
            var handleType = GetResizeHandleAt(point, SelectedLabel);

            Cursor = handleType switch
            {
                ResizeHandleType.TopLeft => new Cursor(StandardCursorType.TopLeftCorner),
                ResizeHandleType.BottomRight => new Cursor(StandardCursorType.BottomRightCorner),
                ResizeHandleType.TopRight => new Cursor(StandardCursorType.TopRightCorner),
                ResizeHandleType.BottomLeft => new Cursor(StandardCursorType.BottomLeftCorner),
                ResizeHandleType.Left or ResizeHandleType.Right => new Cursor(StandardCursorType.SizeWestEast),
                ResizeHandleType.Top or ResizeHandleType.Bottom => new Cursor(StandardCursorType.SizeNorthSouth),
                _ => new Cursor(StandardCursorType.Arrow)
            };
        }
        else
        {
            Cursor = new Cursor(StandardCursorType.Arrow);
        }
    }

    #endregion

    #region Helper Methods

    private LabelData? GetLabelAt(Point point)
    {
        // Check in reverse order so topmost labels are selected first
        for (int i = Labels.Count - 1; i >= 0; i--)
        {
            var label = Labels[i];
            if (point.X >= label.Rect.X && point.X <= label.Rect.X + label.Rect.Width &&
                point.Y >= label.Rect.Y && point.Y <= label.Rect.Y + label.Rect.Height)
            {
                return label;
            }
        }
        return null;
    }

    private ResizeHandleType GetResizeHandleAt(Point point, LabelData label)
    {
        double handleSize = ResizeHandleSize;
        var rect = label.Rect;

        // Check corner handles
        if (IsPointNearHandle(point, new Point(rect.X, rect.Y), handleSize))
            return ResizeHandleType.TopLeft;
        if (IsPointNearHandle(point, new Point(rect.X + rect.Width, rect.Y), handleSize))
            return ResizeHandleType.TopRight;
        if (IsPointNearHandle(point, new Point(rect.X, rect.Y + rect.Height), handleSize))
            return ResizeHandleType.BottomLeft;
        if (IsPointNearHandle(point, new Point(rect.X + rect.Width, rect.Y + rect.Height), handleSize))
            return ResizeHandleType.BottomRight;

        // Check edge handles
        if (IsPointNearHandle(point, new Point(rect.X, rect.Y + rect.Height / 2), handleSize))
            return ResizeHandleType.Left;
        if (IsPointNearHandle(point, new Point(rect.X + rect.Width, rect.Y + rect.Height / 2), handleSize))
            return ResizeHandleType.Right;
        if (IsPointNearHandle(point, new Point(rect.X + rect.Width / 2, rect.Y), handleSize))
            return ResizeHandleType.Top;
        if (IsPointNearHandle(point, new Point(rect.X + rect.Width / 2, rect.Y + rect.Height), handleSize))
            return ResizeHandleType.Bottom;

        return ResizeHandleType.None;
    }

    private bool IsPointNearHandle(Point point, Point handleCenter, double handleSize)
    {
        double halfSize = handleSize / 2;
        return point.X >= handleCenter.X - halfSize && point.X <= handleCenter.X + halfSize &&
               point.Y >= handleCenter.Y - halfSize && point.Y <= handleCenter.Y + halfSize;
    }

    private LabelRect CalculateResizedRect(LabelRect original, Point newPoint, ResizeHandleType handleType)
    {
        double x = original.X;
        double y = original.Y;
        double width = original.Width;
        double height = original.Height;

        switch (handleType)
        {
            case ResizeHandleType.TopLeft:
                width += (x - newPoint.X);
                height += (y - newPoint.Y);
                x = newPoint.X;
                y = newPoint.Y;
                break;
            case ResizeHandleType.TopRight:
                width = newPoint.X - x;
                height += (y - newPoint.Y);
                y = newPoint.Y;
                break;
            case ResizeHandleType.BottomLeft:
                width += (x - newPoint.X);
                height = newPoint.Y - y;
                x = newPoint.X;
                break;
            case ResizeHandleType.BottomRight:
                width = newPoint.X - x;
                height = newPoint.Y - y;
                break;
            case ResizeHandleType.Left:
                width += (x - newPoint.X);
                x = newPoint.X;
                break;
            case ResizeHandleType.Right:
                width = newPoint.X - x;
                break;
            case ResizeHandleType.Top:
                height += (y - newPoint.Y);
                y = newPoint.Y;
                break;
            case ResizeHandleType.Bottom:
                height = newPoint.Y - y;
                break;
        }

        // Ensure minimum size
        width = Math.Max(width, 10);
        height = Math.Max(height, 10);

        return new LabelRect(x, y, width, height);
    }

    protected virtual void OnLabelsChanged()
    {
        LabelsChanged?.Invoke(this, EventArgs.Empty);
    }

    #endregion

    #region Keyboard Handling

    private void DrawingCanvas_KeyDown(object? sender, KeyEventArgs e)
    {
        // Undo/Redo
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta))
        {
            switch (e.Key)
            {
                case Key.Z:
                    Undo();
                    e.Handled = true;
                    return;
                case Key.Y:
                    Redo();
                    e.Handled = true;
                    return;
                case Key.C:
                    CopyLabels();
                    e.Handled = true;
                    return;
                case Key.V:
                    PasteLabels();
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

        // Movement and deletion for selected labels
        if ((SelectedLabel != null && Labels.Contains(SelectedLabel)) || SelectedLabels.Count > 0)
        {
            int moveAmount = 1;
            bool needsUpdate = false;

            switch (e.Key)
            {
                case Key.Up:
                case Key.Down:
                case Key.Left:
                case Key.Right:
                    // Save undo state for arrow key movements
                    SaveUndoState();
                    needsUpdate = true;
                    break;
            }

            switch (e.Key)
            {
                case Key.Up:
                    MoveSelectedLabels(0, -moveAmount);
                    break;
                case Key.Down:
                    MoveSelectedLabels(0, moveAmount);
                    break;
                case Key.Left:
                    MoveSelectedLabels(-moveAmount, 0);
                    break;
                case Key.Right:
                    MoveSelectedLabels(moveAmount, 0);
                    break;
                case Key.Delete:
                    SaveUndoState();
                    if (SelectedLabels.Count > 0)
                    {
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

                    RefreshLabelListBox();
                    OnLabelsChanged();
                    needsUpdate = true;
                    break;
            }

            if (needsUpdate)
            {
                if (e.Key != Key.Delete)
                {
                    OnLabelsChanged();
                }
                InvalidateVisual();
                e.Handled = true;
            }
        }
    }

    private void MoveSelectedLabels(double deltaX, double deltaY)
    {
        if (SelectedLabels.Count > 0)
        {
            foreach (var label in SelectedLabels)
            {
                label.Rect = new LabelRect(
                    label.Rect.X + deltaX,
                    label.Rect.Y + deltaY,
                    label.Rect.Width,
                    label.Rect.Height);
            }
        }
        else if (SelectedLabel != null)
        {
            SelectedLabel.Rect = new LabelRect(
                SelectedLabel.Rect.X + deltaX,
                SelectedLabel.Rect.Y + deltaY,
                SelectedLabel.Rect.Width,
                SelectedLabel.Rect.Height);
        }
    }

    #endregion
}
