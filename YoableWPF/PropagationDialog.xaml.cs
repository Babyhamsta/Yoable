using System.Windows;
using YoableWPF.Managers;

namespace YoableWPF
{
    public enum PropagationScope
    {
        CurrentImage,
        AllImages
    }

    public partial class PropagationDialog : Window
    {
        public bool RunImageSimilarity => ImageSimilarityCheckBox.IsChecked == true;
        public bool RunObjectSimilarity => ObjectSimilarityCheckBox.IsChecked == true;
        public bool RunTracking => TrackingCheckBox.IsChecked == true;
        public bool AutoAccept => AutoAcceptCheckBox.IsChecked == true;
        public PropagationScope Scope => ScopeAllRadio.IsChecked == true ? PropagationScope.AllImages : PropagationScope.CurrentImage;

        public PropagationDialog()
        {
            InitializeComponent();

            ImageSimilarityCheckBox.IsChecked = Properties.Settings.Default.EnableImageSimilarity;
            ObjectSimilarityCheckBox.IsChecked = Properties.Settings.Default.EnableObjectSimilarity;
            TrackingCheckBox.IsChecked = Properties.Settings.Default.EnableTracking;
            AutoAcceptCheckBox.IsChecked = Properties.Settings.Default.PropagationAutoAccept;
        }

        private void Run_Click(object sender, RoutedEventArgs e)
        {
            if (!RunImageSimilarity && !RunObjectSimilarity && !RunTracking)
            {
                MessageBox.Show(
                    LanguageManager.Instance.GetString("Propagation_NoModesSelected") ?? "Select at least one propagation mode.",
                    LanguageManager.Instance.GetString("Main_Error") ?? "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

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
