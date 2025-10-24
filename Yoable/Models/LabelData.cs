namespace Yoable.Models;

/// <summary>
/// Cross-platform rectangle structure for labels
/// </summary>
public struct LabelRect
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }

    public LabelRect(double x, double y, double width, double height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public override string ToString() => $"X={X}, Y={Y}, Width={Width}, Height={Height}";
}

/// <summary>
/// Represents a label with name and bounding box
/// </summary>
public class LabelData
{
    public string Name { get; set; }
    public LabelRect Rect { get; set; }

    public LabelData(string name, LabelRect rect)
    {
        Name = name;
        Rect = rect;
    }

    // Deep copy constructor
    public LabelData(LabelData source)
    {
        Name = source.Name;
        Rect = source.Rect;
    }
}
