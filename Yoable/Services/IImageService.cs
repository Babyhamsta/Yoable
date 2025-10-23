using System.Threading.Tasks;

namespace Yoable.Services;

/// <summary>
/// Cross-platform image loading service
/// </summary>
public interface IImageService
{
    /// <summary>
    /// Gets the dimensions of an image file without loading it into memory
    /// </summary>
    Task<ImageSize?> GetImageDimensionsAsync(string filePath);

    /// <summary>
    /// Validates if a file is a valid image
    /// </summary>
    bool IsValidImage(string filePath);
}

/// <summary>
/// Simple structure to hold image dimensions
/// </summary>
public struct ImageSize
{
    public double Width { get; set; }
    public double Height { get; set; }

    public ImageSize(double width, double height)
    {
        Width = width;
        Height = height;
    }

    public override string ToString() => $"{Width}x{Height}";
}
