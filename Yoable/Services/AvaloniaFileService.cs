using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace Yoable.Services;

/// <summary>
/// Avalonia implementation of file service
/// </summary>
public class AvaloniaFileService : IFileService
{
    private Window? GetMainWindow()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.MainWindow;
        }
        return null;
    }

    public async Task<string?> OpenFileAsync(string title, FileFilter[]? filters = null)
    {
        var window = GetMainWindow();
        if (window == null)
            return null;

        var storageProvider = window.StorageProvider;
        if (storageProvider == null)
            return null;

        var options = new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = ConvertFilters(filters)
        };

        var result = await storageProvider.OpenFilePickerAsync(options);
        return result?.FirstOrDefault()?.Path.LocalPath;
    }

    public async Task<string[]?> OpenFilesAsync(string title, FileFilter[]? filters = null)
    {
        var window = GetMainWindow();
        if (window == null)
            return null;

        var storageProvider = window.StorageProvider;
        if (storageProvider == null)
            return null;

        var options = new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = true,
            FileTypeFilter = ConvertFilters(filters)
        };

        var result = await storageProvider.OpenFilePickerAsync(options);
        return result?.Select(f => f.Path.LocalPath).ToArray();
    }

    public async Task<string?> SaveFileAsync(string title, string? defaultFileName = null, FileFilter[]? filters = null)
    {
        var window = GetMainWindow();
        if (window == null)
            return null;

        var storageProvider = window.StorageProvider;
        if (storageProvider == null)
            return null;

        var options = new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = defaultFileName,
            FileTypeChoices = ConvertFilters(filters)
        };

        var result = await storageProvider.SaveFilePickerAsync(options);
        return result?.Path.LocalPath;
    }

    public async Task<string?> OpenFolderAsync(string title)
    {
        var window = GetMainWindow();
        if (window == null)
            return null;

        var storageProvider = window.StorageProvider;
        if (storageProvider == null)
            return null;

        var options = new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false
        };

        var result = await storageProvider.OpenFolderPickerAsync(options);
        return result?.FirstOrDefault()?.Path.LocalPath;
    }

    private List<FilePickerFileType>? ConvertFilters(FileFilter[]? filters)
    {
        if (filters == null || filters.Length == 0)
            return null;

        var result = new List<FilePickerFileType>();

        foreach (var filter in filters)
        {
            var patterns = filter.Extensions.Select(ext => ext.StartsWith("*.") ? ext : "*." + ext).ToArray();
            result.Add(new FilePickerFileType(filter.Name)
            {
                Patterns = patterns
            });
        }

        return result;
    }
}
