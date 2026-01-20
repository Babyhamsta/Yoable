using Microsoft.Win32;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using YoableWPF.Managers;
using YoableWPF.Models;

namespace YoableWPF
{
    public partial class StartupWindow : Window
    {
        private ProjectManager projectManager;
        private CancellationTokenSource loadCancellationToken;

        public StartupWindow()
        {
            InitializeComponent();

            // Initialize temporary project manager (just for loading recent projects)
            projectManager = new ProjectManager(null);

            // Load recent projects
            LoadRecentProjects();

            // Show current app version
            VersionText.Text = $"Version {VersionInfo.CurrentVersion}";
        }

        private void LoadRecentProjects()
        {
            var recentProjects = projectManager.GetRecentProjects();

            if (recentProjects.Count > 0)
            {
                RecentProjectsList.ItemsSource = recentProjects;
                EmptyStatePanel.Visibility = Visibility.Collapsed;
            }
            else
            {
                EmptyStatePanel.Visibility = Visibility.Visible;
            }
        }

        private async void ProjectCard_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.Tag is string projectPath)
            {
                if (File.Exists(projectPath))
                {
                    await OpenProjectAndLaunchMainWindowAsync(projectPath);
                }
                else
                {
                    var result = MessageBox.Show(
                        $"Project file not found:\n{projectPath}\n\nRemove from recent projects?",
                        "Project Not Found",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                    if (result == MessageBoxResult.Yes)
                    {
                        projectManager.RemoveFromRecentProjects(projectPath);
                        LoadRecentProjects(); // Refresh the list
                    }
                }
            }
        }

        private void NewProject_Click(object sender, RoutedEventArgs e)
        {
            var newProjectDialog = new NewProjectDialog();
            newProjectDialog.Owner = this;

            if (newProjectDialog.ShowDialog() == true)
            {
                string projectName = newProjectDialog.ProjectName;
                string projectLocation = newProjectDialog.ProjectLocation;

                // Create MainWindow first
                var mainWindow = new MainWindow();

                // Create project manager with reference to main window
                var projectMgr = new ProjectManager(mainWindow);

                if (projectMgr.CreateNewProject(projectName, projectLocation))
                {
                    // Set up the main window with the new project
                    mainWindow.projectManager = projectMgr;
                    mainWindow.ProjectNameText.Text = projectName;
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

        private async void OpenProject_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "Yoable Project Files (*.yoable)|*.yoable|All Files (*.*)|*.*",
                Title = "Open Project"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                await OpenProjectAndLaunchMainWindowAsync(openFileDialog.FileName);
            }
        }

        private async Task OpenProjectAndLaunchMainWindowAsync(string projectPath)
        {
            MainWindow mainWindow = null;
            ProjectManager projectMgr = null;
            OverlayManager overlayManager = null;

            try
            {
                // Create MainWindow first
                mainWindow = new MainWindow();

                // Create overlay manager for progress feedback
                overlayManager = new OverlayManager(mainWindow);

                // Create cancellation token for the loading process
                loadCancellationToken = new CancellationTokenSource();

                // Show the window (but keep it disabled during loading)
                mainWindow.IsEnabled = false;
                mainWindow.Show();

                // Show loading overlay
                overlayManager.ShowOverlayWithProgress("Loading project...", loadCancellationToken);

                // Create project manager
                projectMgr = new ProjectManager(mainWindow);

                // Create progress reporter
                var progress = new Progress<(int current, int total, string message)>(report =>
                {
                    mainWindow.Dispatcher.Invoke(() =>
                    {
                        overlayManager.UpdateProgress(report.current);
                        overlayManager.UpdateMessage(report.message);
                    });
                });

                // Load the project with progress feedback
                bool loaded = await projectMgr.LoadProjectAsync(projectPath, progress);

                if (!loaded)
                {
                    overlayManager.HideOverlay();
                    mainWindow.Close();
                    return;
                }

                // Set up the main window with the loaded project
                mainWindow.projectManager = projectMgr;

                // Update message for import phase
                overlayManager.UpdateMessage("Importing project data...");

                // Import project data into main window with progress
                await projectMgr.ImportProjectDataAsync(progress, loadCancellationToken.Token);

                // Update UI to reflect loaded data
                await mainWindow.Dispatcher.InvokeAsync(() =>
                {
                    mainWindow.RefreshUIAfterProjectLoadAsync();
                    mainWindow.ProjectNameText.Text = projectMgr.CurrentProject.ProjectName;
                    mainWindow.UpdateProjectUI();
                });

                // Start auto-save
                projectMgr.StartAutoSave();

                // Hide overlay and enable window
                overlayManager.HideOverlay();
                mainWindow.IsEnabled = true;

                // Close startup window
                this.Close();
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("Project loading was canceled by user");
                overlayManager?.HideOverlay();
                mainWindow?.Close();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading project: {ex.Message}");
                overlayManager?.HideOverlay();

                MessageBox.Show(
                    $"Failed to load project:\n\n{ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                mainWindow?.Close();
            }
        }

        private void OpenProjectFolder_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string projectPath)
            {
                try
                {
                    // Get the directory containing the project file
                    string folderPath = Path.GetDirectoryName(projectPath);

                    if (Directory.Exists(folderPath))
                    {
                        // Open Windows Explorer at the folder location and select the file
                        Process.Start("explorer.exe", $"/select,\"{projectPath}\"");
                    }
                    else
                    {
                        MessageBox.Show(
                            $"Project folder not found:\n{folderPath}",
                            "Folder Not Found",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        $"Failed to open project folder:\n\n{ex.Message}",
                        "Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
        }

        private void DeleteProject_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string projectPath)
            {
                var result = MessageBox.Show(
                    $"Are you sure you want to delete this project?\n\n{projectPath}\n\nThis action cannot be undone.",
                    "Delete Project",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    try
                    {
                        // Delete the project file
                        if (File.Exists(projectPath))
                        {
                            File.Delete(projectPath);
                        }

                        // Remove from recent projects
                        projectManager.RemoveFromRecentProjects(projectPath);

                        // Refresh the list
                        LoadRecentProjects();

                        MessageBox.Show(
                            "Project deleted successfully.",
                            "Success",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(
                            $"Failed to delete project:\n\n{ex.Message}",
                            "Error",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                    }
                }
            }
        }

        private void ContinueWithoutProject_Click(object sender, RoutedEventArgs e)
        {
            // Launch main window without a project
            var mainWindow = new MainWindow();
            mainWindow.projectManager = new ProjectManager(mainWindow);
            mainWindow.Show();
            this.Close();
        }
    }
}
