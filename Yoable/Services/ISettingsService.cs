namespace Yoable.Services;

/// <summary>
/// Cross-platform settings service
/// </summary>
public interface ISettingsService
{
    /// <summary>
    /// Gets a setting value by key
    /// </summary>
    T? GetSetting<T>(string key, T? defaultValue = default);

    /// <summary>
    /// Sets a setting value
    /// </summary>
    void SetSetting<T>(string key, T value);

    /// <summary>
    /// Saves all settings to disk
    /// </summary>
    void Save();

    /// <summary>
    /// Gets the auto-save interval in minutes
    /// </summary>
    int AutoSaveInterval { get; set; }

    /// <summary>
    /// Gets whether parallel processing is enabled
    /// </summary>
    bool EnableParallelProcessing { get; set; }

    /// <summary>
    /// Gets the UI batch size for processing
    /// </summary>
    int UIBatchSize { get; set; }

    /// <summary>
    /// Gets the label load batch size
    /// </summary>
    int LabelLoadBatchSize { get; set; }

    /// <summary>
    /// Gets the processing batch size
    /// </summary>
    int ProcessingBatchSize { get; set; }

    /// <summary>
    /// Gets or sets the recent projects JSON
    /// </summary>
    string RecentProjects { get; set; }
}
