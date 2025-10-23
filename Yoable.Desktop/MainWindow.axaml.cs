using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Yoable.Managers;
using Yoable.Models;
using Yoable.Services;

namespace Yoable.Desktop;

public partial class MainWindow : Window
{
    // Managers
    private ImageManager _imageManager = null!;
    private LabelManager _labelManager = null!;
    private ProjectManager? _projectManager;
    private UIStateManager _uiStateManager = null!;
    private YoloAI _yoloAI = null!;
    private YoutubeDownloader _youtubeDownloader = null!;

    // Services
    private readonly IDialogService _dialogService;
    private readonly IFileService _fileService;
    private readonly IImageService _imageService;
    private readonly ISettingsService _settingsService;
    private readonly IDispatcherService _dispatcherService;

    // UI Controls
    private ListBox? _imageListBox;
    private ListBox? _labelListBox;
    private Controls.DrawingCanvas? _drawingCanvas;
    private ComboBox? _sortComboBox;
    private Button? _filterAllButton;
    private Button? _filterReviewButton;
    private Button? _filterNoLabelButton;
    private Button? _filterVerifiedButton;
    private TextBlock? _projectNameText;
    private TextBlock? _lastSaveText;
    private TextBlock? _lastSaveTimeText;
    private TextBlock? _needsReviewCount;
    private TextBlock? _unverifiedCount;
    private TextBlock? _verifiedCount;
    private TextBlock? _autoSaveText;
    private Border? _saveStatusBorder;

    // Menu items
    private MenuItem? _newProjectMenuItem;
    private MenuItem? _openProjectMenuItem;
    private MenuItem? _saveProjectMenuItem;
    private MenuItem? _saveProjectAsMenuItem;
    private MenuItem? _closeProjectMenuItem;
    private MenuItem? _importDirectoryMenuItem;
    private MenuItem? _importImageMenuItem;
    private MenuItem? _importLabelsMenuItem;
    private MenuItem? _youtubeToImagesMenuItem;
    private MenuItem? _exportLabelsMenuItem;
    private MenuItem? _clearAllMenuItem;
    private MenuItem? _manageModelsMenuItem;
    private MenuItem? _autoLabelImagesMenuItem;
    private MenuItem? _autoSuggestLabelsMenuItem;
    private MenuItem? _settingsMenuItem;

