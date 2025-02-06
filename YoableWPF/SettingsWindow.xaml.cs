using ModernWpf;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace YoableWPF
{
    /// <summary>
    /// Interaction logic for SettingsWindow.xaml
    /// </summary>
    public partial class SettingsWindow : Window
    {
        public SettingsWindow()
        {
            InitializeComponent();
            LoadSettings();
        }

        private void LoadSettings()
        {
            // Load AI Settings
            ProcessingDeviceComboBox.SelectedIndex = Properties.Settings.Default.UseGPU ? 1 : 0;
            ConfidenceSlider.Value = Properties.Settings.Default.AIConfidence * 100;
            FormHexAccent.Text = Properties.Settings.Default.FormAccent;

            // Load General Settings
            DarkModeCheckBox.IsChecked = Properties.Settings.Default.DarkTheme;
            UpdateCheckBox.IsChecked = Properties.Settings.Default.CheckUpdatesOnLaunch;

            // Label Settings
            SelectComboBoxItemByTag(LabelColorPicker, Properties.Settings.Default.LabelColor);
            LabelThicknessSlider.Value = Properties.Settings.Default.LabelThickness;

            // Crosshair Settings
            SelectComboBoxItemByTag(CrosshairColorPicker, Properties.Settings.Default.CrosshairColor);
            CrosshairEnabledCheckbox.IsChecked = Properties.Settings.Default.EnableCrosshair;
            CrosshairSizeSlider.Value = Properties.Settings.Default.CrosshairSize;

            // Uploader Settings
            CloudUploadCheckbox.IsChecked = Properties.Settings.Default.AskForUpload;
            MaxConcurrentUploadsSlider.Value = Properties.Settings.Default.MaxConcurrentUploads;
        }

        private void SelectComboBoxItemByTag(ComboBox comboBox, string colorHex)
        {
            var item = comboBox.Items.Cast<ComboBoxItem>()
                              .FirstOrDefault(i => i.Tag.ToString() == colorHex);
            if (item != null)
                comboBox.SelectedItem = item;
        }

        private void DarkMode_Click(object sender, RoutedEventArgs e)
        {
            ThemeManager.Current.ApplicationTheme = (bool)DarkModeCheckBox.IsChecked ? ApplicationTheme.Dark : ApplicationTheme.Light;
        }

        private void FormHexInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            Brush errorColor = new SolidColorBrush(Color.FromRgb(140, 8, 8));
            if (ThemeManager.Current.ApplicationTheme == ApplicationTheme.Light)
            {
                errorColor = new SolidColorBrush(Color.FromRgb(255, 158, 158));
            }
            else 
            {
                errorColor = new SolidColorBrush(Color.FromRgb(140, 8, 8));
            }


            string hex = FormHexAccent.Text;
            try
            {
                if (hex.StartsWith("#"))
                    hex = hex.Substring(1);

                if (hex.Length == 6 && System.Text.RegularExpressions.Regex.IsMatch(hex, "^[0-9A-Fa-f]{6}$"))
                {
                    FormHexAccent.ClearValue(TextBox.BackgroundProperty);
                    SaveButton.IsEnabled = true;
                }
                else
                {
                    FormHexAccent.Background = errorColor;
                    SaveButton.IsEnabled = false;
                }
            }
            catch
            {
                FormHexAccent.Background = errorColor;
                SaveButton.IsEnabled = false;
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            // Save AI Settings
            Properties.Settings.Default.UseGPU = ProcessingDeviceComboBox.SelectedIndex == 1;
            Properties.Settings.Default.AIConfidence = (float)(ConfidenceSlider.Value / 100);

            // Save General Settings
            Properties.Settings.Default.DarkTheme = DarkModeCheckBox.IsChecked ?? false;
            Properties.Settings.Default.CheckUpdatesOnLaunch = UpdateCheckBox.IsChecked ?? false;
            Properties.Settings.Default.FormAccent = FormHexAccent.Text;
            ThemeManager.Current.AccentColor = (Color)ColorConverter.ConvertFromString(FormHexAccent.Text);

            // Label Settings
            Properties.Settings.Default.LabelColor = ((ComboBoxItem)LabelColorPicker.SelectedItem).Tag.ToString();
            Properties.Settings.Default.LabelThickness = LabelThicknessSlider.Value;

            // Crosshair settings
            Properties.Settings.Default.EnableCrosshair = CrosshairEnabledCheckbox.IsChecked ?? false;
            Properties.Settings.Default.CrosshairColor = ((ComboBoxItem)CrosshairColorPicker.SelectedItem).Tag.ToString();
            Properties.Settings.Default.CrosshairSize = CrosshairSizeSlider.Value;

            // Uploader Settings
            Properties.Settings.Default.AskForUpload = CloudUploadCheckbox.IsChecked ?? false;
            Properties.Settings.Default.MaxConcurrentUploads = MaxConcurrentUploadsSlider.Value;

            Properties.Settings.Default.Save();
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            ThemeManager.Current.ApplicationTheme = Properties.Settings.Default.DarkTheme ? ApplicationTheme.Dark : ApplicationTheme.Light; // Reset if not saved
            Close();
        }
    }
}
