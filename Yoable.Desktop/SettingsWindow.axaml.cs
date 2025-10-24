using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using System;
using System.Linq;
using System.Text.RegularExpressions;
using Yoable.Managers;
using Yoable.Services;

namespace Yoable.Desktop
{
    public partial class SettingsWindow : Window
    {
        private YoloAI? _yoloAI;
        private ISettingsService _settingsService;

        // Control references
        private ToggleButton? _generalTab;
        private ToggleButton? _aiTab;
        private ToggleButton? _performanceTab;

        private StackPanel? _generalSettingsPanel;
        private StackPanel? _aiSettingsPanel;
        private StackPanel? _performanceSettingsPanel;

        private CheckBox? _darkModeCheckBox;
        private TextBox? _formHexAccent;
        private CheckBox? _updateCheckBox;
        private CheckBox? _autoSaveCheckBox;
        private ComboBox? _labelColorPicker;
        private Slider? _labelThicknessSlider;
        private CheckBox? _crosshairEnabledCheckbox;
        private ComboBox? _crosshairColorPicker;
        private Slider? _crosshairSizeSlider;

        private ComboBox? _processingDeviceComboBox;
        private Slider? _confidenceSlider;
        private TextBlock? _modelCountText;
        private Slider? _minConsensusSlider;
        private Slider? _consensusIoUSlider;
        private Slider? _ensembleIoUSlider;
        private CheckBox? _useWeightedAverageCheckBox;

        private Slider? _uiBatchSizeSlider;
        private Slider? _processingBatchSizeSlider;
        private Slider? _labelLoadBatchSizeSlider;
        private CheckBox? _enableParallelCheckBox;

        private Button? _saveButton;
        private Button? _cancelButton;

        public SettingsWindow()
        {
            InitializeComponent();
            _settingsService = new JsonSettingsService();
            GetControls();
            WireUpEventHandlers();
            LoadSettings();
        }

