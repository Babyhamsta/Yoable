using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using YoableWPF.Managers;

namespace YoableWPF
{
    public partial class ImportLabelsAndImageDialog : Window
    {
        public string ImagesFolderPath { get; private set; }
        public string LabelsFolderPath { get; private set; }

        public ImportLabelsAndImageDialog()
        {
            InitializeComponent();

            ImagesFolderPath = string.Empty;
            LabelsFolderPath = string.Empty;

            // Set initial directories from settings
            if (!string.IsNullOrEmpty(Properties.Settings.Default.LastImageDirectory) &&
                Directory.Exists(Properties.Settings.Default.LastImageDirectory))
            {
                ImagesFolderTextBox.Text = Properties.Settings.Default.LastImageDirectory;
                ImagesFolderPath = Properties.Settings.Default.LastImageDirectory;
                UpdateImagesStatus(true);
            }

            if (!string.IsNullOrEmpty(Properties.Settings.Default.LastLabelDirectory) &&
                Directory.Exists(Properties.Settings.Default.LastLabelDirectory))
            {
                LabelsFolderTextBox.Text = Properties.Settings.Default.LastLabelDirectory;
                LabelsFolderPath = Properties.Settings.Default.LastLabelDirectory;
                UpdateLabelsStatus(true);
            }

            UpdateConfirmButtonState();
        }

        private void BrowseImagesButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                ValidateNames = false,
                CheckFileExists = false,
                CheckPathExists = true,
                FileName = "Select Images Folder",
                Title = LanguageManager.Instance.GetString("Dialog_ImportLabelsAndImage_SelectImagesFolder") ?? "Select Images Folder"
            };

            // Set initial directory
            if (!string.IsNullOrEmpty(Properties.Settings.Default.LastImageDirectory) &&
                Directory.Exists(Properties.Settings.Default.LastImageDirectory))
            {
                dialog.InitialDirectory = Properties.Settings.Default.LastImageDirectory;
            }

            if (dialog.ShowDialog() == true)
            {
                string folderPath = Path.GetDirectoryName(dialog.FileName);
                if (!string.IsNullOrEmpty(folderPath) && Directory.Exists(folderPath))
                {
                    ImagesFolderTextBox.Text = folderPath;
                    ImagesFolderPath = folderPath;
                    UpdateImagesStatus(true);
                    UpdateConfirmButtonState();

                    // Save for next time
                    Properties.Settings.Default.LastImageDirectory = folderPath;
                    Properties.Settings.Default.Save();
                }
                else
                {
                    MessageBox.Show(
                        LanguageManager.Instance.GetString("Dialog_ImportLabelsAndImage_InvalidPath") ?? "Invalid folder path.",
                        LanguageManager.Instance.GetString("Main_Error") ?? "Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
        }

        private void BrowseLabelsButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                ValidateNames = false,
                CheckFileExists = false,
                CheckPathExists = true,
                FileName = "Select Labels Folder",
                Title = LanguageManager.Instance.GetString("Dialog_ImportLabelsAndImage_SelectLabelsFolder") ?? "Select Labels Folder"
            };

            // Set initial directory
            if (!string.IsNullOrEmpty(Properties.Settings.Default.LastLabelDirectory) &&
                Directory.Exists(Properties.Settings.Default.LastLabelDirectory))
            {
                dialog.InitialDirectory = Properties.Settings.Default.LastLabelDirectory;
            }

            if (dialog.ShowDialog() == true)
            {
                string folderPath = Path.GetDirectoryName(dialog.FileName);
                if (!string.IsNullOrEmpty(folderPath) && Directory.Exists(folderPath))
                {
                    LabelsFolderTextBox.Text = folderPath;
                    LabelsFolderPath = folderPath;
                    UpdateLabelsStatus(true);
                    UpdateConfirmButtonState();

                    // Save for next time
                    Properties.Settings.Default.LastLabelDirectory = folderPath;
                    Properties.Settings.Default.Save();
                }
                else
                {
                    MessageBox.Show(
                        LanguageManager.Instance.GetString("Dialog_ImportLabelsAndImage_InvalidPath") ?? "Invalid folder path.",
                        LanguageManager.Instance.GetString("Main_Error") ?? "Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
        }

        private void ConfirmButton_Click(object sender, RoutedEventArgs e)
        {
            // Validate paths
            if (string.IsNullOrEmpty(ImagesFolderPath) || !Directory.Exists(ImagesFolderPath))
            {
                MessageBox.Show(
                    LanguageManager.Instance.GetString("Dialog_ImportLabelsAndImage_InvalidImagesPath") ?? "Please select a valid images folder.",
                    LanguageManager.Instance.GetString("Main_Error") ?? "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrEmpty(LabelsFolderPath) || !Directory.Exists(LabelsFolderPath))
            {
                MessageBox.Show(
                    LanguageManager.Instance.GetString("Dialog_ImportLabelsAndImage_InvalidLabelsPath") ?? "Please select a valid labels folder.",
                    LanguageManager.Instance.GetString("Main_Error") ?? "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void UpdateImagesStatus(bool isSelected)
        {
            if (isSelected)
            {
                ImagesStatusIcon.Text = "&#xE73E;"; // Check mark
                ImagesStatusIcon.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF4CAF50"));
                ImagesStatusText.Text = LanguageManager.Instance.GetString("Dialog_ImportLabelsAndImage_ImagesSelected") ?? "Images folder selected";
                ImagesStatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF4CAF50"));
            }
            else
            {
                ImagesStatusIcon.Text = "&#xE73A;"; // Circle
                ImagesStatusIcon.Foreground = new SolidColorBrush(Colors.Gray);
                ImagesStatusText.Text = LanguageManager.Instance.GetString("Dialog_ImportLabelsAndImage_ImagesNotSelected") ?? "Images folder not selected";
                ImagesStatusText.Foreground = new SolidColorBrush(Colors.Gray);
            }
        }

        private void UpdateLabelsStatus(bool isSelected)
        {
            if (isSelected)
            {
                LabelsStatusIcon.Text = "&#xE73E;"; // Check mark
                LabelsStatusIcon.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF4CAF50"));
                LabelsStatusText.Text = LanguageManager.Instance.GetString("Dialog_ImportLabelsAndImage_LabelsSelected") ?? "Labels folder selected";
                LabelsStatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF4CAF50"));
            }
            else
            {
                LabelsStatusIcon.Text = "&#xE73A;"; // Circle
                LabelsStatusIcon.Foreground = new SolidColorBrush(Colors.Gray);
                LabelsStatusText.Text = LanguageManager.Instance.GetString("Dialog_ImportLabelsAndImage_LabelsNotSelected") ?? "Labels folder not selected";
                LabelsStatusText.Foreground = new SolidColorBrush(Colors.Gray);
            }
        }

        private void UpdateConfirmButtonState()
        {
            bool isValid = !string.IsNullOrEmpty(ImagesFolderPath) &&
                          Directory.Exists(ImagesFolderPath) &&
                          !string.IsNullOrEmpty(LabelsFolderPath) &&
                          Directory.Exists(LabelsFolderPath);

            ConfirmButton.IsEnabled = isValid;
        }
    }
}

