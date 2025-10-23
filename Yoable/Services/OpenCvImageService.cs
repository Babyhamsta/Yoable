using System;
using System.IO;
using System.Threading.Tasks;
using OpenCvSharp;

namespace Yoable.Services;

/// <summary>
/// OpenCV-based image service implementation (cross-platform)
/// </summary>
public class OpenCvImageService : IImageService
{
    public Task<ImageSize?> GetImageDimensionsAsync(string filePath)
    {
        return Task.Run<ImageSize?>(() =>
        {
            try
            {
                if (!File.Exists(filePath))
                    return null;

                // Use OpenCV to read image dimensions without loading full image
                using var mat = Cv2.ImRead(filePath, ImreadModes.Unchanged);
                if (mat.Empty())
                    return null;

                return new ImageSize(mat.Width, mat.Height);
            }
            catch
            {
                return null;
            }
        });
    }

    public bool IsValidImage(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
                return false;

            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            if (extension != ".jpg" && extension != ".jpeg" && extension != ".png" && extension != ".bmp")
                return false;

            // Quick check: Try to read the image header
            using var mat = Cv2.ImRead(filePath, ImreadModes.Unchanged);
            return !mat.Empty();
        }
        catch
        {
            return false;
        }
    }
}
