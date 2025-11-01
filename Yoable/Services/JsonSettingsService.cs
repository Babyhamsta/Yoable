using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Yoable.Services;

/// <summary>
/// JSON-based settings service (cross-platform)
/// </summary>
public class JsonSettingsService : ISettingsService
{
    private readonly string _settingsPath;
    private Dictionary<string, JsonElement> _settings;

    public JsonSettingsService(string? settingsPath = null)
    {
        // Default to user's application data folder
        _settingsPath = settingsPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Yoable",
            "settings.json");

        // Ensure directory exists
        var directory = Path.GetDirectoryName(_settingsPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _settings = new Dictionary<string, JsonElement>();
        Load();
    }

    private void Load()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                var json = File.ReadAllText(_settingsPath);
                _settings = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)
                           ?? new Dictionary<string, JsonElement>();
            }
        }
        catch
        {
            _settings = new Dictionary<string, JsonElement>();
        }
    }

    public T? GetSetting<T>(string key, T? defaultValue = default)
    {
        try
        {
            if (_settings.TryGetValue(key, out var element))
            {
                return JsonSerializer.Deserialize<T>(element.GetRawText());
            }
        }
        catch
        {
            // Fall through to return default
        }

        return defaultValue;
    }

    public void SetSetting<T>(string key, T value)
    {
        var json = JsonSerializer.Serialize(value);
        _settings[key] = JsonSerializer.Deserialize<JsonElement>(json);
    }

    public void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_settingsPath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save settings: {ex.Message}");
        }
    }

    // Convenience properties
    public int AutoSaveInterval
    {
        get => GetSetting("AutoSaveInterval", 5);
        set => SetSetting("AutoSaveInterval", value);
    }

    public bool EnableParallelProcessing
    {
        get => GetSetting("EnableParallelProcessing", true);
        set => SetSetting("EnableParallelProcessing", value);
    }

    public int UIBatchSize
    {
        get => GetSetting("UIBatchSize", 100);
        set => SetSetting("UIBatchSize", value);
    }

    public int LabelLoadBatchSize
    {
        get => GetSetting("LabelLoadBatchSize", 50);
        set => SetSetting("LabelLoadBatchSize", value);
    }

    public int ProcessingBatchSize
    {
        get => GetSetting("ProcessingBatchSize", 1000);
        set => SetSetting("ProcessingBatchSize", value);
    }

    public string RecentProjects
    {
        get => GetSetting("RecentProjects", string.Empty) ?? string.Empty;
        set => SetSetting("RecentProjects", value);
    }
}
