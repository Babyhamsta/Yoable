using System.Collections.Generic;
using System.Threading.Tasks;

namespace Yoable.Services;

/// <summary>
/// Cross-platform file picker and folder browser service
/// </summary>
public interface IFileService
{
    /// <summary>
    /// Opens a file picker dialog to select a single file
    /// </summary>
    Task<string?> OpenFileAsync(string title, FileFilter[]? filters = null);

    /// <summary>
    /// Opens a file picker dialog to select multiple files
    /// </summary>
    Task<string[]?> OpenFilesAsync(string title, FileFilter[]? filters = null);

    /// <summary>
    /// Opens a save file dialog
    /// </summary>
    Task<string?> SaveFileAsync(string title, string? defaultFileName = null, FileFilter[]? filters = null);

    /// <summary>
    /// Opens a folder picker dialog
    /// </summary>
    Task<string?> OpenFolderAsync(string title);
}

/// <summary>
/// File filter for file picker dialogs
/// </summary>
public class FileFilter
{
    public string Name { get; set; }
    public List<string> Extensions { get; set; }

    public FileFilter(string name, params string[] extensions)
    {
        Name = name;
        Extensions = new List<string>(extensions);
    }
}
