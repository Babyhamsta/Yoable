using ModernWpf;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using YoableWPF.Managers;

namespace YoableWPF
{
    public partial class SettingsWindow : Window
    {
        private YoloAI yoloAI;

        public SettingsWindow()
        {
            InitializeComponent();
            LoadSettings();
        }

        public SettingsWindow(YoloAI ai) : this()
        {
            yoloAI = ai;
            UpdateEnsembleControls();
        }

        private void Tab_Changed(object sender, RoutedEventArgs e)
        {
            // Don't process if controls aren't loaded yet
            if (GeneralSettingsPanel == null || AISettingsPanel == null || PerformanceSettingsPanel == null)
                return;

            if (sender is RadioButton radioButton)
            {
                // Hide all panels
                GeneralSettingsPanel.Visibility = Visibility.Collapsed;
                AISettingsPanel.Visibility = Visibility.Collapsed;
                PerformanceSettingsPanel.Visibility = Visibility.Collapsed;

                // Show selected panel
                if (radioButton == GeneralTab)
                {
                    GeneralSettingsPanel.Visibility = Visibility.Visible;
                }
                else if (radioButton == AITab)
                {
                    AISettingsPanel.Visibility = Visibility.Visible;
                }
                else if (radioButton == PerformanceTab)
                {
                    PerformanceSettingsPanel.Visibility = Visibility.Visible;
                }
            }
        }

        private void UpdateEnsembleControls()
        {
            if (yoloAI != null)
            {
                int modelCount = yoloAI.GetLoadedModelsCount();

                // Update model count text
                if (modelCount == 0)
                {
                    ModelCountText.Text = "No models loaded";
                    ModelCountText.Foreground = new SolidColorBrush(Colors.Gray);
                }
                else if (modelCount == 1)
                {
                    ModelCountText.Text = "1 model loaded (single model mode)";
                    ModelCountText.Foreground = new SolidColorBrush(Colors.Orange);
                }
                else
                {
                    ModelCountText.Text = $"{modelCount} models loaded (ensemble mode active)";
                    ModelCountText.Foreground = new SolidColorBrush(Colors.Green);
                }

                // Update max value for consensus slider
                if (modelCount > 0)
                {
                    MinConsensusSlider.Maximum = Math.Max(2, modelCount);

                    // Adjust current value if it exceeds new maximum
                    if (MinConsensusSlider.Value > MinConsensusSlider.Maximum)
                    {
                        MinConsensusSlider.Value = MinConsensusSlider.Maximum;
                    }
                }
            }
        }

        private void LoadSettings()
        {
            // Load AI Settings
            ProcessingDeviceComboBox.SelectedIndex = Properties.Settings.Default.UseGPU ? 1 : 0;
            ConfidenceSlider.Value = Properties.Settings.Default.AIConfidence * 100;
            FormHexAccent.Text = Properties.Settings.Default.FormAccent;

            // Load Ensemble Settings
            MinConsensusSlider.Value = Properties.Settings.Default.MinimumConsensus;
            ConsensusIoUSlider.Value = Properties.Settings.Default.ConsensusIoUThreshold;
            EnsembleIoUSlider.Value = Properties.Settings.Default.EnsembleIoUThreshold;
            UseWeightedAverageCheckBox.IsChecked = Properties.Settings.Default.UseWeightedAverage;

            // Load General Settings
            DarkModeCheckBox.IsChecked = Properties.Settings.Default.DarkTheme;
            UpdateCheckBox.IsChecked = Properties.Settings.Default.CheckUpdatesOnLaunch;
            AutoSaveCheckBox.IsChecked = Properties.Settings.Default.EnableAutoSave;

            // Label Settings
            SelectComboBoxItemByTag(LabelColorPicker, Properties.Settings.Default.LabelColor);
            LabelThicknessSlider.Value = Properties.Settings.Default.LabelThickness;

            // Crosshair Settings
            SelectComboBoxItemByTag(CrosshairColorPicker, Properties.Settings.Default.CrosshairColor);
            CrosshairEnabledCheckbox.IsChecked = Properties.Settings.Default.EnableCrosshair;
            CrosshairSizeSlider.Value = Properties.Settings.Default.CrosshairSize;

            // Performance Settings
            UIBatchSizeSlider.Value = Properties.Settings.Default.UIBatchSize;
            ProcessingBatchSizeSlider.Value = Properties.Settings.Default.ProcessingBatchSize;
            LabelLoadBatchSizeSlider.Value = Properties.Settings.Default.LabelLoadBatchSize;
            EnableParallelCheckBox.IsChecked = Properties.Settings.Default.EnableParallelProcessing;
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

            // Save Ensemble Settings
            Properties.Settings.Default.MinimumConsensus = (int)MinConsensusSlider.Value;
            Properties.Settings.Default.ConsensusIoUThreshold = (float)ConsensusIoUSlider.Value;
            Properties.Settings.Default.EnsembleIoUThreshold = (float)EnsembleIoUSlider.Value;
            Properties.Settings.Default.UseWeightedAverage = UseWeightedAverageCheckBox.IsChecked ?? true;

            // Save General Settings
            Properties.Settings.Default.DarkTheme = DarkModeCheckBox.IsChecked ?? false;
            Properties.Settings.Default.CheckUpdatesOnLaunch = UpdateCheckBox.IsChecked ?? false;
            Properties.Settings.Default.EnableAutoSave = AutoSaveCheckBox.IsChecked ?? true;
            Properties.Settings.Default.FormAccent = FormHexAccent.Text;
            ThemeManager.Current.AccentColor = (Color)ColorConverter.ConvertFromString(FormHexAccent.Text);

            // Label Settings
            Properties.Settings.Default.LabelColor = ((ComboBoxItem)LabelColorPicker.SelectedItem).Tag.ToString();
            Properties.Settings.Default.LabelThickness = LabelThicknessSlider.Value;

            // Crosshair settings
            Properties.Settings.Default.EnableCrosshair = CrosshairEnabledCheckbox.IsChecked ?? false;
            Properties.Settings.Default.CrosshairColor = ((ComboBoxItem)CrosshairColorPicker.SelectedItem).Tag.ToString();
            Properties.Settings.Default.CrosshairSize = CrosshairSizeSlider.Value;

            // Performance Settings
            Properties.Settings.Default.UIBatchSize = (int)UIBatchSizeSlider.Value;
            Properties.Settings.Default.ProcessingBatchSize = (int)ProcessingBatchSizeSlider.Value;
            Properties.Settings.Default.LabelLoadBatchSize = (int)LabelLoadBatchSizeSlider.Value;
            Properties.Settings.Default.EnableParallelProcessing = EnableParallelCheckBox.IsChecked ?? true;

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