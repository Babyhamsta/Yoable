using ModernWpf;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using YoableWPF.Managers;
using System.Collections.Generic;

namespace YoableWPF
{
    public partial class SettingsWindow : Window
    {
        private YoloAI yoloAI;

        public SettingsWindow()
        {
            InitializeComponent();
            InitializeLanguageComboBox();
            LoadSettings();
            
            // Subscribe to language changes to update UI immediately
            LanguageManager.Instance.LanguageChanged += LanguageManager_LanguageChanged;
        }

        private void LanguageManager_LanguageChanged(object sender, EventArgs e)
        {
            // Reload window resources when language changes
            Dispatcher.Invoke(() =>
            {
                ReloadWindowResources();
            });
        }

        private void ReloadWindowResources()
        {
            try
            {
                // LanguageManager 已經將資源加載到 Application.Current.Resources 中
                // 需要強制所有 DynamicResource 綁定重新評估
                
                // 移除窗口本地的語言資源字典（如果存在）
                var languageDictToRemove = this.Resources.MergedDictionaries
                    .FirstOrDefault(d => d.Source != null && d.Source.ToString().Contains("Languages/Strings."));
                
                if (languageDictToRemove != null)
                {
                    this.Resources.MergedDictionaries.Remove(languageDictToRemove);
                }

                // 強制所有 DynamicResource 綁定重新查找資源
                // 通過臨時移除並重新添加資源字典來觸發重新評估
                var tempDict = new ResourceDictionary();
                this.Resources.MergedDictionaries.Add(tempDict);
                this.Resources.MergedDictionaries.Remove(tempDict);

                // 強制刷新所有使用 DynamicResource 的控件
                this.InvalidateVisual();
                this.UpdateLayout();
                
                // 手動更新窗口標題（因為 Title 綁定可能不會自動更新）
                this.Title = LanguageManager.Instance.GetString("Settings_Title");
                
                // 強制更新所有文本控件
                UpdateAllTextControls();
                
                // Update ensemble controls text if available
                if (yoloAI != null)
                {
                    UpdateEnsembleControls();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to reload language resources in SettingsWindow: {ex.Message}");
            }
        }

        private void UpdateAllTextControls()
        {
            // 遞歸更新所有使用 DynamicResource 的控件
            UpdateTextControlsRecursive(this);
        }

        private void UpdateTextControlsRecursive(DependencyObject obj)
        {
            if (obj == null) return;

            try
            {
                // 強制所有可能的屬性重新評估 DynamicResource
                if (obj is FrameworkElement fe)
                {
                    // 檢查常見的 DynamicResource 屬性
                    var properties = new List<DependencyProperty>
                    { 
                        FrameworkElement.TagProperty,
                        TextBlock.TextProperty
                    };

                    // 根據控件類型添加相應的屬性
                    if (fe is ContentControl)
                    {
                        properties.Add(ContentControl.ContentProperty);
                    }
                    if (fe is HeaderedItemsControl)
                    {
                        properties.Add(HeaderedItemsControl.HeaderProperty);
                    }

                    foreach (var prop in properties)
                    {
                        if (fe.ReadLocalValue(prop) == DependencyProperty.UnsetValue)
                        {
                            // 可能是使用 DynamicResource，強制重新評估
                            fe.InvalidateProperty(prop);
                        }
                    }
                }
            }
            catch { }

            // 遞歸處理子元素
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(obj); i++)
            {
                var child = VisualTreeHelper.GetChild(obj, i);
                UpdateTextControlsRecursive(child);
            }
        }

        public SettingsWindow(YoloAI ai) : this()
        {
            yoloAI = ai;
            UpdateEnsembleControls();
        }