        public SettingsWindow(YoloAI ai) : this()
        {
            _yoloAI = ai;
            UpdateEnsembleControls();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void GetControls()
        {
            // Tab buttons
            _generalTab = this.FindControl<ToggleButton>("GeneralTab");
            _aiTab = this.FindControl<ToggleButton>("AITab");
            _performanceTab = this.FindControl<ToggleButton>("PerformanceTab");

            // Panel containers
            _generalSettingsPanel = this.FindControl<StackPanel>("GeneralSettingsPanel");
            _aiSettingsPanel = this.FindControl<StackPanel>("AISettingsPanel");
            _performanceSettingsPanel = this.FindControl<StackPanel>("PerformanceSettingsPanel");

            // General settings controls
            _darkModeCheckBox = this.FindControl<CheckBox>("DarkModeCheckBox");
            _formHexAccent = this.FindControl<TextBox>("FormHexAccent");
            _updateCheckBox = this.FindControl<CheckBox>("UpdateCheckBox");
            _autoSaveCheckBox = this.FindControl<CheckBox>("AutoSaveCheckBox");
            _labelColorPicker = this.FindControl<ComboBox>("LabelColorPicker");
            _labelThicknessSlider = this.FindControl<Slider>("LabelThicknessSlider");
            _crosshairEnabledCheckbox = this.FindControl<CheckBox>("CrosshairEnabledCheckbox");
            _crosshairColorPicker = this.FindControl<ComboBox>("CrosshairColorPicker");
            _crosshairSizeSlider = this.FindControl<Slider>("CrosshairSizeSlider");

            // AI settings controls
            _processingDeviceComboBox = this.FindControl<ComboBox>("ProcessingDeviceComboBox");
            _confidenceSlider = this.FindControl<Slider>("ConfidenceSlider");
            _modelCountText = this.FindControl<TextBlock>("ModelCountText");
            _minConsensusSlider = this.FindControl<Slider>("MinConsensusSlider");
            _consensusIoUSlider = this.FindControl<Slider>("ConsensusIoUSlider");
            _ensembleIoUSlider = this.FindControl<Slider>("EnsembleIoUSlider");
            _useWeightedAverageCheckBox = this.FindControl<CheckBox>("UseWeightedAverageCheckBox");

            // Performance settings controls
            _uiBatchSizeSlider = this.FindControl<Slider>("UIBatchSizeSlider");
            _processingBatchSizeSlider = this.FindControl<Slider>("ProcessingBatchSizeSlider");
            _labelLoadBatchSizeSlider = this.FindControl<Slider>("LabelLoadBatchSizeSlider");
            _enableParallelCheckBox = this.FindControl<CheckBox>("EnableParallelCheckBox");

            // Buttons
            _saveButton = this.FindControl<Button>("SaveButton");
            _cancelButton = this.FindControl<Button>("CancelButton");
        }

        private void WireUpEventHandlers()
        {
            // Tab switching
            if (_generalTab != null)
                _generalTab.IsCheckedChanged += Tab_Changed;
            if (_aiTab != null)
                _aiTab.IsCheckedChanged += Tab_Changed;
            if (_performanceTab != null)
                _performanceTab.IsCheckedChanged += Tab_Changed;

            // Dark mode toggle
            if (_darkModeCheckBox != null)
                _darkModeCheckBox.IsCheckedChanged += DarkMode_Click;

            // Hex input validation
            if (_formHexAccent != null)
                _formHexAccent.TextChanged += FormHexInput_TextChanged;

            // Buttons
            if (_saveButton != null)
                _saveButton.Click += SaveButton_Click;
            if (_cancelButton != null)
                _cancelButton.Click += CancelButton_Click;
        }

        private void Tab_Changed(object? sender, RoutedEventArgs e)
        {
            if (_generalSettingsPanel == null || _aiSettingsPanel == null || _performanceSettingsPanel == null)
                return;

            // Hide all panels
            _generalSettingsPanel.IsVisible = false;
            _aiSettingsPanel.IsVisible = false;
            _performanceSettingsPanel.IsVisible = false;

            // Uncheck all tabs except the one clicked
            if (sender is ToggleButton clickedTab)
            {
                if (clickedTab == _generalTab)
                {
                    _generalSettingsPanel.IsVisible = true;
                    if (_aiTab != null) _aiTab.IsChecked = false;
                    if (_performanceTab != null) _performanceTab.IsChecked = false;
                }
                else if (clickedTab == _aiTab)
                {
                    _aiSettingsPanel.IsVisible = true;
                    if (_generalTab != null) _generalTab.IsChecked = false;
                    if (_performanceTab != null) _performanceTab.IsChecked = false;
                }
                else if (clickedTab == _performanceTab)
                {
                    _performanceSettingsPanel.IsVisible = true;
                    if (_generalTab != null) _generalTab.IsChecked = false;
                    if (_aiTab != null) _aiTab.IsChecked = false;
                }
            }
        }

        private void UpdateEnsembleControls()
        {
            if (_yoloAI != null && _modelCountText != null && _minConsensusSlider != null)
            {
                int modelCount = _yoloAI.GetLoadedModelsCount();

                // Update model count text
                if (modelCount == 0)
                {
                    _modelCountText.Text = "No models loaded";
                    _modelCountText.Foreground = new SolidColorBrush(Colors.Gray);
                }
                else if (modelCount == 1)
                {
                    _modelCountText.Text = "1 model loaded (single model mode)";
                    _modelCountText.Foreground = new SolidColorBrush(Colors.Orange);
                }
                else
                {
                    _modelCountText.Text = $"{modelCount} models loaded (ensemble mode active)";
                    _modelCountText.Foreground = new SolidColorBrush(Colors.Green);
                }

                // Update max value for consensus slider
                if (modelCount > 0)
                {
                    _minConsensusSlider.Maximum = Math.Max(2, modelCount);

                    // Adjust current value if it exceeds new maximum
                    if (_minConsensusSlider.Value > _minConsensusSlider.Maximum)
                    {
                        _minConsensusSlider.Value = _minConsensusSlider.Maximum;
                    }
                }
            }
        }

        private void LoadSettings()
        {
            // Load AI Settings
            if (_processingDeviceComboBox != null)
                _processingDeviceComboBox.SelectedIndex = _settingsService.GetSetting<bool>("UseGPU") ? 1 : 0;
            if (_confidenceSlider != null)
                _confidenceSlider.Value = _settingsService.GetSetting<float>("AIConfidence") * 100;
            if (_formHexAccent != null)
                _formHexAccent.Text = _settingsService.GetSetting<string>("FormAccent");

            // Load Ensemble Settings
            if (_minConsensusSlider != null)
                _minConsensusSlider.Value = _settingsService.GetSetting<int>("MinimumConsensus");
            if (_consensusIoUSlider != null)
                _consensusIoUSlider.Value = _settingsService.GetSetting<float>("ConsensusIoUThreshold");
            if (_ensembleIoUSlider != null)
                _ensembleIoUSlider.Value = _settingsService.GetSetting<float>("EnsembleIoUThreshold");
            if (_useWeightedAverageCheckBox != null)
                _useWeightedAverageCheckBox.IsChecked = _settingsService.GetSetting<bool>("UseWeightedAverage");

            // Load General Settings
            if (_darkModeCheckBox != null)
                _darkModeCheckBox.IsChecked = _settingsService.GetSetting<bool>("DarkTheme");
            if (_updateCheckBox != null)
                _updateCheckBox.IsChecked = _settingsService.GetSetting<bool>("CheckUpdatesOnLaunch");
            if (_autoSaveCheckBox != null)
                _autoSaveCheckBox.IsChecked = _settingsService.GetSetting<bool>("EnableAutoSave");

            // Label Settings
            if (_labelColorPicker != null)
                SelectComboBoxItemByTag(_labelColorPicker, _settingsService.GetSetting<string>("LabelColor"));
            if (_labelThicknessSlider != null)
                _labelThicknessSlider.Value = _settingsService.GetSetting<float>("LabelThickness");

            // Crosshair Settings
            if (_crosshairColorPicker != null)
                SelectComboBoxItemByTag(_crosshairColorPicker, _settingsService.GetSetting<string>("CrosshairColor"));
            if (_crosshairEnabledCheckbox != null)
                _crosshairEnabledCheckbox.IsChecked = _settingsService.GetSetting<bool>("EnableCrosshair");
            if (_crosshairSizeSlider != null)
                _crosshairSizeSlider.Value = _settingsService.GetSetting<float>("CrosshairSize");

            // Performance Settings
            if (_uiBatchSizeSlider != null)
                _uiBatchSizeSlider.Value = _settingsService.GetSetting<int>("UIBatchSize");
            if (_processingBatchSizeSlider != null)
                _processingBatchSizeSlider.Value = _settingsService.GetSetting<int>("ProcessingBatchSize");
            if (_labelLoadBatchSizeSlider != null)
                _labelLoadBatchSizeSlider.Value = _settingsService.GetSetting<int>("LabelLoadBatchSize");
            if (_enableParallelCheckBox != null)
                _enableParallelCheckBox.IsChecked = _settingsService.GetSetting<bool>("EnableParallelProcessing");
        }

        private void SelectComboBoxItemByTag(ComboBox comboBox, string colorHex)
        {
            foreach (var item in comboBox.Items)
            {
                if (item is ComboBoxItem cbi && cbi.Tag?.ToString() == colorHex)
                {
                    comboBox.SelectedItem = item;
                    break;
                }
            }
        }

        private void DarkMode_Click(object? sender, RoutedEventArgs e)
        {
            // Note: Avalonia theme switching would be handled differently
            // This is a placeholder for future theme implementation
            // Application.Current.RequestedThemeVariant = _darkModeCheckBox?.IsChecked == true ? ThemeVariant.Dark : ThemeVariant.Light;
        }

        private void FormHexInput_TextChanged(object? sender, TextChangedEventArgs e)
        {
            if (_formHexAccent == null || _saveButton == null)
                return;

            var errorColor = new SolidColorBrush(Color.FromRgb(140, 8, 8));

            string hex = _formHexAccent.Text ?? "";
            try
            {
                if (hex.StartsWith("#"))
                    hex = hex.Substring(1);

                if (hex.Length == 6 && Regex.IsMatch(hex, "^[0-9A-Fa-f]{6}$"))
                {
                    _formHexAccent.Background = Brushes.Transparent;
                    _saveButton.IsEnabled = true;
                }
                else
                {
                    _formHexAccent.Background = errorColor;
                    _saveButton.IsEnabled = false;
                }
            }
            catch
            {
                _formHexAccent.Background = errorColor;
                _saveButton.IsEnabled = false;
            }
        }

        private void SaveButton_Click(object? sender, RoutedEventArgs e)
        {
            // Save AI Settings
            if (_processingDeviceComboBox != null)
                _settingsService.SetSetting("UseGPU", _processingDeviceComboBox.SelectedIndex == 1);
            if (_confidenceSlider != null)
                _settingsService.SetSetting("AIConfidence", (float)(_confidenceSlider.Value / 100));

            // Save Ensemble Settings
            if (_minConsensusSlider != null)
                _settingsService.SetSetting("MinimumConsensus", (int)_minConsensusSlider.Value);
            if (_consensusIoUSlider != null)
                _settingsService.SetSetting("ConsensusIoUThreshold", (float)_consensusIoUSlider.Value);
            if (_ensembleIoUSlider != null)
                _settingsService.SetSetting("EnsembleIoUThreshold", (float)_ensembleIoUSlider.Value);
            if (_useWeightedAverageCheckBox != null)
                _settingsService.SetSetting("UseWeightedAverage", _useWeightedAverageCheckBox.IsChecked ?? true);

            // Save General Settings
            if (_darkModeCheckBox != null)
                _settingsService.SetSetting("DarkTheme", _darkModeCheckBox.IsChecked ?? false);
            if (_updateCheckBox != null)
                _settingsService.SetSetting("CheckUpdatesOnLaunch", _updateCheckBox.IsChecked ?? false);
            if (_autoSaveCheckBox != null)
                _settingsService.SetSetting("EnableAutoSave", _autoSaveCheckBox.IsChecked ?? true);
            if (_formHexAccent != null)
                _settingsService.SetSetting("FormAccent", _formHexAccent.Text ?? "#FF0000");

            // Label Settings
            if (_labelColorPicker != null && _labelColorPicker.SelectedItem is ComboBoxItem labelItem)
                _settingsService.SetSetting("LabelColor", labelItem.Tag?.ToString() ?? "#FFFF0000");
            if (_labelThicknessSlider != null)
                _settingsService.SetSetting("LabelThickness", (float)_labelThicknessSlider.Value);

            // Crosshair settings
            if (_crosshairEnabledCheckbox != null)
                _settingsService.SetSetting("EnableCrosshair", _crosshairEnabledCheckbox.IsChecked ?? false);
            if (_crosshairColorPicker != null && _crosshairColorPicker.SelectedItem is ComboBoxItem crosshairItem)
                _settingsService.SetSetting("CrosshairColor", crosshairItem.Tag?.ToString() ?? "#FFFF0000");
            if (_crosshairSizeSlider != null)
                _settingsService.SetSetting("CrosshairSize", (float)_crosshairSizeSlider.Value);

            // Performance Settings
            if (_uiBatchSizeSlider != null)
                _settingsService.SetSetting("UIBatchSize", (int)_uiBatchSizeSlider.Value);
            if (_processingBatchSizeSlider != null)
                _settingsService.SetSetting("ProcessingBatchSize", (int)_processingBatchSizeSlider.Value);
            if (_labelLoadBatchSizeSlider != null)
                _settingsService.SetSetting("LabelLoadBatchSize", (int)_labelLoadBatchSizeSlider.Value);
            if (_enableParallelCheckBox != null)
                _settingsService.SetSetting("EnableParallelProcessing", _enableParallelCheckBox.IsChecked ?? true);

            _settingsService.Save();
            Close();
        }

        private void CancelButton_Click(object? sender, RoutedEventArgs e)
        {
            // Reset theme if not saved (placeholder for future implementation)
            Close();
        }
    }
}
