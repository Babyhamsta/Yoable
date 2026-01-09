using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace YoableWPF
{
    public partial class NewProjectDialog : Window
    {
        public string ProjectName { get; private set; }
        public string ProjectLocation { get; private set; }

        public NewProjectDialog()
        {
            InitializeComponent();

            // Set default location to user's Documents folder
            string defaultLocation = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            ProjectLocationTextBox.Text = defaultLocation;
            ProjectLocation = defaultLocation;

            // Focus on project name textbox
            Loaded += (s, e) => ProjectNameTextBox.Focus();
        }

        private void ProjectNameTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateCreateButtonState();
            UpdateFullPathPreview();
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            // Use FolderBrowserDialog equivalent
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                ValidateNames = false,
                CheckFileExists = false,
                CheckPathExists = true,
                FileName = "Select Folder"
            };

            if (dialog.ShowDialog() == true)
            {
                string folderPath = Path.GetDirectoryName(dialog.FileName);
                ProjectLocationTextBox.Text = folderPath;
                ProjectLocation = folderPath;
                UpdateCreateButtonState();
                UpdateFullPathPreview();
            }
        }

        private void CreateButton_Click(object sender, RoutedEventArgs e)
        {
            ProjectName = ProjectNameTextBox.Text.Trim();
            ProjectLocation = ProjectLocationTextBox.Text.Trim();

            // Validate project name
            if (string.IsNullOrWhiteSpace(ProjectName))
            {
                MessageBox.Show(
                    "Please enter a project name.",
                    "Project Name Required",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            // Check for invalid characters in project name
            if (ProjectName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                MessageBox.Show(
                    "Project name contains invalid characters.\nPlease use only valid file name characters.",
                    "Invalid Project Name",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            // Validate location
            if (string.IsNullOrWhiteSpace(ProjectLocation))
            {
                MessageBox.Show(
                    "Please select a project location.",
                    "Location Required",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (!Directory.Exists(ProjectLocation))
            {
                var result = MessageBox.Show(
                    $"The location '{ProjectLocation}' does not exist.\n\nDo you want to create it?",
                    "Create Location",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    try
                    {
                        Directory.CreateDirectory(ProjectLocation);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(
                            $"Failed to create location:\n\n{ex.Message}",
                            "Error",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                        return;
                    }
                }
                else
                {
                    return;
                }
            }

            // Check if project folder already exists and has content
            string projectFolder = Path.Combine(ProjectLocation, ProjectName);
            if (Directory.Exists(projectFolder))
            {
                string projectFile = Path.Combine(projectFolder, ProjectName + ".yoable");

                if (File.Exists(projectFile))
                {
                    var result = MessageBox.Show(
                        $"A project with this name already exists at this location.\n\nDo you want to open it instead?",
                        "Project Exists",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (result == MessageBoxResult.No)
                    {
                        return;
                    }
                }
            }

            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void UpdateCreateButtonState()
        {
            bool isValid = !string.IsNullOrWhiteSpace(ProjectNameTextBox.Text) &&
                          !string.IsNullOrWhiteSpace(ProjectLocationTextBox.Text);

            CreateButton.IsEnabled = isValid;
        }

        private void UpdateFullPathPreview()
        {
            if (!string.IsNullOrWhiteSpace(ProjectNameTextBox.Text) &&
                !string.IsNullOrWhiteSpace(ProjectLocationTextBox.Text))
            {
                string projectFolder = Path.Combine(ProjectLocationTextBox.Text, ProjectNameTextBox.Text.Trim());
                FullPathPreview.Text = $"Full path: {projectFolder}";
            }
            else
            {
                FullPathPreview.Text = "Full path: Not set";
            }
        }
    }
}