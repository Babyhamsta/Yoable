using System;
using System.Collections.Generic;

namespace Yoable.Models;

/// <summary>
/// Represents all data for a Yoable project
/// </summary>
public class ProjectData
{
    public string ProjectName { get; set; }
    public string ProjectPath { get; set; }  // Full path to the .yoable file
    public string ProjectFolder { get; set; }  // Folder containing the project file
    public DateTime CreatedDate { get; set; }
    public DateTime LastModified { get; set; }
    public string Version { get; set; } = "1.0";

    // Image references (paths only, no copies)
    public List<ImageReference> Images { get; set; } = new List<ImageReference>();

    // Labels created in-app (stored in project folder)
    // Key: filename, Value: relative path to label file in project folder
    public Dictionary<string, string> AppCreatedLabels { get; set; } = new Dictionary<string, string>();

    // Labels that reference external .txt files (imported labels)
    // Key: filename, Value: full path to external label file
    public Dictionary<string, string> ImportedLabelPaths { get; set; } = new Dictionary<string, string>();

    // UI State
    public Dictionary<string, ImageStatus> ImageStatuses { get; set; } = new Dictionary<string, ImageStatus>();
    public int LastSelectedImageIndex { get; set; } = -1;
    public string CurrentSortMode { get; set; } = "ByName";
    public string CurrentFilterMode { get; set; } = "All";

    // Statistics (optional, for display purposes)
    public int TotalImages => Images?.Count ?? 0;
    public int TotalLabels => (AppCreatedLabels?.Count ?? 0) + (ImportedLabelPaths?.Count ?? 0);
}

/// <summary>
/// Reference to an image file (no actual image data stored)
/// </summary>
public class ImageReference
{
    public string FileName { get; set; }
    public string FullPath { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }

    public ImageReference()
    {
    }

    public ImageReference(string fileName, string fullPath, double width, double height)
    {
        FileName = fileName;
        FullPath = fullPath;
        Width = width;
        Height = height;
    }
}