    public MainWindow()
    {
        InitializeComponent();

        // Initialize services
        _dialogService = new AvaloniaDialogService();
        _fileService = new AvaloniaFileService();
        _imageService = new OpenCvImageService();
        _settingsService = new JsonSettingsService();
        _dispatcherService = new AvaloniaDispatcherService();

        // Initialize managers
        _imageManager = new ImageManager(_imageService);
        _labelManager = new LabelManager();
        _uiStateManager = new UIStateManager();
        _yoloAI = new YoloAI();
        _youtubeDownloader = new YoutubeDownloader(_dialogService);

        // Get UI controls
        GetControls();

        // Wire up event handlers
        WireUpEventHandlers();

        // Load settings
        LoadSettings();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void GetControls()
    {
        // Lists
        _imageListBox = this.FindControl<ListBox>("ImageListBox");
        _labelListBox = this.FindControl<ListBox>("LabelListBox");
        _drawingCanvas = this.FindControl<Controls.DrawingCanvas>("DrawingCanvas");

        // Wire up DrawingCanvas to LabelListBox
        if (_drawingCanvas != null && _labelListBox != null)
        {
            _drawingCanvas.LabelListBox = _labelListBox;

            // Subscribe to LabelsChanged event
            _drawingCanvas.LabelsChanged += (sender, e) =>
            {
                if (_projectManager != null && _projectManager.IsProjectOpen)
                {
                    MarkProjectDirty();
                }
            };
        }

        // Sort and filter controls
        _sortComboBox = this.FindControl<ComboBox>("SortComboBox");
        _filterAllButton = this.FindControl<Button>("FilterAllButton");
        _filterReviewButton = this.FindControl<Button>("FilterReviewButton");
        _filterNoLabelButton = this.FindControl<Button>("FilterNoLabelButton");
        _filterVerifiedButton = this.FindControl<Button>("FilterVerifiedButton");

        // Status bar controls
        _projectNameText = this.FindControl<TextBlock>("ProjectNameText");
        _lastSaveText = this.FindControl<TextBlock>("LastSaveText");
        _lastSaveTimeText = this.FindControl<TextBlock>("LastSaveTimeText");
        _needsReviewCount = this.FindControl<TextBlock>("NeedsReviewCount");
        _unverifiedCount = this.FindControl<TextBlock>("UnverifiedCount");
        _verifiedCount = this.FindControl<TextBlock>("VerifiedCount");
        _autoSaveText = this.FindControl<TextBlock>("AutoSaveText");
        _saveStatusBorder = this.FindControl<Border>("SaveStatusBorder");

        // Menu items
        _newProjectMenuItem = this.FindControl<MenuItem>("NewProjectMenuItem");
        _openProjectMenuItem = this.FindControl<MenuItem>("OpenProjectMenuItem");
        _saveProjectMenuItem = this.FindControl<MenuItem>("SaveProjectMenuItem");
        _saveProjectAsMenuItem = this.FindControl<MenuItem>("SaveProjectAsMenuItem");
        _closeProjectMenuItem = this.FindControl<MenuItem>("CloseProjectMenuItem");
        _importDirectoryMenuItem = this.FindControl<MenuItem>("ImportDirectoryMenuItem");
        _importImageMenuItem = this.FindControl<MenuItem>("ImportImageMenuItem");
        _importLabelsMenuItem = this.FindControl<MenuItem>("ImportLabelsMenuItem");
        _youtubeToImagesMenuItem = this.FindControl<MenuItem>("YoutubeToImagesMenuItem");
        _exportLabelsMenuItem = this.FindControl<MenuItem>("ExportLabelsMenuItem");
        _clearAllMenuItem = this.FindControl<MenuItem>("ClearAllMenuItem");
        _manageModelsMenuItem = this.FindControl<MenuItem>("ManageModelsMenuItem");
        _autoLabelImagesMenuItem = this.FindControl<MenuItem>("AutoLabelImagesMenuItem");
        _autoSuggestLabelsMenuItem = this.FindControl<MenuItem>("AutoSuggestLabelsMenuItem");
        _settingsMenuItem = this.FindControl<MenuItem>("SettingsMenuItem");
    }

    private void WireUpEventHandlers()
    {
        // Menu events
        if (_newProjectMenuItem != null)
            _newProjectMenuItem.Click += NewProject_Click;
        if (_openProjectMenuItem != null)
            _openProjectMenuItem.Click += OpenProject_Click;
        if (_saveProjectMenuItem != null)
            _saveProjectMenuItem.Click += SaveProject_Click;
        if (_saveProjectAsMenuItem != null)
            _saveProjectAsMenuItem.Click += SaveProjectAs_Click;
        if (_closeProjectMenuItem != null)
            _closeProjectMenuItem.Click += CloseProject_Click;
        if (_importDirectoryMenuItem != null)
            _importDirectoryMenuItem.Click += ImportDirectory_Click;
        if (_importImageMenuItem != null)
            _importImageMenuItem.Click += ImportImage_Click;
        if (_importLabelsMenuItem != null)
            _importLabelsMenuItem.Click += ImportLabels_Click;
        if (_youtubeToImagesMenuItem != null)
            _youtubeToImagesMenuItem.Click += YoutubeToImages_Click;
        if (_exportLabelsMenuItem != null)
            _exportLabelsMenuItem.Click += ExportLabels_Click;
        if (_clearAllMenuItem != null)
            _clearAllMenuItem.Click += ClearAll_Click;
        if (_manageModelsMenuItem != null)
            _manageModelsMenuItem.Click += ManageModels_Click;
        if (_autoLabelImagesMenuItem != null)
            _autoLabelImagesMenuItem.Click += AutoLabelImages_Click;
        if (_autoSuggestLabelsMenuItem != null)
            _autoSuggestLabelsMenuItem.Click += AutoSuggestLabels_Click;
        if (_settingsMenuItem != null)
            _settingsMenuItem.Click += Settings_Click;

        // Sort and filter events
        if (_sortComboBox != null)
            _sortComboBox.SelectionChanged += SortComboBox_SelectionChanged;
        if (_filterAllButton != null)
            _filterAllButton.Click += FilterAll_Click;
        if (_filterReviewButton != null)
            _filterReviewButton.Click += FilterReview_Click;
        if (_filterNoLabelButton != null)
            _filterNoLabelButton.Click += FilterNoLabel_Click;
        if (_filterVerifiedButton != null)
            _filterVerifiedButton.Click += FilterVerified_Click;

        // List selection events
        if (_imageListBox != null)
            _imageListBox.SelectionChanged += ImageListBox_SelectionChanged;
        if (_labelListBox != null)
            _labelListBox.SelectionChanged += LabelListBox_SelectionChanged;

        // Window events
        this.Closing += Window_Closing;
        this.KeyDown += Window_KeyDown;
    }

    private void LoadSettings()
    {
        // TODO: Load theme and accent color settings when settings are fully implemented
        // For now, just use defaults
    }

    #region Helper Methods

    /// <summary>
    /// Prompts user to save changes if there are unsaved changes.
    /// Returns true if it's safe to proceed (changes saved or discarded).
    /// Returns false if user cancelled.
    /// </summary>
    private async Task<bool> PromptToSaveChangesAsync(string actionDescription = "continue")
    {
        if (_projectManager?.IsProjectOpen != true || !_projectManager.HasUnsavedChanges)
            return true; // No unsaved changes, safe to proceed

        var result = await _dialogService.ShowYesNoCancelAsync(
            "Unsaved Changes",
            $"Save changes to the current project before {actionDescription}?");

        if (result == DialogResult.Cancel)
            return false; // User cancelled

        if (result == DialogResult.Yes)
        {
            // Save the project before proceeding
            if (_projectManager != null && _projectManager.IsProjectOpen && !_projectManager.IsSaving)
            {
                await _projectManager.SaveProjectAsync();
            }
        }

        return true; // User chose No or Yes (and save completed)
    }

    /// <summary>
    /// Creates a progress reporter for updates
    /// </summary>
    private IProgress<(int current, int total, string message)> CreateProgressReporter()
    {
        return new Progress<(int current, int total, string message)>(report =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                // TODO: Update progress UI when overlay manager is ported
                Debug.WriteLine($"Progress: {report.current}/{report.total} - {report.message}");
            });
        });
    }

    /// <summary>
    /// Marks the project as having unsaved changes
    /// </summary>
    private void MarkProjectDirty()
    {
        if (_projectManager != null && _projectManager.IsProjectOpen)
        {
            _projectManager.MarkDirty();
            UpdateProjectUI();
        }
    }

    #endregion

    #region Project Methods

    private async void NewProject_Click(object? sender, RoutedEventArgs e)
    {
        if (!await PromptToSaveChangesAsync("creating a new project"))
            return;

        var newProjectDialog = new NewProjectDialog();
        var result = await newProjectDialog.ShowDialog<bool>(this);

        if (result && newProjectDialog.ProjectName != null && newProjectDialog.ProjectLocation != null)
        {
            string projectName = newProjectDialog.ProjectName;
            string projectLocation = newProjectDialog.ProjectLocation;

            // Close current project if any
            if (_projectManager != null)
            {
                await _projectManager.CloseProjectAsync(false);
            }
            else
            {
                _projectManager = new ProjectManager(_dialogService, _fileService, _settingsService, _dispatcherService);
            }

            if (await _projectManager.CreateNewProjectAsync(projectName, projectLocation))
            {
                if (_projectNameText != null)
                    _projectNameText.Text = projectName;

                Title = $"Yoable - {projectName}";
                UpdateProjectUI();
                _projectManager.StartAutoSave();
            }
        }
    }

    private async void OpenProject_Click(object? sender, RoutedEventArgs e)
    {
        if (!await PromptToSaveChangesAsync("opening another project"))
            return;

        var projectPath = await _fileService.OpenFileAsync(
            "Open Project",
            new[] { new FileFilter("Yoable Project Files", "yoable") });

        if (projectPath != null)
        {
            // Close current project if any
            if (_projectManager != null)
            {
                await _projectManager.CloseProjectAsync(false);
            }
            else
            {
                _projectManager = new ProjectManager(_dialogService, _fileService, _settingsService, _dispatcherService);
            }

            await LoadProjectWithProgressAsync(projectPath);
        }
    }

    /// <summary>
    /// Loads a project asynchronously with progress feedback
    /// </summary>
    private async Task LoadProjectWithProgressAsync(string projectPath)
    {
        if (_projectManager == null)
            return;

        try
        {
            // TODO: Show loading overlay when OverlayManager is ported
            Title = "Loading project...";
            IsEnabled = false;

            var progress = CreateProgressReporter();

            // Load the project with progress feedback
            bool loaded = await _projectManager.LoadProjectAsync(projectPath, progress);

            if (!loaded)
            {
                IsEnabled = true;
                Title = "Yoable";
                return;
            }

            // Import project data into main window with progress
            await _projectManager.ImportProjectDataAsync(progress, CancellationToken.None);

            // Update all image statuses based on loaded labels
            await UpdateAllImageStatusesAsync();

            // Update UI to reflect loaded data
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                RefreshUIAfterProjectLoadAsync();
                if (_projectNameText != null && _projectManager.CurrentProject != null)
                    _projectNameText.Text = _projectManager.CurrentProject.ProjectName;

                if (_projectManager.CurrentProject != null)
                    Title = $"Yoable - {_projectManager.CurrentProject.ProjectName}";

                UpdateProjectUI();
            });

            // Start auto-save
            _projectManager.StartAutoSave();

            IsEnabled = true;
        }
        catch (OperationCanceledException)
        {
            Debug.WriteLine("Project loading was canceled by user");
            IsEnabled = true;
            Title = "Yoable";
            await _dialogService.ShowMessageAsync("Canceled", "Project loading was canceled.");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error loading project: {ex.Message}");
            IsEnabled = true;
            Title = "Yoable";
            await _dialogService.ShowErrorAsync("Error", $"Failed to load project:\n\n{ex.Message}");
        }
    }

    private async void SaveProject_Click(object? sender, RoutedEventArgs? e)
    {
        if (_projectManager == null || !_projectManager.IsProjectOpen)
        {
            await _dialogService.ShowMessageAsync(
                "No Project",
                "No project is currently open. Create a new project or open an existing one.");
            return;
        }

        // Prevent concurrent saves
        if (_projectManager.IsSaving)
        {
            return; // Already saving, ignore this request
        }

        // Use async save with progress
        bool success = await _projectManager.SaveProjectAsync();

        if (success)
        {
            // Update UI to reflect saved state
            UpdateProjectUI();
        }
    }

    private async void SaveProjectAs_Click(object? sender, RoutedEventArgs e)
    {
        if (_projectManager == null || !_projectManager.IsProjectOpen)
        {
            await _dialogService.ShowMessageAsync("No Project", "No project is currently open.");
            return;
        }

        var savePath = await _fileService.SaveFileAsync(
            "Save Project As",
            _projectManager.CurrentProject?.ProjectName ?? "Project",
            new[] { new FileFilter("Yoable Project Files", "yoable") });

        if (savePath != null)
        {
            try
            {
                // TODO: Show overlay when OverlayManager is ported
                Title = "Saving project...";

                // Export current state to project
                _projectManager.ExportProjectData();

                // Save to new location
                if (await _projectManager.SaveProjectAsAsync(savePath))
                {
                    if (_projectNameText != null && _projectManager.CurrentProject != null)
                        _projectNameText.Text = _projectManager.CurrentProject.ProjectName;

                    if (_projectManager.CurrentProject != null)
                        Title = $"Yoable - {_projectManager.CurrentProject.ProjectName}";

                    UpdateProjectUI();
                }
            }
            finally
            {
                if (_projectManager.CurrentProject != null)
                    Title = $"Yoable - {_projectManager.CurrentProject.ProjectName}";
            }
        }
    }

    private async void CloseProject_Click(object? sender, RoutedEventArgs e)
    {
        if (_projectManager == null || !_projectManager.IsProjectOpen)
        {
            await _dialogService.ShowMessageAsync("No Project", "No project is currently open.");
            return;
        }

        if (await _projectManager.CloseProjectAsync())
        {
            // Clear UI completely
            if (_projectNameText != null)
                _projectNameText.Text = "No Project";
            if (_lastSaveText != null)
                _lastSaveText.Text = "Not saved";
            if (_lastSaveTimeText != null)
                _lastSaveTimeText.Text = "";

            Title = "Yoable";

            // Clear all data
            _imageListBox?.Items.Clear();
            _labelListBox?.Items.Clear();
            if (_drawingCanvas != null)
            {
                _drawingCanvas.Labels.Clear();
                _drawingCanvas.InvalidateVisual();
            }

            // Update project UI
            UpdateProjectUI();

            // Update status counts
            UpdateStatusCounts();
        }
    }

    /// <summary>
    /// Updates the project UI indicators (save status, auto-save, etc.)
    /// </summary>
    public void UpdateProjectUI()
    {
        if (_projectManager == null || !_projectManager.IsProjectOpen)
        {
            // No project mode
            if (_saveStatusBorder != null)
                _saveStatusBorder.Background = new SolidColorBrush(Color.FromArgb(0x44, 0x9E, 0x9E, 0x9E));
            if (_lastSaveText != null)
            {
                _lastSaveText.Text = "No project";
                _lastSaveText.Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0x9E, 0x9E, 0x9E));
            }
            if (_lastSaveTimeText != null)
                _lastSaveTimeText.Text = "";
            if (_autoSaveText != null)
                _autoSaveText.Text = "Auto-save disabled";

            return;
        }

        // Update save status
        if (_projectManager.HasUnsavedChanges)
        {
            if (_saveStatusBorder != null)
                _saveStatusBorder.Background = new SolidColorBrush(Color.FromArgb(0x44, 0xFF, 0xB7, 0x4D));
            if (_lastSaveText != null)
            {
                _lastSaveText.Text = "Unsaved changes";
                _lastSaveText.Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0xB7, 0x4D));
            }
        }
        else
        {
            if (_saveStatusBorder != null)
                _saveStatusBorder.Background = new SolidColorBrush(Color.FromArgb(0x44, 0x81, 0xC7, 0x84));
            if (_lastSaveText != null)
            {
                _lastSaveText.Text = "All changes saved";
                _lastSaveText.Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0x81, 0xC7, 0x84));
            }
        }

        // Update last save time
        if (_lastSaveTimeText != null && _projectManager.LastSaveTime != DateTime.MinValue)
        {
            TimeSpan timeSince = DateTime.Now - _projectManager.LastSaveTime;
            if (timeSince.TotalSeconds < 5)
                _lastSaveTimeText.Text = "Saved just now";
            else if (timeSince.TotalMinutes < 1)
                _lastSaveTimeText.Text = $"Saved {(int)timeSince.TotalSeconds} seconds ago";
            else if (timeSince.TotalMinutes < 60)
                _lastSaveTimeText.Text = $"Saved {(int)timeSince.TotalMinutes} minutes ago";
            else if (timeSince.TotalHours < 24)
                _lastSaveTimeText.Text = $"Saved {(int)timeSince.TotalHours} hours ago";
            else
                _lastSaveTimeText.Text = $"Saved on {_projectManager.LastSaveTime:MMM dd}";
        }
        else if (_lastSaveTimeText != null)
        {
            _lastSaveTimeText.Text = "Not saved yet";
        }

        // Update auto-save indicator
        // TODO: Get auto-save setting from settings service
        bool autoSaveEnabled = true; // Default for now
        if (_autoSaveText != null)
            _autoSaveText.Text = autoSaveEnabled ? "Auto-save enabled" : "Auto-save disabled";
    }

    /// <summary>
    /// Refreshes the UI after loading a project
    /// </summary>
    public async Task RefreshUIAfterProjectLoadAsync()
    {
        if (_imageListBox == null)
            return;

        // Clear the list first
        _imageListBox.Items.Clear();

        // Get all images
        var allImages = _imageManager.ImagePathMap.Keys.ToArray();

        if (allImages.Length == 0)
        {
            UpdateStatusCounts();
            return;
        }

        // Create all items in memory first
        var allItems = new List<ImageListItem>(allImages.Length);

        await Task.Run(() =>
        {
            foreach (var fileName in allImages)
            {
                var status = _imageManager.GetImageStatus(fileName);
                allItems.Add(new ImageListItem(fileName, status));
            }
        });

        // Add all items to the listbox in batches
        int uiBatchSize = 100; // TODO: Get from settings
        for (int i = 0; i < allItems.Count; i += uiBatchSize)
        {
            var batch = allItems.Skip(i).Take(uiBatchSize);
            foreach (var item in batch)
            {
                _imageListBox.Items.Add(item);
            }

            if (i % (uiBatchSize * 5) == 0 && i > 0)
            {
                await Task.Delay(1);
            }
        }

        // Build the cache for O(1) lookups
        _uiStateManager.BuildCache(_imageListBox.Items.Cast<ImageListItem>());

        // Update status counts
        UpdateStatusCounts();

        // Apply saved sort mode
        if (_projectManager?.CurrentProject != null && _sortComboBox != null)
        {
            if (_projectManager.CurrentProject.CurrentSortMode == "ByStatus")
            {
                _sortComboBox.SelectedIndex = 1;
                ApplySortMode(UIStateManager.SortMode.ByStatus);
            }
            else
            {
                _sortComboBox.SelectedIndex = 0;
                ApplySortMode(UIStateManager.SortMode.ByName);
            }
        }

        // Select the saved image index if any
        if (_imageListBox.Items.Count > 0)
        {
            int targetIndex = _projectManager?.CurrentProject?.LastSelectedImageIndex ?? 0;
            if (targetIndex >= _imageListBox.Items.Count)
                targetIndex = 0;

            await Task.Delay(50);
            _imageListBox.SelectedIndex = targetIndex;

            if (_imageListBox.SelectedItem != null)
                _imageListBox.ScrollIntoView(_imageListBox.SelectedItem);
        }
    }

    #endregion

    #region Image and Label List Handlers

    private void ImageListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_imageListBox?.SelectedItem is ImageListItem selected &&
            _imageManager.ImagePathMap.TryGetValue(selected.FileName, out var imageInfo) &&
            _drawingCanvas != null)
        {
            // Save existing labels for previously selected image
            if (!string.IsNullOrEmpty(_imageManager.CurrentImagePath))
            {
                _labelManager.SaveLabels(_imageManager.CurrentImagePath, _drawingCanvas.Labels);
                MarkProjectDirty();
            }

            // Update current image path
            _imageManager.CurrentImagePath = selected.FileName;

            // Load image into DrawingCanvas
            var avaloniaSize = new Avalonia.Size(imageInfo.OriginalDimensions.Width, imageInfo.OriginalDimensions.Height);
            _drawingCanvas.LoadImage(imageInfo.Path, avaloniaSize);

            // Reset zoom
            _drawingCanvas.ResetZoom();

            // Load labels for this image if they exist
            if (_labelManager.LabelStorage.ContainsKey(selected.FileName))
            {
                _drawingCanvas.Labels = new System.Collections.Generic.List<LabelData>(_labelManager.LabelStorage[selected.FileName]);

                // Update status when viewing an image with AI or imported labels
                if (_labelManager.LabelStorage[selected.FileName].Any(l =>
                    l.Name.StartsWith("AI") || l.Name.StartsWith("Imported")))
                {
                    var currentStatus = _imageManager.GetImageStatus(selected.FileName);
                    if (currentStatus == ImageStatus.VerificationNeeded)
                    {
                        _imageManager.UpdateImageStatusValue(selected.FileName, ImageStatus.Verified);
                        selected.Status = ImageStatus.Verified;
                    }
                }
            }
            else
            {
                _drawingCanvas.Labels.Clear();
            }

            // Update UI
            UpdateStatusCounts();
            RefreshLabelList();

            // Force canvas redraw
            _drawingCanvas.InvalidateVisual();
        }
    }

    private void LabelListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_labelListBox?.SelectedItem is string selectedLabelName && _drawingCanvas != null)
        {
            var selectedLabel = _drawingCanvas.Labels.FirstOrDefault(label => label.Name == selectedLabelName);

            if (selectedLabel != null)
            {
                _drawingCanvas.SelectedLabel = selectedLabel;
                _drawingCanvas.InvalidateVisual();
                _drawingCanvas.Focus(); // Ensure canvas captures key events
            }
        }
        else if (_drawingCanvas != null)
        {
            // No label selected, deselect in canvas
            _drawingCanvas.SelectedLabel = null;
            _drawingCanvas.InvalidateVisual();
        }
    }

    private void RefreshLabelList()
    {
        if (_labelListBox == null || _drawingCanvas == null)
            return;

        _labelListBox.Items.Clear();
        foreach (var label in _drawingCanvas.Labels)
        {
            _labelListBox.Items.Add(label.Name);
        }
    }

    #endregion

    #region File Import/Export

    private async void ImportDirectory_Click(object? sender, RoutedEventArgs e)
    {
        var folderPath = await _fileService.OpenFolderAsync("Select Directory");

        if (folderPath != null)
        {
            await LoadImagesAsync(folderPath);
        }
    }

    private async void ImportImage_Click(object? sender, RoutedEventArgs e)
    {
        var imagePath = await _fileService.OpenFileAsync(
            "Import Image",
            new[] { new FileFilter("Image Files", "jpg", "png", "jpeg") });

        if (imagePath != null)
        {
            if (_imageManager.AddImage(imagePath))
            {
                string fileName = Path.GetFileName(imagePath);
                await UpdateImageListInBatchesAsync(new[] { fileName }, CancellationToken.None);

                if (_imageListBox?.Items.Count == 1)
                {
                    _imageListBox.SelectedIndex = 0;
                }

                UpdateStatusCounts();
                MarkProjectDirty();
            }
        }
    }

    private async void ImportLabels_Click(object? sender, RoutedEventArgs e)
    {
        // Check if images are loaded first
        if (_imageManager.ImagePathMap.Count == 0)
        {
            await _dialogService.ShowErrorAsync(
                "Load Images First",
                "Please load images before importing labels.\n\nImages must be loaded first so label dimensions can be calculated correctly.");
            return;
        }

        var folderPath = await _fileService.OpenFolderAsync("Select Labels Directory");

        if (folderPath != null)
        {
            await LoadYOLOLabelsFromDirectory(folderPath);
        }
    }

    private async void YoutubeToImages_Click(object? sender, RoutedEventArgs e)
    {
        var youtubeWindow = new YoutubeDownloadWindow();
        var result = await youtubeWindow.ShowDialog<bool>(this);

        if (result)
        {
            var youtubeDownloader = new Yoable.Managers.YoutubeDownloader(_dialogService);

            var progressDialog = new Window
            {
                Title = "Downloading YouTube Video",
                Width = 450,
                Height = 180,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false
            };

            var panel = new StackPanel { Margin = new Avalonia.Thickness(20) };
            var progressText = new TextBlock
            {
                Text = "Initializing...",
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                Margin = new Avalonia.Thickness(0, 0, 0, 10)
            };
            var progressBar = new ProgressBar
            {
                Height = 30,
                Minimum = 0,
                Maximum = 100,
                Value = 0
            };

            panel.Children.Add(progressText);
            panel.Children.Add(progressBar);
            progressDialog.Content = panel;

            var cts = new System.Threading.CancellationTokenSource();
            progressDialog.Closing += (s, e) => cts.Cancel();

            var progress = new Progress<(int current, int total, string message)>(p =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    progressText.Text = p.message;
                    progressBar.Value = p.current;
                });
            });

            // Show progress dialog non-blocking
            progressDialog.Show(this);

            var framesDirectory = await youtubeDownloader.DownloadAndProcessVideoAsync(
                youtubeWindow.YoutubeUrl,
                youtubeWindow.DesiredFps,
                youtubeWindow.FrameSize,
                progress,
                cts.Token);

            progressDialog.Close();

            if (framesDirectory != null)
            {
                await LoadImagesAsync(framesDirectory);
                await _dialogService.ShowMessageAsync("Success", $"Extracted frames loaded from YouTube video");
            }
        }
    }

    private async void ExportLabels_Click(object? sender, RoutedEventArgs e)
    {
        var folderPath = await _fileService.OpenFolderAsync("Select Export Directory");

        if (folderPath != null)
        {
            await ExportLabelsToYoloAsync(folderPath);
        }
    }

    private async void ClearAll_Click(object? sender, RoutedEventArgs e)
    {
        var result = await _dialogService.ShowConfirmationAsync(
            "Clear All",
            "Are you sure you want to clear all images and labels?");

        if (result)
        {
            _imageManager.ClearAll();
            _labelManager.ClearAll();
            if (_drawingCanvas != null)
            {
                _drawingCanvas.Labels.Clear();
                _drawingCanvas.InvalidateVisual();
            }
            _imageListBox?.Items.Clear();
            _labelListBox?.Items.Clear();

            UpdateStatusCounts();
            MarkProjectDirty();
        }
    }

    #endregion

    #region AI Functions

    private async void ManageModels_Click(object? sender, RoutedEventArgs e)
    {
        var modelManagerDialog = new ModelManagerDialog(_yoloAI);
        await modelManagerDialog.ShowDialog(this);
    }

    private async void AutoLabelImages_Click(object? sender, RoutedEventArgs e)
    {
        // Check if any models are loaded
        int modelCount = _yoloAI.GetLoadedModelsCount();
        if (modelCount == 0)
        {
            var result = await _dialogService.ShowYesNoCancelAsync(
                "No Models",
                "No models loaded. Would you like to load models now?");

            if (result == DialogResult.Yes)
            {
                var modelManagerDialog = new ModelManagerDialog(_yoloAI);
                await modelManagerDialog.ShowDialog(this);
            }
            return;
        }

        // Show ensemble info if multiple models
        string processingMode = modelCount == 1 ? "single model" : $"ensemble ({modelCount} models)";
        string warningNote = modelCount > 3 ? "\n\nNote: Processing with many models may take considerable time." : "";

        var continueResult = await _dialogService.ShowYesNoCancelAsync(
            "Start Detection",
            $"Process {_imageManager.ImagePathMap.Count} images using {processingMode} detection?{warningNote}");

        if (continueResult != DialogResult.Yes) return;

        // Create progress dialog
        var progressDialog = new Window
        {
            Title = $"Running AI Detections ({processingMode})",
            Width = 450,
            Height = 180,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false
        };

        var panel = new StackPanel { Margin = new Avalonia.Thickness(20) };
        var progressText = new TextBlock
        {
            Text = "Initializing...",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Margin = new Avalonia.Thickness(0, 0, 0, 10)
        };
        var progressBar = new ProgressBar
        {
            Height = 30,
            Minimum = 0,
            Maximum = 100,
            Value = 0
        };

        panel.Children.Add(progressText);
        panel.Children.Add(progressBar);
        progressDialog.Content = panel;

        var cts = new System.Threading.CancellationTokenSource();
        progressDialog.Closing += (s, e) => cts.Cancel();

        // Show progress dialog non-blocking
        progressDialog.Show(this);

        int totalDetections = 0;
        int totalImages = _imageManager.ImagePathMap.Count;
        int processedImages = 0;

        await Task.Run(() =>
        {
            foreach (var imagePath in _imageManager.ImagePathMap.Values)
            {
                if (cts.Token.IsCancellationRequested) break;

                if (!File.Exists(imagePath.Path))
                {
                    continue;
                }

                using var image = System.Drawing.Image.FromFile(imagePath.Path);
                using var bitmap = new System.Drawing.Bitmap(image);
                string fileName = Path.GetFileName(imagePath.Path);
                var detectedBoxes = _yoloAI.RunInference(bitmap);

                // Convert System.Drawing.Rectangle to LabelRect
                var labelRects = detectedBoxes.Select(r => new Yoable.Models.LabelRect(r.X, r.Y, r.Width, r.Height)).ToList();
                _labelManager.AddAILabels(fileName, labelRects);
                totalDetections += labelRects.Count;

                processedImages++;
                Dispatcher.UIThread.Post(() =>
                {
                    progressBar.Value = (processedImages * 100) / totalImages;
                    progressText.Text = $"Processing image {processedImages}/{totalImages} ({processingMode})...";
                });
            }
        }, cts.Token);

        progressDialog.Close();

        await UpdateAllImageStatusesAsync();
        RefreshLabelList();
        _drawingCanvas?.InvalidateVisual();

        string modeInfo = modelCount == 1 ? "" : $" using {modelCount} models";
        await _dialogService.ShowMessageAsync("AI Labels",
            $"Auto-labeling complete{modeInfo}.\nTotal detections: {totalDetections}");

        MarkProjectDirty();
    }

    private async void AutoSuggestLabels_Click(object? sender, RoutedEventArgs e)
    {
        await _dialogService.ShowMessageAsync("Not Implemented",
            "Auto Suggest Labels feature is coming soon.\n\nThis will provide AI-powered label suggestions for the current image.");
    }

    #endregion

    #region Settings

    private async void Settings_Click(object? sender, RoutedEventArgs e)
    {
        var settingsWindow = new SettingsWindow(_yoloAI);
        await settingsWindow.ShowDialog(this);
    }

    #endregion

    #region Sort and Filter

    private void SortComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_uiStateManager == null || _sortComboBox == null)
            return;

        switch (_sortComboBox.SelectedIndex)
        {
            case 0:
                ApplySortMode(UIStateManager.SortMode.ByName);
                break;
            case 1:
                ApplySortMode(UIStateManager.SortMode.ByStatus);
                break;
        }
    }

    private void ApplySortMode(UIStateManager.SortMode sortMode)
    {
        if (_imageListBox == null)
            return;

        var items = _imageListBox.Items.Cast<ImageListItem>().ToList();

        List<ImageListItem> sortedItems = sortMode == UIStateManager.SortMode.ByName
            ? _uiStateManager.SortImagesByName(items)
            : _uiStateManager.SortImagesByStatus(items);

        _imageListBox.Items.Clear();
        foreach (var item in sortedItems)
        {
            _imageListBox.Items.Add(item);
        }
    }

    private void FilterAll_Click(object? sender, RoutedEventArgs e)
    {
        UpdateFilterButtonStyles(_filterAllButton);
        ApplyFilter(null);
    }

    private void FilterReview_Click(object? sender, RoutedEventArgs e)
    {
        UpdateFilterButtonStyles(_filterReviewButton);
        ApplyFilter(ImageStatus.VerificationNeeded);
    }

    private void FilterNoLabel_Click(object? sender, RoutedEventArgs e)
    {
        UpdateFilterButtonStyles(_filterNoLabelButton);
        ApplyFilter(ImageStatus.NoLabel);
    }

    private void FilterVerified_Click(object? sender, RoutedEventArgs e)
    {
        UpdateFilterButtonStyles(_filterVerifiedButton);
        ApplyFilter(ImageStatus.Verified);
    }

    private void UpdateFilterButtonStyles(Button? activeButton)
    {
        // Reset all buttons
        var buttons = new[] { _filterAllButton, _filterReviewButton, _filterNoLabelButton, _filterVerifiedButton };
        foreach (var button in buttons)
        {
            if (button != null)
            {
                // TODO: Update button styles when theme system is fully implemented
            }
        }
    }

    private void ApplyFilter(ImageStatus? filterStatus)
    {
        if (_imageListBox == null)
            return;

        var allItems = _uiStateManager.ImageListItemCache.Values.ToList();

        var filteredItems = filterStatus.HasValue
            ? allItems.Where(x => x.Status == filterStatus.Value).ToList()
            : allItems;

        _imageListBox.Items.Clear();
        foreach (var item in filteredItems)
        {
            _imageListBox.Items.Add(item);
        }

        UpdateStatusCounts();
    }

    #endregion

    #region Helper Methods for Loading

    private async Task LoadImagesAsync(string directoryPath)
    {
        if (!Directory.Exists(directoryPath))
            return;

        try
        {
            Title = "Loading images...";
            IsEnabled = false;

            var progress = CreateProgressReporter();

            // Load images asynchronously
            var files = await _imageManager.LoadImagesFromDirectoryAsync(
                directoryPath,
                progress,
                CancellationToken.None,
                enableParallelProcessing: true);

            // Update UI in batches
            await UpdateImageListInBatchesAsync(files, CancellationToken.None);

            if (_imageListBox?.Items.Count > 0)
            {
                _imageListBox.SelectedIndex = 0;
            }

            _uiStateManager.BuildCache(_imageListBox?.Items.Cast<ImageListItem>() ?? Enumerable.Empty<ImageListItem>());
            UpdateStatusCounts();

            MarkProjectDirty();
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("Error", $"Error loading images: {ex.Message}");
        }
        finally
        {
            IsEnabled = true;
            if (_projectManager?.CurrentProject != null)
                Title = $"Yoable - {_projectManager.CurrentProject.ProjectName}";
            else
                Title = "Yoable";
        }
    }

    private async Task UpdateImageListInBatchesAsync(string[] files, CancellationToken cancellationToken)
    {
        if (_imageListBox == null)
            return;

        int batchSize = 100; // TODO: Get from settings

        await Dispatcher.UIThread.InvokeAsync(() => _imageListBox.Items.Clear());

        for (int i = 0; i < files.Length; i += batchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var batch = files.Skip(i).Take(batchSize).ToArray();

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                foreach (string file in batch)
                {
                    string fileName = Path.GetFileName(file);
                    _imageListBox.Items.Add(new ImageListItem(fileName, ImageStatus.NoLabel));
                }
            });
        }
    }

    private async Task LoadYOLOLabelsFromDirectory(string directoryPath)
    {
        if (!Directory.Exists(directoryPath))
            return;

        // Search recursively in all subdirectories for label files
        string[] labelFiles = Directory.GetFiles(directoryPath, "*.txt", SearchOption.AllDirectories);
        int totalFiles = labelFiles.Length;

        if (totalFiles == 0)
        {
            await _dialogService.ShowMessageAsync("Label Import", "No YOLO label files found in the selected directory.");
            return;
        }

        try
        {
            Title = "Importing YOLO labels...";
            IsEnabled = false;

            var progress = CreateProgressReporter();

            // Load labels with batch processing
            int labelsLoaded = await _labelManager.LoadYoloLabelsBatchAsync(
                directoryPath,
                _imageManager,
                progress,
                CancellationToken.None,
                enableParallelProcessing: true);

            await UpdateAllImageStatusesAsync();

            // Reload labels for current image if one is selected
            if (!string.IsNullOrEmpty(_imageManager.CurrentImagePath) &&
                _labelManager.LabelStorage.ContainsKey(_imageManager.CurrentImagePath) &&
                _drawingCanvas != null)
            {
                _drawingCanvas.Labels = new System.Collections.Generic.List<LabelData>(
                    _labelManager.LabelStorage[_imageManager.CurrentImagePath]);
                RefreshLabelList();
                _drawingCanvas.InvalidateVisual();
            }

            await _dialogService.ShowMessageAsync("Label Import", $"Successfully imported {labelsLoaded} label files.");
            MarkProjectDirty();
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("Import Error", $"Error importing labels: {ex.Message}");
        }
        finally
        {
            IsEnabled = true;
            if (_projectManager?.CurrentProject != null)
                Title = $"Yoable - {_projectManager.CurrentProject.ProjectName}";
            else
                Title = "Yoable";
        }
    }

    private async Task ExportLabelsToYoloAsync(string exportDirectory)
    {
        if (string.IsNullOrEmpty(exportDirectory))
            return;

        try
        {
            Title = "Exporting labels...";
            IsEnabled = false;

            var progress = CreateProgressReporter();

            await _labelManager.ExportLabelsBatchAsync(
                exportDirectory,
                _imageManager,
                progress,
                CancellationToken.None);

            await _dialogService.ShowMessageAsync("Export Complete", "Labels exported successfully!");
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("Error", $"Export failed: {ex.Message}");
        }
        finally
        {
            IsEnabled = true;
            if (_projectManager?.CurrentProject != null)
                Title = $"Yoable - {_projectManager.CurrentProject.ProjectName}";
            else
                Title = "Yoable";
        }
    }

    private async Task UpdateAllImageStatusesAsync()
    {
        try
        {
            var statusUpdates = new ConcurrentDictionary<string, ImageStatus>();
            var allFiles = _imageManager.ImagePathMap.Keys.ToArray();

            await Task.Run(() =>
            {
                System.Threading.Tasks.Parallel.ForEach(allFiles,
                    new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
                    fileName =>
                    {
                        ImageStatus newStatus = DetermineImageStatus(fileName);
                        _imageManager.UpdateImageStatusValue(fileName, newStatus);
                        statusUpdates[fileName] = newStatus;
                    });
            });

            // Update UI in one batch
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                foreach (var kvp in statusUpdates)
                {
                    if (_uiStateManager.TryGetFromCache(kvp.Key, out var imageItem))
                    {
                        imageItem.Status = kvp.Value;
                    }
                }

                UpdateStatusCounts();
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error updating image statuses: {ex.Message}");
        }
    }

    private ImageStatus DetermineImageStatus(string fileName)
    {
        if (!_labelManager.LabelStorage.TryGetValue(fileName, out var labels) || labels.Count == 0)
            return ImageStatus.NoLabel;

        // Fast check for imported/AI labels
        for (int i = 0; i < labels.Count; i++)
        {
            var name = labels[i].Name;
            if (name.Length > 0 && (name[0] == 'I' || name[0] == 'A'))
            {
                if (name.StartsWith("Imported") || name.StartsWith("AI"))
                    return ImageStatus.VerificationNeeded;
            }
        }

        return ImageStatus.Verified;
    }

    #endregion

    #region UI Updates

    private void UpdateStatusCounts()
    {
        if (_imageListBox == null)
            return;

        var counts = _uiStateManager.CalculateStatusCounts(_imageListBox.Items.Cast<ImageListItem>());

        if (_needsReviewCount != null)
            _needsReviewCount.Text = counts.NeedsReview.ToString();
        if (_unverifiedCount != null)
            _unverifiedCount.Text = counts.Unverified.ToString();
        if (_verifiedCount != null)
            _verifiedCount.Text = counts.Verified.ToString();
    }

    #endregion

    #region Keyboard Shortcuts

    private async void Window_KeyDown(object? sender, KeyEventArgs e)
    {
        // Handle Ctrl+S for save
        if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.S)
        {
            // Inline save logic since we can't await async void
            if (_projectManager != null && _projectManager.IsProjectOpen && !_projectManager.IsSaving)
            {
                await _projectManager.SaveProjectAsync();
                UpdateProjectUI();
            }
            e.Handled = true;
            return;
        }

        // Handle number keys (1-9) for label selection
        if (e.Key >= Key.D1 && e.Key <= Key.D9)
        {
            int index = (int)e.Key - (int)Key.D1;
            if (_labelListBox != null && index < _labelListBox.Items.Count)
            {
                _labelListBox.SelectedIndex = index;
                e.Handled = true;
                return;
            }
        }

        // Handle Delete key for label deletion
        if (e.Key == Key.Delete && _labelListBox?.SelectedItem != null && _drawingCanvas != null)
        {
            string? labelName = _labelListBox.SelectedItem as string;
            if (labelName != null)
            {
                var labelToRemove = _drawingCanvas.Labels.FirstOrDefault(l => l.Name == labelName);
                if (labelToRemove != null)
                {
                    _drawingCanvas.Labels.Remove(labelToRemove);
                    RefreshLabelList();
                    _drawingCanvas.InvalidateVisual();
                    MarkProjectDirty();
                }
            }
            e.Handled = true;
        }
    }

    #endregion

    #region Window Closing

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        // Check for unsaved changes
        if (_projectManager != null && _projectManager.IsProjectOpen && _projectManager.HasUnsavedChanges)
        {
            e.Cancel = true; // Cancel the close temporarily

            var result = await _dialogService.ShowYesNoCancelAsync(
                "Unsaved Changes",
                $"Do you want to save changes to '{_projectManager.CurrentProject?.ProjectName}'?");

            if (result == DialogResult.Cancel)
            {
                return; // User cancelled, keep window open
            }

            if (result == DialogResult.Yes)
            {
                // Save synchronously
                _projectManager.ExportProjectData();
                bool saved = await _projectManager.SaveProjectAsync();

                if (!saved)
                {
                    return; // Save failed, keep window open
                }
            }

            // Now actually close
            _projectManager?.Dispose();
            _yoloAI?.Dispose();

            // Close for real this time
            this.Closing -= Window_Closing;
            this.Close();
        }
        else
        {
            // Cleanup
            _projectManager?.Dispose();
            _yoloAI?.Dispose();
        }
    }

    #endregion

    #region Public Methods for External Use

    public void SetProjectManager(ProjectManager projectManager)
    {
        _projectManager = projectManager;
    }

    public void SetProjectName(string projectName)
    {
        Title = $"Yoable - {projectName}";
        if (_projectNameText != null)
            _projectNameText.Text = projectName;
    }

    #endregion
}