        private void Tab_Changed(object sender, RoutedEventArgs e)
        {
            // Don't process if controls aren't loaded yet
            if (GeneralSettingsPanel == null || AISettingsPanel == null || PerformanceSettingsPanel == null || UISettingsPanel == null || ShortcutsSettingsPanel == null)
                return;

            if (sender is RadioButton radioButton)
            {
                // Hide all panels
                GeneralSettingsPanel.Visibility = Visibility.Collapsed;
                AISettingsPanel.Visibility = Visibility.Collapsed;
                PerformanceSettingsPanel.Visibility = Visibility.Collapsed;
                UISettingsPanel.Visibility = Visibility.Collapsed;
                ShortcutsSettingsPanel.Visibility = Visibility.Collapsed;

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
                else if (radioButton == UITab)
                {
                    UISettingsPanel.Visibility = Visibility.Visible;
                }
                else if (radioButton == ShortcutsTab)
                {
                    ShortcutsSettingsPanel.Visibility = Visibility.Visible;
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
                    ModelCountText.Text = LanguageManager.Instance.GetString("Settings_NoModelsLoaded");
                    ModelCountText.Foreground = new SolidColorBrush(Colors.Gray);
                }
                else if (modelCount == 1)
                {
                    ModelCountText.Text = LanguageManager.Instance.GetString("Settings_ModelLoaded_Single");
                    ModelCountText.Foreground = new SolidColorBrush(Colors.Orange);
                }
                else
                {
                    ModelCountText.Text = string.Format(LanguageManager.Instance.GetString("Settings_ModelLoaded_Ensemble"), modelCount);
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
            DetectionModeComboBox.SelectedIndex = Properties.Settings.Default.EnsembleDetectionMode;
            MinConsensusSlider.Value = Properties.Settings.Default.MinimumConsensus;
            ConsensusIoUSlider.Value = Properties.Settings.Default.ConsensusIoUThreshold;
            EnsembleIoUSlider.Value = Properties.Settings.Default.EnsembleIoUThreshold;
            UseWeightedAverageCheckBox.IsChecked = Properties.Settings.Default.UseWeightedAverage;
            
            // 根據模式顯示/隱藏相關設定
            UpdateModeDependentSettings();

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

            // UI Settings
            string currentLanguage = Properties.Settings.Default.Language ?? "zh-TW";
            var selectedLanguage = LanguageComboBox.Items.Cast<LanguageInfo>()
                .FirstOrDefault(l => l.Code == currentLanguage);
            if (selectedLanguage != null)
            {
                LanguageComboBox.SelectedItem = selectedLanguage;
            }

            // Load Hotkey Settings
            if (SaveProjectHotkeyControl != null)
            {
                SaveProjectHotkeyControl.Hotkey = Properties.Settings.Default.Hotkey_SaveProject ?? "Ctrl + S";
            }
            if (PreviousImageHotkeyControl != null)
            {
                PreviousImageHotkeyControl.Hotkey = Properties.Settings.Default.Hotkey_PreviousImage ?? "A";
            }
            if (NextImageHotkeyControl != null)
            {
                NextImageHotkeyControl.Hotkey = Properties.Settings.Default.Hotkey_NextImage ?? "D";
            }
            if (MoveLabelUpHotkeyControl != null)
            {
                MoveLabelUpHotkeyControl.Hotkey = Properties.Settings.Default.Hotkey_MoveLabelUp ?? "Up";
            }
            if (MoveLabelDownHotkeyControl != null)
            {
                MoveLabelDownHotkeyControl.Hotkey = Properties.Settings.Default.Hotkey_MoveLabelDown ?? "Down";
            }
            if (MoveLabelLeftHotkeyControl != null)
            {
                MoveLabelLeftHotkeyControl.Hotkey = Properties.Settings.Default.Hotkey_MoveLabelLeft ?? "Left";
            }
            if (MoveLabelRightHotkeyControl != null)
            {
                MoveLabelRightHotkeyControl.Hotkey = Properties.Settings.Default.Hotkey_MoveLabelRight ?? "Right";
            }
        }

        private void InitializeLanguageComboBox()
        {
            var languages = LanguageManager.Instance.GetAvailableLanguages();
            LanguageComboBox.ItemsSource = languages;
        }

        private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LanguageComboBox.SelectedItem is LanguageInfo selectedLanguage)
            {
                // 語言切換將在保存時應用
            }
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
            Properties.Settings.Default.EnsembleDetectionMode = DetectionModeComboBox.SelectedIndex;
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

            // UI Settings
            if (LanguageComboBox.SelectedItem is LanguageInfo selectedLanguage)
            {
                Properties.Settings.Default.Language = selectedLanguage.Code;
                LanguageManager.Instance.SetLanguage(selectedLanguage.Code);
            }

            // Save Hotkey Settings
            if (SaveProjectHotkeyControl != null)
            {
                Properties.Settings.Default.Hotkey_SaveProject = SaveProjectHotkeyControl.Hotkey;
            }
            if (PreviousImageHotkeyControl != null)
            {
                Properties.Settings.Default.Hotkey_PreviousImage = PreviousImageHotkeyControl.Hotkey;
            }
            if (NextImageHotkeyControl != null)
            {
                Properties.Settings.Default.Hotkey_NextImage = NextImageHotkeyControl.Hotkey;
            }
            if (MoveLabelUpHotkeyControl != null)
            {
                Properties.Settings.Default.Hotkey_MoveLabelUp = MoveLabelUpHotkeyControl.Hotkey;
            }
            if (MoveLabelDownHotkeyControl != null)
            {
                Properties.Settings.Default.Hotkey_MoveLabelDown = MoveLabelDownHotkeyControl.Hotkey;
            }
            if (MoveLabelLeftHotkeyControl != null)
            {
                Properties.Settings.Default.Hotkey_MoveLabelLeft = MoveLabelLeftHotkeyControl.Hotkey;
            }
            if (MoveLabelRightHotkeyControl != null)
            {
                Properties.Settings.Default.Hotkey_MoveLabelRight = MoveLabelRightHotkeyControl.Hotkey;
            }

            Properties.Settings.Default.Save();
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            ThemeManager.Current.ApplicationTheme = Properties.Settings.Default.DarkTheme ? ApplicationTheme.Dark : ApplicationTheme.Light; // Reset if not saved
            Close();
        }

        private void DetectionModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateModeDependentSettings();
        }

        private void UpdateModeDependentSettings()
        {
            // 根據模式顯示/隱藏相關設定
            bool isVotingMode = DetectionModeComboBox.SelectedIndex == 0;
            MinConsensusSlider.IsEnabled = isVotingMode;
            ConsensusIoUSlider.IsEnabled = isVotingMode;
        }

        protected override void OnClosed(EventArgs e)
        {
            // Unsubscribe from language changes
            if (LanguageManager.Instance != null)
            {
                LanguageManager.Instance.LanguageChanged -= LanguageManager_LanguageChanged;
            }
            base.OnClosed(e);
        }
    }
}