using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using Yoable.Models;
using Yoable.Services;
using Timer = System.Timers.Timer;

namespace Yoable.Managers;

/// <summary>
/// Cross-platform ProjectManager using service abstractions
/// Manages project creation, loading, saving, and auto-save functionality
/// </summary>
public class ProjectManager
{
    private const string PROJECT_EXTENSION = ".yoable";
    private const string LABELS_FOLDER = "labels";
    private const string BACKUP_FOLDER = ".backup";
    private const int MAX_RECENT_PROJECTS = 10;
    private const int MAX_BACKUPS = 3;
    private const int SAVE_DEBOUNCE_MS = 500; // Debounce save calls by 500ms

    private readonly IDialogService _dialogService;
    private readonly IFileService _fileService;
    private readonly ISettingsService _settingsService;
    private readonly IDispatcherService _dispatcherService;

    // Manager references - will be set after construction
    private ImageManager? _imageManager;
    private LabelManager? _labelManager;

    private Timer? autoSaveTimer;
    private Timer? saveDebounceTimer;
    private DateTime lastSaveTime;
    private bool hasUnsavedChanges;
    private bool isSaving = false;
    private CancellationTokenSource? saveCancellationToken;

    public ProjectData? CurrentProject { get; private set; }
    public DateTime LastSaveTime => lastSaveTime;
    public bool HasUnsavedChanges => hasUnsavedChanges;
    public bool IsProjectOpen => CurrentProject != null;
    public bool IsSaving => isSaving;

    private JsonSerializerOptions jsonOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    public ProjectManager(
        IDialogService dialogService,
        IFileService fileService,
        ISettingsService settingsService,
        IDispatcherService dispatcherService)
    {
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        _fileService = fileService ?? throw new ArgumentNullException(nameof(fileService));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _dispatcherService = dispatcherService ?? throw new ArgumentNullException(nameof(dispatcherService));

        lastSaveTime = DateTime.MinValue;
        hasUnsavedChanges = false;

        // Initialize save debounce timer
        saveDebounceTimer = new Timer(SAVE_DEBOUNCE_MS);
        saveDebounceTimer.AutoReset = false;
        saveDebounceTimer.Elapsed += async (s, e) => await SaveProjectAsync();
    }

    /// <summary>
    /// Sets the manager references after construction
    /// </summary>
    public void SetManagers(ImageManager imageManager, LabelManager labelManager)
    {
        _imageManager = imageManager;
        _labelManager = labelManager;
    }

    #region Project Creation and Loading

