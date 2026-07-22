using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using YoableWPF.Managers;

namespace YoableWPF
{
    public partial class TrainingExportDialog : Window
    {
        public TrainingExportOptions Options { get; private set; }

        public TrainingExportDialog(string defaultOutputDir, int trainPercent, int valPercent, int testPercent, int seed)
        {
            InitializeComponent();

            OutputPathBox.Text = defaultOutputDir ?? string.Empty;
            TrainInput.Value = Math.Clamp(trainPercent, 0, 100);
            ValInput.Value = Math.Clamp(valPercent, 0, 100);
            TestInput.Value = Math.Clamp(testPercent, 0, 100);
            SeedInput.Value = Math.Max(0, seed);

            UpdateValidationState();
        }

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            // Folder picker using the OpenFileDialog "Select Folder" trick already used elsewhere.
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

        private void Ratio_ValueChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            UpdateValidationState();
        }

        private void OutputPath_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            UpdateValidationState();
        }

        private void UpdateValidationState()
        {
            // Controls may still be null during InitializeComponent when a ValueChanged fires early.
            if (SumText == null || OkButton == null) return;

            int train = TrainInput?.Value ?? 0;
            int val = ValInput?.Value ?? 0;
            int test = TestInput?.Value ?? 0;
            int sum = train + val + test;

            string format = LanguageManager.Instance.GetString("TrainingExport_SumFormat") ?? "Total: {0}% (must equal 100%)";
            SumText.Text = string.Format(format, sum);

            bool valid = sum == 100 && train > 0 && !string.IsNullOrWhiteSpace(OutputPathBox?.Text);

            SumText.Foreground = sum == 100
                ? (TryFindResource("SystemControlForegroundBaseMediumBrush") as Brush) ?? Brushes.Gray
                : Brushes.IndianRed;
            OkButton.IsEnabled = valid;
        }

        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
            Options = new TrainingExportOptions
            {
                OutputDirectory = OutputPathBox.Text.Trim(),
                TrainRatio = (TrainInput.Value ?? 0) / 100.0,
                ValRatio = (ValInput.Value ?? 0) / 100.0,
                TestRatio = (TestInput.Value ?? 0) / 100.0,
                Seed = SeedInput.Value ?? 0,
                IncludeUnlabeledAsBackground = BackgroundCheck.IsChecked == true,
                VerifiedOnly = VerifiedCheck.IsChecked == true
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
