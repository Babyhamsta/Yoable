using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Yoable.Managers;
using Yoable.Models;
using Yoable.Services;

namespace Yoable.Desktop;

public partial class StartupWindow : Window
{
    private ProjectManager? _projectManager;
    private readonly IDialogService _dialogService;
    private readonly IFileService _fileService;
    private readonly IImageService _imageService;
    private readonly ISettingsService _settingsService;
    private readonly IDispatcherService _dispatcherService;

    public StartupWindow()
    {
        InitializeComponent();

        // Initialize services
        _dialogService = new AvaloniaDialogService();
        _fileService = new AvaloniaFileService();
        _imageService = new OpenCvImageService();
        _settingsService = new JsonSettingsService();
        _dispatcherService = new AvaloniaDispatcherService();

        // Get controls
        var newProjectButton = this.FindControl<Button>("NewProjectButton");
        var openProjectButton = this.FindControl<Button>("OpenProjectButton");
        var continueButton = this.FindControl<Button>("ContinueWithoutProjectButton");
        var projectsList = this.FindControl<ItemsControl>("RecentProjectsList");

        // Wire up event handlers
        if (newProjectButton != null)
            newProjectButton.Click += NewProject_Click;
        if (openProjectButton != null)
            openProjectButton.Click += OpenProject_Click;
        if (continueButton != null)
            continueButton.Click += ContinueWithoutProject_Click;

        // Handle item clicks on recent project cards
        if (projectsList != null)
            projectsList.AddHandler(PointerPressedEvent, RecentProjectCard_PointerPressed, RoutingStrategies.Tunnel);

        // Load recent projects
        LoadRecentProjects();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void LoadRecentProjects()
    {
        // Initialize temporary project manager (just for loading recent projects)
        _projectManager = new ProjectManager(_dialogService, _fileService, _settingsService, _dispatcherService);

        var recentProjects = _projectManager.GetRecentProjects();

        var projectsList = this.FindControl<ItemsControl>("RecentProjectsList");
        var emptyPanel = this.FindControl<Border>("EmptyStatePanel");

        if (recentProjects.Count > 0)
        {
            if (projectsList != null)
                projectsList.ItemsSource = recentProjects;
            if (emptyPanel != null)
                emptyPanel.IsVisible = false;
        }
        else
        {
            if (emptyPanel != null)
                emptyPanel.IsVisible = true;
        }
    }

    private async void RecentProjectCard_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // Find the Border that was clicked and get its DataContext
        var border = e.Source as Control;
        while (border != null && border is not Border)
        {
            border = border.Parent as Control;
        }

        if (border?.DataContext is RecentProjectInfo projectInfo)
        {
            if (File.Exists(projectInfo.ProjectPath))
            {
                await OpenProjectAndLaunchMainWindowAsync(projectInfo.ProjectPath);
            }
            else
            {
                var result = await _dialogService.ShowYesNoCancelAsync(
                    "Project Not Found",
                    $"Project file not found:\n{projectInfo.ProjectPath}\n\nRemove from recent projects?");

                if (result == DialogResult.Yes)
                {
                    _projectManager?.RemoveFromRecentProjects(projectInfo.ProjectPath);
                    LoadRecentProjects(); // Refresh the list
                }
            }
        }
    }

    private async void NewProject_Click(object? sender, RoutedEventArgs e)
    {
        var newProjectDialog = new NewProjectDialog();

        var result = await newProjectDialog.ShowDialog<bool>(this);

        if (result && newProjectDialog.ProjectName != null && newProjectDialog.ProjectLocation != null)
        {
            string projectName = newProjectDialog.ProjectName;
            string projectLocation = newProjectDialog.ProjectLocation;

            // Create MainWindow first
            var mainWindow = new MainWindow();

            // Create project manager with services
            var projectMgr = new ProjectManager(_dialogService, _fileService, _settingsService, _dispatcherService);

            var created = await projectMgr.CreateNewProjectAsync(projectName, projectLocation);

            if (created)
            {
                // Set up the main window with the new project
                mainWindow.SetProjectManager(projectMgr);
                mainWindow.SetProjectName(projectName);
                mainWindow.UpdateProjectUI();

                // Start auto-save
                projectMgr.StartAutoSave();

                // Show main window and close startup window
                mainWindow.Show();
                this.Close();
            }
            else
            {
                mainWindow.Close();
            }
        }
    }

    private async void OpenProject_Click(object? sender, RoutedEventArgs e)
    {
        var projectPath = await _fileService.OpenFileAsync(
            "Open Project",
            new[] { new FileFilter("Yoable Project Files", "yoable") });

        if (projectPath != null)
        {
            await OpenProjectAndLaunchMainWindowAsync(projectPath);
        }
    }

    private async Task OpenProjectAndLaunchMainWindowAsync(string projectPath)
    {
        MainWindow? mainWindow = null;
        ProjectManager? projectMgr = null;

        try
        {
            // Create MainWindow first
            mainWindow = new MainWindow();

            // Show the window (but keep it disabled during loading)
            mainWindow.IsEnabled = false;
            mainWindow.Show();

            // TODO: Show loading overlay (OverlayManager not yet migrated)
            // For now, just show a simple status in the title
            mainWindow.Title = "Loading project...";

            // Create project manager
            projectMgr = new ProjectManager(_dialogService, _fileService, _settingsService, _dispatcherService);

            // Create progress reporter
            var progress = new Progress<(int current, int total, string message)>(report =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    mainWindow.Title = $"Loading... {report.message}";
                });
            });

            // Load the project with progress feedback
            bool loaded = await projectMgr.LoadProjectAsync(projectPath, progress);

            if (!loaded)
            {
                mainWindow.Close();
                return;
            }

            // Set up the main window with the loaded project
            mainWindow.SetProjectManager(projectMgr);

            // Import project data into main window with progress
            await projectMgr.ImportProjectDataAsync(progress, CancellationToken.None);

            // Update UI to reflect loaded data
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                mainWindow.RefreshUIAfterProjectLoadAsync();
                mainWindow.SetProjectName(projectMgr.CurrentProject!.ProjectName);
                mainWindow.UpdateProjectUI();
                mainWindow.Title = $"Yoable - {projectMgr.CurrentProject.ProjectName}";
            });

            // Start auto-save
            projectMgr.StartAutoSave();

            // Enable window
            mainWindow.IsEnabled = true;

            // Close startup window
            this.Close();
        }
        catch (OperationCanceledException)
        {
            Debug.WriteLine("Project loading was canceled by user");
            mainWindow?.Close();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error loading project: {ex.Message}");

            await _dialogService.ShowErrorAsync(
                "Error",
                $"Failed to load project:\n\n{ex.Message}");

            mainWindow?.Close();
        }
    }

    private void ContinueWithoutProject_Click(object? sender, RoutedEventArgs e)
    {
        // Launch main window without a project
        var mainWindow = new MainWindow();
        var projectMgr = new ProjectManager(_dialogService, _fileService, _settingsService, _dispatcherService);
        mainWindow.SetProjectManager(projectMgr);
        mainWindow.Show();
        this.Close();
    }
}