    /// <summary>
    /// Creates a new project
    /// </summary>
    public async Task<bool> CreateNewProjectAsync(string projectName, string projectLocation)
    {
        try
        {
            // Create project folder
            string projectFolder = Path.Combine(projectLocation, projectName);
            if (Directory.Exists(projectFolder))
            {
                var useExisting = await _dialogService.ShowConfirmationAsync(
                    "Folder Exists",
                    $"A folder named '{projectName}' already exists at this location.\\n\\nDo you want to use this folder?");

                if (!useExisting)
                    return false;
            }
            else
            {
                Directory.CreateDirectory(projectFolder);
            }

            // Create labels folder
            string labelsFolder = Path.Combine(projectFolder, LABELS_FOLDER);
            Directory.CreateDirectory(labelsFolder);

            // Create backup folder
            string backupFolder = Path.Combine(projectFolder, BACKUP_FOLDER);
            Directory.CreateDirectory(backupFolder);

            // Initialize project data
            CurrentProject = new ProjectData
            {
                ProjectName = projectName,
                ProjectPath = Path.Combine(projectFolder, projectName + PROJECT_EXTENSION),
                ProjectFolder = projectFolder,
                CreatedDate = DateTime.Now,
                LastModified = DateTime.Now
            };

            // Save the new project (synchronous for creation)
            string json = JsonSerializer.Serialize(CurrentProject, jsonOptions);
            File.WriteAllText(CurrentProject.ProjectPath, json);
            lastSaveTime = DateTime.Now;

            // Add to recent projects
            AddToRecentProjects(CurrentProject.ProjectPath);

            hasUnsavedChanges = false;
            return true;
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync(
                "Error Creating Project",
                $"Failed to create project:\\n\\n{ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Loads an existing project from file (async with progress)
    /// </summary>
    public async Task<bool> LoadProjectAsync(string projectPath, IProgress<(int current, int total, string message)>? progress = null)
    {
        try
        {
            if (!File.Exists(projectPath))
            {
                await _dialogService.ShowErrorAsync(
                    "File Not Found",
                    $"Project file not found:\\n{projectPath}");
                return false;
            }

            progress?.Report((0, 100, "Reading project file..."));

            // Read and deserialize project file
            string json = await File.ReadAllTextAsync(projectPath);
            CurrentProject = JsonSerializer.Deserialize<ProjectData>(json, jsonOptions);

            if (CurrentProject == null)
            {
                await _dialogService.ShowErrorAsync(
                    "Load Error",
                    "Failed to load project. The project file may be corrupted.");
                return false;
            }

            progress?.Report((20, 100, "Validating project data..."));

            // Update project paths
            CurrentProject.ProjectPath = projectPath;
            CurrentProject.ProjectFolder = Path.GetDirectoryName(projectPath)!;

            // Validate project data and handle missing files
            var validationResult = ValidateProjectData();
            if (validationResult.HasMissingFiles)
            {
                if (!await HandleMissingFilesAsync(validationResult))
                {
                    // User canceled or chose not to load
                    CurrentProject = null;
                    return false;
                }
            }

            progress?.Report((40, 100, "Project loaded successfully"));

            // Add to recent projects
            AddToRecentProjects(projectPath);

            hasUnsavedChanges = false;
            lastSaveTime = File.GetLastWriteTime(projectPath);

            return true;
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync(
                "Error Loading Project",
                $"Failed to load project:\\n\\n{ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Closes the current project
    /// </summary>
    public async Task<bool> CloseProjectAsync(bool checkForUnsavedChanges = true)
    {
        if (CurrentProject == null)
            return true;

        if (checkForUnsavedChanges && hasUnsavedChanges)
        {
            var result = await _dialogService.ShowYesNoCancelAsync(
                "Unsaved Changes",
                $"Do you want to save changes to '{CurrentProject.ProjectName}'?");

            switch (result)
            {
                case DialogResult.Yes:
                    ExportProjectData();
                    if (!await SaveProjectAsync())
                        return false;
                    break;

                case DialogResult.Cancel:
                    return false;
            }
        }

        // Stop auto-save
        StopAutoSave();

        // Clear project
        CurrentProject = null;
        hasUnsavedChanges = false;
        lastSaveTime = DateTime.MinValue;

        Debug.WriteLine("Project closed successfully");
        return true;
    }

    #endregion

    #region Project Saving

    /// <summary>
    /// Saves the project asynchronously with progress reporting
    /// </summary>
    public async Task<bool> SaveProjectAsync(IProgress<(int current, int total, string message)>? progress = null)
    {
        if (CurrentProject == null)
            return false;

        if (isSaving)
        {
            Debug.WriteLine("Save operation already in progress, skipping...");
            return false;
        }

        isSaving = true;

        try
        {
            progress?.Report((0, 100, "Preparing project data..."));

            // Export current state - async to prevent UI lag
            await ExportProjectDataAsync();

            progress?.Report((30, 100, "Saving project file..."));

            // Update timestamps
            CurrentProject.LastModified = DateTime.Now;

            // Serialize to JSON
            string json = JsonSerializer.Serialize(CurrentProject, jsonOptions);

            // Create backup before saving
            await CreateBackupAsync();

            progress?.Report((60, 100, "Writing project file..."));

            // Write to file
            await File.WriteAllTextAsync(CurrentProject.ProjectPath, json);

            progress?.Report((100, 100, "Project saved successfully"));

            lastSaveTime = DateTime.Now;
            hasUnsavedChanges = false;

            Debug.WriteLine($"Project saved successfully: {CurrentProject.ProjectPath}");
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error saving project: {ex.Message}");
            await _dispatcherService.InvokeAsync(async () =>
            {
                await _dialogService.ShowErrorAsync(
                    "Save Error",
                    $"Failed to save project:\\n\\n{ex.Message}");
            });
            return false;
        }
        finally
        {
            isSaving = false;
        }
    }

    /// <summary>
    /// Saves the project to a new location
    /// </summary>
    public async Task<bool> SaveProjectAsAsync(string newPath)
    {
        if (CurrentProject == null)
            return false;

        try
        {
            // Update project info
            string? newFolder = Path.GetDirectoryName(newPath);
            string newName = Path.GetFileNameWithoutExtension(newPath);

            if (string.IsNullOrEmpty(newFolder))
                return false;

            // Create new project folder structure
            string newProjectFolder = Path.Combine(newFolder, newName);
            Directory.CreateDirectory(newProjectFolder);

            string newLabelsFolder = Path.Combine(newProjectFolder, LABELS_FOLDER);
            Directory.CreateDirectory(newLabelsFolder);

            string newBackupFolder = Path.Combine(newProjectFolder, BACKUP_FOLDER);
            Directory.CreateDirectory(newBackupFolder);

            // Copy all label files to new location
            string oldLabelsFolder = Path.Combine(CurrentProject.ProjectFolder, LABELS_FOLDER);
            if (Directory.Exists(oldLabelsFolder))
            {
                foreach (string file in Directory.GetFiles(oldLabelsFolder))
                {
                    string fileName = Path.GetFileName(file);
                    string destFile = Path.Combine(newLabelsFolder, fileName);
                    File.Copy(file, destFile, true);
                }
            }

            // Update project paths
            CurrentProject.ProjectName = newName;
            CurrentProject.ProjectPath = Path.Combine(newProjectFolder, newName + PROJECT_EXTENSION);
            CurrentProject.ProjectFolder = newProjectFolder;
            CurrentProject.LastModified = DateTime.Now;

            // Update all label paths in project data
            var updatedAppLabels = new Dictionary<string, string>();
            foreach (var kvp in CurrentProject.AppCreatedLabels)
            {
                // Keep the same relative path structure
                updatedAppLabels[kvp.Key] = kvp.Value;
            }
            CurrentProject.AppCreatedLabels = updatedAppLabels;

            // Save to new location
            if (await SaveProjectAsync())
            {
                AddToRecentProjects(CurrentProject.ProjectPath);
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync(
                "Save As Error",
                $"Failed to save project as:\\n\\n{ex.Message}");
            return false;
        }
    }

    #endregion

    #region Auto-Save

    /// <summary>
    /// Starts the auto-save timer
    /// </summary>
    public void StartAutoSave()
    {
        int autoSaveInterval = _settingsService.AutoSaveInterval;
        if (autoSaveInterval <= 0)
        {
            Debug.WriteLine("Auto-save is disabled");
            return;
        }

        if (autoSaveTimer != null)
        {
            autoSaveTimer.Stop();
            autoSaveTimer.Dispose();
        }

        autoSaveTimer = new Timer(autoSaveInterval * 60 * 1000); // Convert minutes to milliseconds
        autoSaveTimer.AutoReset = true;
        autoSaveTimer.Elapsed += async (s, e) =>
        {
            if (hasUnsavedChanges && CurrentProject != null)
            {
                Debug.WriteLine("Auto-saving project...");
                await SaveProjectAsync();
            }
        };

        autoSaveTimer.Start();
        Debug.WriteLine($"Auto-save started (interval: {autoSaveInterval} minutes)");
    }

    /// <summary>
    /// Stops the auto-save timer
    /// </summary>
    public void StopAutoSave()
    {
        if (autoSaveTimer != null)
        {
            autoSaveTimer.Stop();
            autoSaveTimer.Dispose();
            autoSaveTimer = null;
            Debug.WriteLine("Auto-save stopped");
        }
    }

    /// <summary>
    /// Marks the project as having unsaved changes
    /// </summary>
    public void MarkDirty()
    {
        hasUnsavedChanges = true;
        saveDebounceTimer?.Stop();
        saveDebounceTimer?.Start();
    }

    #endregion

    #region Backup Management

    /// <summary>
    /// Creates a backup of the current project
    /// </summary>
    private async Task CreateBackupAsync()
    {
        if (CurrentProject == null)
            return;

        try
        {
            string backupFolder = Path.Combine(CurrentProject.ProjectFolder, BACKUP_FOLDER);
            if (!Directory.Exists(backupFolder))
                Directory.CreateDirectory(backupFolder);

            // Create backup filename with timestamp
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string backupFileName = $"{CurrentProject.ProjectName}_backup_{timestamp}{PROJECT_EXTENSION}";
            string backupPath = Path.Combine(backupFolder, backupFileName);

            // Copy current project file to backup
            if (File.Exists(CurrentProject.ProjectPath))
            {
                await Task.Run(() => File.Copy(CurrentProject.ProjectPath, backupPath, true));
                Debug.WriteLine($"Backup created: {backupPath}");
            }

            // Clean up old backups
            await CleanupOldBackupsAsync(backupFolder);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error creating backup: {ex.Message}");
            // Don't throw - backup failure shouldn't prevent saving
        }
    }

    /// <summary>
    /// Removes old backup files, keeping only the most recent ones
    /// </summary>
    private async Task CleanupOldBackupsAsync(string backupFolder)
    {
        await Task.Run(() =>
        {
            try
            {
                var backupFiles = Directory.GetFiles(backupFolder, $"*_backup_*{PROJECT_EXTENSION}")
                                          .Select(f => new FileInfo(f))
                                          .OrderByDescending(f => f.CreationTime)
                                          .ToList();

                // Keep only MAX_BACKUPS most recent backups
                if (backupFiles.Count > MAX_BACKUPS)
                {
                    var filesToDelete = backupFiles.Skip(MAX_BACKUPS);
                    foreach (var file in filesToDelete)
                    {
                        file.Delete();
                        Debug.WriteLine($"Deleted old backup: {file.Name}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error cleaning up backups: {ex.Message}");
            }
        });
    }

    #endregion

    #region Project Data Import/Export

    /// <summary>
    /// Exports project data asynchronously - only blocks UI thread for minimal UI access
    /// </summary>
    private async Task ExportProjectDataAsync()
    {
        if (CurrentProject == null || _imageManager == null || _labelManager == null)
            return;

        Debug.WriteLine("Exporting project data...");

        // Do all heavy processing on background thread
        await Task.Run(() =>
        {
            // Clear existing data
            CurrentProject.Images.Clear();
            CurrentProject.ImageStatuses.Clear();
            CurrentProject.AppCreatedLabels.Clear();

            // Export images
            foreach (var kvp in _imageManager.ImagePathMap)
            {
                string fileName = kvp.Key;
                string fullPath = kvp.Value.Path;

                CurrentProject.Images.Add(new ImageReference
                {
                    FileName = fileName,
                    FullPath = fullPath,
                    Width = kvp.Value.OriginalDimensions.Width,
                    Height = kvp.Value.OriginalDimensions.Height
                });

                Debug.WriteLine($"Exported image: {fileName}");
            }

            // Export image statuses
            foreach (var kvp in _imageManager.ImageStatuses)
            {
                CurrentProject.ImageStatuses[kvp.Key] = kvp.Value;
            }

            // Export labels to project's labels folder
            string labelsFolder = Path.Combine(CurrentProject.ProjectFolder, LABELS_FOLDER);
            if (!Directory.Exists(labelsFolder))
                Directory.CreateDirectory(labelsFolder);

            foreach (var kvp in _labelManager.LabelStorage)
            {
                string fileName = kvp.Key;
                var labels = kvp.Value;

                if (labels.Count == 0)
                    continue;

                // Get the corresponding image to export labels
                if (_imageManager.ImagePathMap.TryGetValue(fileName, out var imageInfo))
                {
                    string labelFileName = Path.GetFileNameWithoutExtension(fileName) + ".txt";
                    string labelPath = Path.Combine(labelsFolder, labelFileName);

                    // Export labels using LabelManager
                    _labelManager.ExportLabelsToYoloWithDimensions(
                        labelPath,
                        (int)imageInfo.OriginalDimensions.Width,
                        (int)imageInfo.OriginalDimensions.Height,
                        labels);

                    // Store relative path in project
                    string relativePath = Path.Combine(LABELS_FOLDER, labelFileName);
                    CurrentProject.AppCreatedLabels[fileName] = relativePath;

                    Debug.WriteLine($"Exported {labels.Count} labels for {fileName}");
                }
            }

            Debug.WriteLine($"Export complete: {CurrentProject.Images.Count} images, {CurrentProject.AppCreatedLabels.Count} label files");
        });
    }

    /// <summary>
    /// Synchronous version for backward compatibility (used in CloseProject)
    /// </summary>
    public void ExportProjectData()
    {
        ExportProjectDataAsync().GetAwaiter().GetResult();
    }

    /// <summary>
    /// Imports project data into managers asynchronously with progress reporting
    /// </summary>
    public async Task ImportProjectDataAsync(
        IProgress<(int current, int total, string message)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (CurrentProject == null || _imageManager == null || _labelManager == null)
            return;

        try
        {
            Debug.WriteLine($"Importing project data from: {CurrentProject.ProjectPath}");
            progress?.Report((0, 100, "Clearing existing data..."));

            // Clear existing data
            _labelManager.ClearAll();
            _imageManager.ClearAll();

            progress?.Report((10, 100, "Loading images..."));

            // Get parallel processing setting
            bool enableParallel = _settingsService.EnableParallelProcessing;
            int batchSize = _settingsService.UIBatchSize;

            // Import images using async batch processing
            int totalImages = CurrentProject.Images.Count;
            int processedImages = 0;

            // Set batch size for image manager
            _imageManager.BatchSize = batchSize;

            // Process images in batches with progress
            foreach (var imageRef in CurrentProject.Images)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                if (File.Exists(imageRef.FullPath))
                {
                    await Task.Run(() => _imageManager.AddImage(imageRef.FullPath), cancellationToken);
                    Debug.WriteLine($"Loaded image: {imageRef.FileName}");
                }
                else
                {
                    Debug.WriteLine($"Warning: Image not found: {imageRef.FullPath}");
                }

                processedImages++;
                int imageProgress = 10 + (int)((processedImages / (double)totalImages) * 30);
                progress?.Report((imageProgress, 100, $"Loading images... {processedImages}/{totalImages}"));
            }

            progress?.Report((40, 100, "Loading image statuses..."));

            // Import image statuses
            foreach (var kvp in CurrentProject.ImageStatuses)
            {
                _imageManager.UpdateImageStatusValue(kvp.Key, kvp.Value);
            }

            progress?.Report((50, 100, "Loading labels..."));

            // Set label batch size from settings
            _labelManager.LabelLoadBatchSize = _settingsService.LabelLoadBatchSize;

            // Prepare label files for batch loading
            string tempLabelsDir = Path.Combine(Path.GetTempPath(), $"yoable_labels_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempLabelsDir);

            int labelsLoaded = 0;

            try
            {
                int labelFilesCopied = 0;

                // Copy app-created labels to temp directory
                foreach (var kvp in CurrentProject.AppCreatedLabels)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    string fileName = kvp.Key;
                    string relativePath = kvp.Value;
                    string labelPath = Path.Combine(CurrentProject.ProjectFolder, relativePath);

                    if (File.Exists(labelPath))
                    {
                        string tempLabelPath = Path.Combine(tempLabelsDir, Path.GetFileName(labelPath));
                        File.Copy(labelPath, tempLabelPath, true);
                        labelFilesCopied++;
                        Debug.WriteLine($"Prepared label file: {Path.GetFileName(labelPath)}");
                    }
                    else
                    {
                        Debug.WriteLine($"Warning: Label file not found: {labelPath}");
                    }
                }

                // Copy imported labels to temp directory
                foreach (var kvp in CurrentProject.ImportedLabelPaths)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    string fileName = kvp.Key;
                    string labelPath = kvp.Value;

                    if (File.Exists(labelPath))
                    {
                        string tempLabelPath = Path.Combine(tempLabelsDir, Path.GetFileName(labelPath));
                        File.Copy(labelPath, tempLabelPath, true);
                        labelFilesCopied++;
                        Debug.WriteLine($"Prepared imported label file: {Path.GetFileName(labelPath)}");
                    }
                    else
                    {
                        Debug.WriteLine($"Warning: External label file not found: {labelPath}");
                    }
                }

                if (labelFilesCopied > 0)
                {
                    // Progress reporter for label loading
                    var labelProgress = new Progress<(int current, int total, string message)>(report =>
                    {
                        // Map label loading progress to 50-90% range
                        int overallProgress = 50 + (int)((report.current / (double)report.total) * 40);
                        progress?.Report((overallProgress, 100, report.message));
                    });

                    // Use optimized batch label loader
                    labelsLoaded = await _labelManager.LoadYoloLabelsBatchAsync(
                        tempLabelsDir,
                        _imageManager,
                        labelProgress,
                        cancellationToken,
                        enableParallel
                    );

                    Debug.WriteLine($"Total labels loaded via batch processing: {labelsLoaded}");
                    Debug.WriteLine($"Images in label storage: {_labelManager.LabelStorage.Count}");
                }
                else
                {
                    Debug.WriteLine("No label files to load");
                }
            }
            finally
            {
                // Clean up temporary directory
                try
                {
                    if (Directory.Exists(tempLabelsDir))
                    {
                        Directory.Delete(tempLabelsDir, true);
                        Debug.WriteLine("Cleaned up temporary label directory");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to clean up temp directory: {ex.Message}");
                }
            }

            Debug.WriteLine($"Total labels loaded: {labelsLoaded}");
            Debug.WriteLine($"Images in label storage: {_labelManager.LabelStorage.Count}");

            progress?.Report((90, 100, "Finalizing load..."));

            // Validate and correct statuses asynchronously with progress
            await ValidateAndCorrectStatusesAsync(progress, cancellationToken);

            progress?.Report((100, 100, "Project loaded successfully"));

            hasUnsavedChanges = false;
        }
        catch (OperationCanceledException)
        {
            Debug.WriteLine("Import operation was canceled");
            throw;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error importing project data: {ex.Message}");
            await _dispatcherService.InvokeAsync(async () =>
            {
                await _dialogService.ShowWarningAsync(
                    "Import Error",
                    $"Error loading project data:\\n\\n{ex.Message}");
            });
            throw;
        }
    }

    #endregion

    #region Recent Projects

    /// <summary>
    /// Adds a project to the recent projects list
    /// </summary>
    private void AddToRecentProjects(string projectPath)
    {
        try
        {
            var recentProjects = GetRecentProjects();

            // Remove if already exists
            var existing = recentProjects.FirstOrDefault(p => p.ProjectPath == projectPath);
            if (existing != null)
                recentProjects.Remove(existing);

            // Add to beginning
            recentProjects.Insert(0, new RecentProjectInfo(
                Path.GetFileNameWithoutExtension(projectPath),
                projectPath,
                DateTime.Now));

            // Keep only last N projects
            if (recentProjects.Count > MAX_RECENT_PROJECTS)
                recentProjects.RemoveRange(MAX_RECENT_PROJECTS, recentProjects.Count - MAX_RECENT_PROJECTS);

            // Save to settings
            SaveRecentProjects(recentProjects);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to add project to recent list: {ex.Message}");
        }
    }

    /// <summary>
    /// Gets the list of recent projects
    /// </summary>
    public List<RecentProjectInfo> GetRecentProjects()
    {
        try
        {
            string json = _settingsService.RecentProjects;
            if (string.IsNullOrEmpty(json))
                return new List<RecentProjectInfo>();

            var projects = JsonSerializer.Deserialize<List<RecentProjectInfo>>(json, jsonOptions);
            return projects ?? new List<RecentProjectInfo>();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to load recent projects: {ex.Message}");
            return new List<RecentProjectInfo>();
        }
    }

    /// <summary>
    /// Removes a project from the recent projects list
    /// </summary>
    public void RemoveFromRecentProjects(string projectPath)
    {
        try
        {
            var recentProjects = GetRecentProjects();
            recentProjects.RemoveAll(p => p.ProjectPath == projectPath);
            SaveRecentProjects(recentProjects);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to remove project from recent list: {ex.Message}");
        }
    }

    /// <summary>
    /// Saves the recent projects list
    /// </summary>
    private void SaveRecentProjects(List<RecentProjectInfo> projects)
    {
        try
        {
            string json = JsonSerializer.Serialize(projects, jsonOptions);
            _settingsService.RecentProjects = json;
            _settingsService.Save();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to save recent projects: {ex.Message}");
        }
    }

    #endregion

    #region Validation

    /// <summary>
    /// Validates project data (checks for missing files, orphaned labels)
    /// </summary>
    private ValidationResult ValidateProjectData()
    {
        var result = new ValidationResult();

        if (CurrentProject == null)
            return result;

        // Check for missing images
        foreach (var imageRef in CurrentProject.Images)
        {
            if (!File.Exists(imageRef.FullPath))
            {
                result.MissingImages.Add(imageRef.FileName);
            }
        }

        // Check for orphaned labels (labels without corresponding images)
        var imageFileNames = CurrentProject.Images.Select(img => img.FileName).ToHashSet();

        foreach (var fileName in CurrentProject.AppCreatedLabels.Keys)
        {
            if (!imageFileNames.Contains(fileName))
            {
                result.OrphanedLabels.Add(fileName);
            }
        }

        foreach (var fileName in CurrentProject.ImportedLabelPaths.Keys)
        {
            if (!imageFileNames.Contains(fileName))
            {
                result.OrphanedLabels.Add(fileName);
            }
        }

        return result;
    }

    /// <summary>
    /// Handles missing files by prompting user
    /// </summary>
    private async Task<bool> HandleMissingFilesAsync(ValidationResult result)
    {
        string message = "";

        if (result.MissingImages.Any())
        {
            message = $"The following {result.MissingImages.Count} image(s) are missing:\\n\\n";
            message += string.Join("\\n", result.MissingImages.Take(5));

            if (result.MissingImages.Count > 5)
            {
                message += $"\\n... and {result.MissingImages.Count - 5} more";
            }

            message += "\\n\\n";
        }

        if (result.OrphanedLabels.Any())
        {
            if (message.Length > 0)
                message += "\\n";

            message += $"Found {result.OrphanedLabels.Count} orphaned label(s) without corresponding images.\\n\\n";
        }

        if (result.MissingImages.Any())
        {
            message += "Would you like to locate the missing images?";

            var dialogResult = await _dialogService.ShowYesNoCancelAsync(
                "Missing Files",
                message);

            switch (dialogResult)
            {
                case DialogResult.Yes:
                    if (!await TryRelocateImagesAsync(result.MissingImages))
                    {
                        // User canceled relocation
                        return false;
                    }
                    break;

                case DialogResult.No:
                    // Continue without missing files
                    break;

                case DialogResult.Cancel:
                    return false;
            }
        }
        else if (result.OrphanedLabels.Any())
        {
            var cleanupResult = await _dialogService.ShowConfirmationAsync(
                "Orphaned Labels",
                message + "Clean up these orphaned labels?");

            if (!cleanupResult)
                return true;
        }

        // Clean up missing and orphaned data
        CleanupMissingFiles(result);
        return true;
    }

    /// <summary>
    /// Attempts to relocate missing images
    /// </summary>
    private async Task<bool> TryRelocateImagesAsync(List<string> missingImages)
    {
        var proceed = await _dialogService.ShowConfirmationAsync(
            "Locate Images",
            $"Looking for {missingImages.Count} missing image(s).\\n\\n" +
            "Have your images been moved to a new folder?\\n" +
            "If yes, browse to the new location and we'll try to find them.");

        if (!proceed)
            return false;

        var newDirectory = await _fileService.OpenFolderAsync("Locate Image Folder");
        if (string.IsNullOrEmpty(newDirectory))
            return false;

        // Try to find missing images
        int foundCount = 0;
        foreach (var missingFileName in missingImages.ToList())
        {
            string newPath = Path.Combine(newDirectory, missingFileName);
            if (File.Exists(newPath))
            {
                var imageRef = CurrentProject!.Images.FirstOrDefault(img => img.FileName == missingFileName);
                if (imageRef != null)
                {
                    imageRef.FullPath = newPath;
                    foundCount++;
                    Debug.WriteLine($"Relocated image: {missingFileName} -> {newPath}");
                }
            }
        }

        if (foundCount > 0)
        {
            await _dialogService.ShowMessageAsync(
                "Images Found",
                $"Successfully relocated {foundCount} out of {missingImages.Count} image(s)!");

            MarkDirty();
            return true;
        }
        else
        {
            await _dialogService.ShowWarningAsync(
                "Images Not Found",
                "No matching images found in the selected folder.");
            return false;
        }
    }

    /// <summary>
    /// Removes missing images and orphaned labels
    /// </summary>
    private void CleanupMissingFiles(ValidationResult result)
    {
        if (CurrentProject == null)
            return;

        bool cleaned = false;

        if (result.MissingImages.Any())
        {
            CurrentProject.Images.RemoveAll(img => result.MissingImages.Contains(img.FileName));
            foreach (var fileName in result.MissingImages)
            {
                CurrentProject.ImageStatuses.Remove(fileName);
            }
            cleaned = true;
            Debug.WriteLine($"Cleaned up {result.MissingImages.Count} missing images");
        }

        if (result.OrphanedLabels.Any())
        {
            foreach (var fileName in result.OrphanedLabels)
            {
                CurrentProject.AppCreatedLabels.Remove(fileName);
                CurrentProject.ImportedLabelPaths.Remove(fileName);
                CurrentProject.ImageStatuses.Remove(fileName);
            }
            cleaned = true;
            Debug.WriteLine($"Cleaned up {result.OrphanedLabels.Count} orphaned labels");
        }

        if (cleaned)
        {
            MarkDirty();
        }
    }

    /// <summary>
    /// Validates and corrects image statuses asynchronously with batch processing
    /// </summary>
    private async Task ValidateAndCorrectStatusesAsync(
        IProgress<(int current, int total, string message)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (_imageManager == null || _labelManager == null || CurrentProject == null)
            return;

        Debug.WriteLine("Validating image statuses against actual labels...");

        var statusKeys = CurrentProject.ImageStatuses.Keys.ToArray();
        int totalImages = statusKeys.Length;

        if (totalImages == 0)
        {
            Debug.WriteLine("No images to validate");
            return;
        }

        int corrected = 0;
        int processed = 0;

        // Use batch size from settings
        int batchSize = _settingsService.ProcessingBatchSize;
        if (batchSize <= 0) batchSize = 1000;

        await Task.Run(() =>
        {
            for (int i = 0; i < statusKeys.Length; i += batchSize)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var batch = statusKeys.Skip(i).Take(batchSize).ToArray();

                foreach (var fileName in batch)
                {
                    var savedStatus = CurrentProject.ImageStatuses[fileName];
                    ImageStatus correctedStatus;

                    if (!_labelManager.LabelStorage.ContainsKey(fileName) ||
                        _labelManager.LabelStorage[fileName].Count == 0)
                    {
                        correctedStatus = ImageStatus.NoLabel;
                    }
                    else
                    {
                        if (savedStatus == ImageStatus.Verified)
                        {
                            correctedStatus = ImageStatus.Verified;
                        }
                        else
                        {
                            var labels = _labelManager.LabelStorage[fileName];
                            bool hasImportedOrAI = labels.Any(l =>
                                l.Name.StartsWith("Imported") || l.Name.StartsWith("AI"));

                            correctedStatus = hasImportedOrAI
                                ? ImageStatus.VerificationNeeded
                                : ImageStatus.Verified;
                        }
                    }

                    if (savedStatus != correctedStatus)
                    {
                        CurrentProject.ImageStatuses[fileName] = correctedStatus;
                        _imageManager.UpdateImageStatusValue(fileName, correctedStatus);
                        corrected++;
                        Debug.WriteLine($"Corrected status for {fileName}: {savedStatus} -> {correctedStatus}");
                    }

                    processed++;
                }

                // Report progress after each batch
                int overallProgress = 90 + (int)((processed / (double)totalImages) * 10);
                progress?.Report((overallProgress, 100,
                    $"Finalizing... {processed}/{totalImages} validated"));
            }

        }, cancellationToken);

        if (corrected > 0)
        {
            Debug.WriteLine($"Corrected {corrected} image status(es)");
        }
        else
        {
            Debug.WriteLine("All image statuses were already correct");
        }
    }

    private class ValidationResult
    {
        public List<string> MissingImages { get; set; } = new List<string>();
        public List<string> OrphanedLabels { get; set; } = new List<string>();
        public bool HasMissingFiles => MissingImages.Any() || OrphanedLabels.Any();
    }

    #endregion

    #region Cleanup

    /// <summary>
    /// Disposes of resources
    /// </summary>
    public void Dispose()
    {
        StopAutoSave();
        saveDebounceTimer?.Dispose();
        saveCancellationToken?.Dispose();
    }

    #endregion
}
