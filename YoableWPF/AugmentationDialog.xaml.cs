using System;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using YoableWPF.Managers;

namespace YoableWPF
{
    public partial class AugmentationDialog : Window
    {
        public AugmentationOptions Options { get; private set; }

        public AugmentationDialog(string defaultOutputDir, int variants, int seed, bool allowCurrentScope)
        {
            InitializeComponent();

            OutputPathBox.Text = defaultOutputDir ?? string.Empty;
            VariantsInput.Value = Math.Clamp(variants, 1, 50);
            SeedInput.Value = Math.Max(0, seed);

            if (!allowCurrentScope)
            {
                ScopeCurrent.IsEnabled = false;
                ScopeAll.IsChecked = true;
            }

            UpdateValidationState();
        }

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                ValidateNames = false,
                CheckFileExists = false,
                CheckPathExists = true,
                FileName = "Select Folder"
            };

            if (!string.IsNullOrEmpty(OutputPathBox.Text) && Directory.Exists(OutputPathBox.Text))
                dialog.InitialDirectory = OutputPathBox.Text;

            if (dialog.ShowDialog() == true)
            {
                string folderPath = Path.GetDirectoryName(dialog.FileName);
                if (!string.IsNullOrEmpty(folderPath))
                    OutputPathBox.Text = folderPath;
            }
        }

        private void Options_Changed(object sender, RoutedPropertyChangedEventArgs<object> e) => UpdateValidationState();
        private void Options_CheckChanged(object sender, RoutedEventArgs e) => UpdateValidationState();
        private void OutputPath_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => UpdateValidationState();

        private bool AnyAugEnabled =>
            (FlipCheck?.IsChecked == true) ||
            (BrightnessCheck?.IsChecked == true) ||
            (HsvCheck?.IsChecked == true) ||
            (NoiseCheck?.IsChecked == true) ||
            (BlurCheck?.IsChecked == true);

        private void UpdateValidationState()
        {
            if (OkButton == null || WarningText == null) return;

            bool hasOutput = !string.IsNullOrWhiteSpace(OutputPathBox?.Text);
            bool anyAug = AnyAugEnabled;

            OkButton.IsEnabled = hasOutput && anyAug;

            if (!anyAug)
            {
                WarningText.Text = LanguageManager.Instance.GetString("Aug_NeedAugmentation")
                    ?? "Enable at least one augmentation.";
                WarningText.Visibility = Visibility.Visible;
            }
            else
            {
                WarningText.Visibility = Visibility.Collapsed;
            }
        }

        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
            Options = new AugmentationOptions
            {
                OutputDirectory = OutputPathBox.Text.Trim(),
                Scope = ScopeCurrent.IsChecked == true ? AugmentationScope.CurrentImage : AugmentationScope.AllImages,
                VariantsPerImage = VariantsInput.Value ?? 3,
                Seed = SeedInput.Value ?? 0,
                IncludeOriginals = IncludeOriginalsCheck.IsChecked == true,
                LabeledOnly = LabeledOnlyCheck.IsChecked == true,
                EnableFlip = FlipCheck.IsChecked == true,
                EnableBrightnessContrast = BrightnessCheck.IsChecked == true,
                EnableHsv = HsvCheck.IsChecked == true,
                EnableNoise = NoiseCheck.IsChecked == true,
                EnableBlur = BlurCheck.IsChecked == true
            };

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
