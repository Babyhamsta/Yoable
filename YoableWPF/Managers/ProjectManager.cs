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
using System.Windows;
using YoableWPF.Models;
using Timer = System.Timers.Timer;

namespace YoableWPF.Managers
{
    /// <summary>
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

        private Timer autoSaveTimer;
        private Timer saveDebounceTimer;
        private DateTime lastSaveTime;
        private bool hasUnsavedChanges;
        private MainWindow mainWindow;
        private bool isSaving = false;
        private CancellationTokenSource saveCancellationToken;

        public ProjectData CurrentProject { get; private set; }
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

        public ProjectManager(MainWindow window)
        {
            mainWindow = window;
            lastSaveTime = DateTime.MinValue;
            hasUnsavedChanges = false;

            // Initialize save debounce timer
            saveDebounceTimer = new Timer(SAVE_DEBOUNCE_MS);
            saveDebounceTimer.AutoReset = false;
            saveDebounceTimer.Elapsed += async (s, e) => await SaveProjectAsync();
        }

        #region Project Creation and Loading

        /// <summary>
        /// Creates a new project
        /// </summary>
        public bool CreateNewProject(string projectName, string projectLocation)
        {
            try
            {
                // Create project folder
                string projectFolder = Path.Combine(projectLocation, projectName);
                if (Directory.Exists(projectFolder))
                {
                    var result = MessageBox.Show(
                        $"A folder named '{projectName}' already exists at this location.\n\nDo you want to use this folder?",
                        "Folder Exists",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (result != MessageBoxResult.Yes)
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
                    LastModified = DateTime.Now,
                    Classes = new List<LabelClass> 
                    { 
                        new LabelClass("default", "#E57373", 0) 
                    }
                };

                // Save the new project (synchronous for creation)
                // Simple synchronous save for new project (no async blocking)
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
                MessageBox.Show(
                    $"Failed to create project:\n\n{ex.Message}",
                    "Error Creating Project",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return false;
            }
        }

        /// <summary>
        /// Loads an existing project from file (async with progress)
        /// </summary>
        public async Task<bool> LoadProjectAsync(string projectPath, IProgress<(int current, int total, string message)> progress = null)
        {
            try
            {
                if (!File.Exists(projectPath))
                {
                    MessageBox.Show(
                        $"Project file not found:\n{projectPath}",
                        "File Not Found",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return false;
                }

                progress?.Report((0, 100, "Reading project file..."));

                // Read and deserialize project file
                string json = await File.ReadAllTextAsync(projectPath);
                CurrentProject = JsonSerializer.Deserialize<ProjectData>(json, jsonOptions);

                if (CurrentProject == null)
                {
                    MessageBox.Show(
                        "Failed to load project. The project file may be corrupted.",
                        "Load Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return false;
                }

                progress?.Report((20, 100, "Validating project data..."));

                // Update project paths
                CurrentProject.ProjectPath = projectPath;
                CurrentProject.ProjectFolder = Path.GetDirectoryName(projectPath);

                // MIGRATION: Ensure Classes property exists (for old projects created before multi-class support)
                if (CurrentProject.Classes == null || CurrentProject.Classes.Count == 0)
                {
                    CurrentProject.Classes = new List<LabelClass>
                    {
                        new LabelClass("default", "#E57373", 0)
                    };
                    MarkDirty(); // Mark as dirty so migration gets saved
                }

                // Validate project data and handle missing files
                var validationResult = ValidateProjectData();
                if (validationResult.HasMissingFiles)
                {
                    if (!HandleMissingFiles(validationResult))
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
                MessageBox.Show(
                    $"Failed to load project:\n\n{ex.Message}",
                    "Error Loading Project",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return false;
            }
        }

        /// <summary>
        /// Synchronous load for backward compatibility
        /// </summary>
        public bool LoadProject(string projectPath)
        {
            return LoadProjectAsync(projectPath).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Closes the current project
        /// </summary>
        public bool CloseProject(bool checkForUnsavedChanges = true)
        {
            if (CurrentProject == null)
                return true;

            if (checkForUnsavedChanges && hasUnsavedChanges)
            {
                var result = MessageBox.Show(
                    $"Do you want to save changes to '{CurrentProject.ProjectName}'?",
                    "Unsaved Changes",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);

                switch (result)
                {
                    case MessageBoxResult.Yes:
                        ExportProjectData();
                        if (!SaveProjectSync())
                            return false;
                        break;

                    case MessageBoxResult.Cancel:
                        return false;
                }
            }

            // Stop auto-save
            StopAutoSave();

            // Clear models when closing project
            if (mainWindow?.yoloAI != null)
            {
                mainWindow.yoloAI.ClearAllModels();
            }

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
        public async Task<bool> SaveProjectAsync(IProgress<(int current, int total, string message)> progress = null)
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

                // Export current state from main window - async to prevent UI lag
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
                await mainWindow.Dispatcher.InvokeAsync(() =>
                {
                    MessageBox.Show(
                        $"Failed to save project:\n\n{ex.Message}",
                        "Save Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                });
                return false;
            }
            finally
            {
                isSaving = false;
            }
        }

        /// <summary>
        /// Synchronous save for backward compatibility
        /// </summary>
        public bool SaveProjectSync()
        {
            return SaveProjectAsync().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Saves the project to a new location
        /// </summary>
        public bool SaveProjectAs(string newPath)
        {
            if (CurrentProject == null)
                return false;

            try
            {
                // Update project info
                string newFolder = Path.GetDirectoryName(newPath);
                string newName = Path.GetFileNameWithoutExtension(newPath);

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
                if (SaveProjectSync())
                {
                    AddToRecentProjects(CurrentProject.ProjectPath);
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to save project as:\n\n{ex.Message}",
                    "Save As Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
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
            int autoSaveInterval = Properties.Settings.Default.AutoSaveInterval;
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
            saveDebounceTimer.Stop();
            saveDebounceTimer.Start();
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
        /// Exports current state from MainWindow to ProjectData
        /// </summary>
        /// <summary>
        /// Exports project data asynchronously - only blocks UI thread for minimal UI access
        /// </summary>
        private async Task ExportProjectDataAsync()
        {
            if (CurrentProject == null || mainWindow == null)
                return;

            // Quickly capture UI state on UI thread
            int selectedIndex = -1;
            string sortMode = "ByName";

            await mainWindow.Dispatcher.InvokeAsync(() =>
            {
                selectedIndex = mainWindow.ImageListBox.SelectedIndex;
                sortMode = mainWindow.SortComboBox?.SelectedIndex == 1 ? "ByStatus" : "ByName";
            });

            // Do all heavy processing on background thread
            await Task.Run(() =>
            {
                // Clear existing data
                CurrentProject.Images.Clear();
                CurrentProject.ImageStatuses.Clear();
                CurrentProject.AppCreatedLabels.Clear();

                // Export images
                foreach (var kvp in mainWindow.imageManager.ImagePathMap)
                {
                    string fileName = kvp.Key;
                    string fullPath = kvp.Value.Path;

                    CurrentProject.Images.Add(new ImageReference
                    {
                        FileName = fileName,
                        FullPath = fullPath
                    });
                }

                // Export image statuses
                foreach (var kvp in mainWindow.imageManager.ImageStatuses)
                {
                    CurrentProject.ImageStatuses[kvp.Key] = kvp.Value;
                }

                // Export labels to project's labels folder
                string labelsFolder = Path.Combine(CurrentProject.ProjectFolder, LABELS_FOLDER);
                if (!Directory.Exists(labelsFolder))
                    Directory.CreateDirectory(labelsFolder);

                foreach (var kvp in mainWindow.labelManager.LabelStorage)
                {
                    string fileName = kvp.Key;
                    var labels = kvp.Value;

                    if (labels.Count == 0)
                        continue;

                    // Get the corresponding image to export labels
                    if (mainWindow.imageManager.ImagePathMap.TryGetValue(fileName, out var imageInfo))
                    {
                        string labelFileName = Path.GetFileNameWithoutExtension(fileName) + ".txt";
                        string labelPath = Path.Combine(labelsFolder, labelFileName);

                        // Export labels using LabelManager
                        mainWindow.labelManager.ExportLabelsToYolo(labelPath, imageInfo.Path, labels);

                        // Store relative path in project
                        string relativePath = Path.Combine(LABELS_FOLDER, labelFileName);
                        CurrentProject.AppCreatedLabels[fileName] = relativePath;
                    }
                }

                // Save UI state (captured earlier)
                if (selectedIndex >= 0)
                {
                    CurrentProject.LastSelectedImageIndex = selectedIndex;
                }
                CurrentProject.CurrentSortMode = sortMode;
            });
        }

        /// <summary>
        /// Synchronous version for backward compatibility (used in CloseProject)
        /// </summary>
        public void ExportProjectData()
        {
            if (CurrentProject == null || mainWindow == null)
                return;

            // Clear existing data
            CurrentProject.Images.Clear();
            CurrentProject.ImageStatuses.Clear();
            CurrentProject.AppCreatedLabels.Clear();
            CurrentProject.LoadedModelPaths.Clear();
            CurrentProject.ModelClassMappings.Clear();

            // Export images
            foreach (var kvp in mainWindow.imageManager.ImagePathMap)
            {
                string fileName = kvp.Key;
                string fullPath = kvp.Value.Path;

                CurrentProject.Images.Add(new ImageReference
                {
                    FileName = fileName,
                    FullPath = fullPath
                });
            }

            // Export image statuses
            foreach (var kvp in mainWindow.imageManager.ImageStatuses)
            {
                CurrentProject.ImageStatuses[kvp.Key] = kvp.Value;
            }

            // Export labels to project's labels folder
            string labelsFolder = Path.Combine(CurrentProject.ProjectFolder, LABELS_FOLDER);
            if (!Directory.Exists(labelsFolder))
                Directory.CreateDirectory(labelsFolder);

            foreach (var kvp in mainWindow.labelManager.LabelStorage)
            {
                string fileName = kvp.Key;
                var labels = kvp.Value;

                if (labels.Count == 0)
                    continue;

                // Get the corresponding image to export labels
                if (mainWindow.imageManager.ImagePathMap.TryGetValue(fileName, out var imageInfo))
                {
                    string labelFileName = Path.GetFileNameWithoutExtension(fileName) + ".txt";
                    string labelPath = Path.Combine(labelsFolder, labelFileName);

                    // Export labels using LabelManager
                    mainWindow.labelManager.ExportLabelsToYolo(labelPath, imageInfo.Path, labels);

                    // Store relative path in project
                    string relativePath = Path.Combine(LABELS_FOLDER, labelFileName);
                    CurrentProject.AppCreatedLabels[fileName] = relativePath;
                }
            }

            // Save current UI state
            if (mainWindow.ImageListBox.SelectedIndex >= 0)
            {
                CurrentProject.LastSelectedImageIndex = mainWindow.ImageListBox.SelectedIndex;
            }

            // Save sort mode
            if (mainWindow.SortComboBox != null)
            {
                CurrentProject.CurrentSortMode = mainWindow.SortComboBox.SelectedIndex == 1 ? "ByStatus" : "ByName";
            }

            // Export model paths and class mappings
            if (mainWindow.yoloAI != null)
            {
                // Save loaded model paths
                CurrentProject.LoadedModelPaths = new List<string>();
                foreach (var model in mainWindow.yoloAI.GetLoadedModels())
                {
                    CurrentProject.LoadedModelPaths.Add(model.ModelPath);
                    
                    // Save mapping using model path as key
                    if (model.ClassMapping != null && model.ClassMapping.Count > 0)
                    {
                        CurrentProject.ModelClassMappings[model.ModelPath] = new Dictionary<int, int>(model.ClassMapping);
                    }
                }
            }

            Debug.WriteLine($"Export complete: {CurrentProject.Images.Count} images, {CurrentProject.AppCreatedLabels.Count} label files, {CurrentProject.LoadedModelPaths.Count} models, {CurrentProject.ModelClassMappings.Count} model mappings");
        }

        /// <summary>
        /// Imports project data into MainWindow asynchronously with progress reporting
        /// OPTIMIZED: Uses batch processing and async operations for large datasets
        /// </summary>
        public async Task ImportProjectDataAsync(IProgress<(int current, int total, string message)> progress = null, CancellationToken cancellationToken = default)
        {
            if (CurrentProject == null || mainWindow == null)
                return;

            try
            {
                Debug.WriteLine($"Importing project data from: {CurrentProject.ProjectPath}");
                progress?.Report((0, 100, "Clearing existing data..."));

                // Clear existing data
                await mainWindow.Dispatcher.InvokeAsync(() =>
                {
                    mainWindow.labelManager.ClearAll();
                    mainWindow.imageManager.ClearAll();
                    // Clear existing models before loading project models
                    if (mainWindow.yoloAI != null)
                    {
                        mainWindow.yoloAI.ClearAllModels();
                    }
                });

                progress?.Report((10, 100, "Loading images..."));

                // Get parallel processing setting
                bool enableParallel = Properties.Settings.Default.EnableParallelProcessing;
                int batchSize = Properties.Settings.Default.UIBatchSize;

                // Import images using async batch processing
                int totalImages = CurrentProject.Images.Count;
                int processedImages = 0;

                // Set batch size for image manager
                mainWindow.imageManager.BatchSize = batchSize;

                // Process images in batches with progress
                foreach (var imageRef in CurrentProject.Images)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    if (File.Exists(imageRef.FullPath))
                    {
                        await Task.Run(() => mainWindow.imageManager.AddImage(imageRef.FullPath), cancellationToken);
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
                    mainWindow.imageManager.UpdateImageStatusValue(kvp.Key, kvp.Value);
                }

                progress?.Report((50, 100, "Loading labels..."));

                // Set label batch size from settings
                mainWindow.labelManager.LabelLoadBatchSize = Properties.Settings.Default.LabelLoadBatchSize;

                // Prepare label files for batch loading
                // First, copy all label files to a temporary directory for batch processing
                string tempLabelsDir = Path.Combine(Path.GetTempPath(), $"yoable_labels_{Guid.NewGuid():N}");
                Directory.CreateDirectory(tempLabelsDir);

                int labelsLoaded = 0; // Declare outside try block so it's accessible later

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

                        // Use optimized batch label loader - MUCH faster!
                        var (loadedCount, foundClassIds) = await mainWindow.labelManager.LoadYoloLabelsBatchAsync(
                            tempLabelsDir,
                            mainWindow.imageManager,
                            labelProgress,
                            cancellationToken,
                            enableParallel
                        );
                        labelsLoaded = loadedCount;

                        Debug.WriteLine($"Total labels loaded via batch processing: {labelsLoaded}");
                        Debug.WriteLine($"Images in label storage: {mainWindow.labelManager.LabelStorage.Count}");
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
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Failed to clean up temp directory: {ex.Message}");
                    }
                }

                Debug.WriteLine($"Total labels loaded: {labelsLoaded}");
                Debug.WriteLine($"Images in label storage: {mainWindow.labelManager.LabelStorage.Count}");

                progress?.Report((90, 100, "Finalizing load..."));

                // OPTIMIZED: Validate and correct statuses asynchronously with progress
                await ValidateAndCorrectStatusesAsync(progress, cancellationToken);

                progress?.Report((95, 100, "Restoring models..."));

                // Restore loaded models
                if (CurrentProject.LoadedModelPaths != null && CurrentProject.LoadedModelPaths.Count > 0 && mainWindow.yoloAI != null)
                {
                    await mainWindow.Dispatcher.InvokeAsync(() =>
                    {
                        foreach (var modelPath in CurrentProject.LoadedModelPaths)
                        {
                            if (File.Exists(modelPath))
                            {
                                try
                                {
                                    // Use silent loading to avoid showing message boxes during project load
                                    var loadedModel = mainWindow.yoloAI.LoadModelFromPathSilent(modelPath);
                                    if (loadedModel != null)
                                    {
                                        // Restore saved mapping if available
                                        if (CurrentProject.ModelClassMappings != null && 
                                            CurrentProject.ModelClassMappings.TryGetValue(modelPath, out var savedMapping))
                                        {
                                            loadedModel.ClassMapping = new Dictionary<int, int>(savedMapping);
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Debug.WriteLine($"Failed to restore model {modelPath}: {ex.Message}");
                                    // Continue with other models even if one fails
                                }
                            }
                            else
                            {
                                Debug.WriteLine($"Warning: Model file not found: {modelPath}");
                            }
                        }
                    });
                }

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
                await mainWindow.Dispatcher.InvokeAsync(() =>
                {
                    MessageBox.Show(
                        $"Error loading project data:\n\n{ex.Message}",
                        "Import Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                });
                throw;
            }
        }

        /// <summary>
        /// Synchronous import for backward compatibility
        /// </summary>
        public void ImportProjectData()
        {
            ImportProjectDataAsync().GetAwaiter().GetResult();
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
                string json = Properties.Settings.Default.RecentProjects;
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
                Properties.Settings.Default.RecentProjects = json;
                Properties.Settings.Default.Save();
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
        private bool HandleMissingFiles(ValidationResult result)
        {
            string message = "";

            if (result.MissingImages.Any())
            {
                message = $"The following {result.MissingImages.Count} image(s) are missing:\n\n";
                message += string.Join("\n", result.MissingImages.Take(5));

                if (result.MissingImages.Count > 5)
                {
                    message += $"\n... and {result.MissingImages.Count - 5} more";
                }

                message += "\n\n";
            }

            if (result.OrphanedLabels.Any())
            {
                if (message.Length > 0)
                    message += "\n";

                message += $"Found {result.OrphanedLabels.Count} orphaned label(s) without corresponding images.\n\n";
            }

            if (result.MissingImages.Any())
            {
                message += "Would you like to locate the missing images?";

                var dialogResult = MessageBox.Show(
                    message,
                    "Missing Files",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Warning);

                switch (dialogResult)
                {
                    case MessageBoxResult.Yes:
                        if (!TryRelocateImages(result.MissingImages))
                        {
                            // User canceled relocation
                            return false;
                        }
                        break;

                    case MessageBoxResult.No:
                        // Continue without missing files
                        break;

                    case MessageBoxResult.Cancel:
                        return false;
                }
            }
            else if (result.OrphanedLabels.Any())
            {
                var cleanupResult = MessageBox.Show(
                    message + "Clean up these orphaned labels?",
                    "Orphaned Labels",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (cleanupResult == MessageBoxResult.No)
                    return true;
            }

            // Clean up missing and orphaned data
            CleanupMissingFiles(result);
            return true;
        }

        /// <summary>
        /// Attempts to relocate missing images
        /// </summary>
        private bool TryRelocateImages(List<string> missingImages)
        {
            var result = MessageBox.Show(
                $"Looking for {missingImages.Count} missing image(s).\n\n" +
                "Have your images been moved to a new folder?\n" +
                "If yes, browse to the new location and we'll try to find them.",
                "Locate Images",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.OK)
                return false;

            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                ValidateNames = false,
                CheckFileExists = false,
                CheckPathExists = true,
                FileName = "Select Folder with Images",
                Title = "Locate Image Folder"
            };

            if (openFileDialog.ShowDialog() != true)
                return false;

            string newDirectory = Path.GetDirectoryName(openFileDialog.FileName);
            if (string.IsNullOrEmpty(newDirectory))
                return false;

            // Try to find missing images
            int foundCount = 0;
            foreach (var missingFileName in missingImages.ToList())
            {
                string newPath = Path.Combine(newDirectory, missingFileName);
                if (File.Exists(newPath))
                {
                    var imageRef = CurrentProject.Images.FirstOrDefault(img => img.FileName == missingFileName);
                    if (imageRef != null)
                    {
                        imageRef.FullPath = newPath;
                        foundCount++;
                    }
                }
            }

            if (foundCount > 0)
            {
                MessageBox.Show(
                    $"Successfully relocated {foundCount} out of {missingImages.Count} image(s)!",
                    "Images Found",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                MarkDirty();
                return true;
            }
            else
            {
                MessageBox.Show(
                    "No matching images found in the selected folder.",
                    "Images Not Found",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return false;
            }
        }

        /// <summary>
        /// Removes missing images and orphaned labels
        /// </summary>
        private void CleanupMissingFiles(ValidationResult result)
        {
            bool cleaned = false;

            if (result.MissingImages.Any())
            {
                CurrentProject.Images.RemoveAll(img => result.MissingImages.Contains(img.FileName));
                foreach (var fileName in result.MissingImages)
                {
                    CurrentProject.ImageStatuses.Remove(fileName);
                }
                cleaned = true;
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
            }

            if (cleaned)
            {
                MarkDirty();
            }
        }

        /// <summary>
        /// OPTIMIZED: Validates and corrects image statuses asynchronously with batch processing
        /// Uses settings-based batch size for optimal performance with user-friendly progress messages
        /// </summary>
        private async Task ValidateAndCorrectStatusesAsync(
            IProgress<(int current, int total, string message)> progress = null,
            CancellationToken cancellationToken = default)
        {
            if (mainWindow == null) return;

            var statusKeys = CurrentProject.ImageStatuses.Keys.ToArray();
            int totalImages = statusKeys.Length;

            if (totalImages == 0)
            {
                return;
            }

            int corrected = 0;
            int processed = 0;

            // Use batch size from settings instead of hardcoding
            int batchSize = Properties.Settings.Default.ProcessingBatchSize;
            if (batchSize <= 0) batchSize = 1000; // Fallback to safe default

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

                        if (!mainWindow.labelManager.LabelStorage.ContainsKey(fileName) ||
                            mainWindow.labelManager.LabelStorage[fileName].Count == 0)
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
                                var labels = mainWindow.labelManager.LabelStorage[fileName];
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
                            mainWindow.imageManager.UpdateImageStatusValue(fileName, correctedStatus);
                            corrected++;
                        }

                        processed++;
                    }

                    // Report progress after each batch with clear messaging
                    // Map to 90-100% range for finalization phase
                    int overallProgress = 90 + (int)((processed / (double)totalImages) * 10);
                    progress?.Report((overallProgress, 100,
                        $"Finalizing... {processed}/{totalImages} validated"));
                }

            }, cancellationToken);
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
}