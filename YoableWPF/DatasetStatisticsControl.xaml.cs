using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using YoableWPF.Managers;

namespace YoableWPF
{
    public partial class DatasetStatisticsControl : UserControl
    {
        private Func<DatasetStatistics> computeStats;

        public DatasetStatisticsControl()
        {
            InitializeComponent();
        }

        public void Initialize(Func<DatasetStatistics> computeCallback)
        {
            computeStats = computeCallback;
        }

        public void Refresh()
        {
            if (computeStats == null)
                return;

            DatasetStatistics stats = computeStats();

            bool hasImages = stats.TotalImages > 0;
            EmptyText.Visibility = hasImages ? Visibility.Collapsed : Visibility.Visible;
            ContentPanel.Visibility = hasImages ? Visibility.Visible : Visibility.Collapsed;
            if (!hasImages)
                return;

            StatTotalImages.Text = stats.TotalImages.ToString(CultureInfo.CurrentCulture);
            StatLabeled.Text = stats.LabeledImages.ToString(CultureInfo.CurrentCulture);
            StatUnlabeled.Text = stats.UnlabeledImages.ToString(CultureInfo.CurrentCulture);
            StatTotalBoxes.Text = stats.TotalBoxes.ToString(CultureInfo.CurrentCulture);
            StatAvgBoxes.Text = stats.AvgBoxesPerLabeledImage.ToString("F1", CultureInfo.CurrentCulture);

            ClassStatsItems.ItemsSource = stats.ClassStats;

            StatSmall.Text = stats.SmallBoxes.ToString(CultureInfo.CurrentCulture);
            StatMedium.Text = stats.MediumBoxes.ToString(CultureInfo.CurrentCulture);
            StatLarge.Text = stats.LargeBoxes.ToString(CultureInfo.CurrentCulture);

            StatVerified.Text = stats.VerifiedCount.ToString(CultureInfo.CurrentCulture);
            StatNeedsReview.Text = stats.NeedsReviewCount.ToString(CultureInfo.CurrentCulture);
            StatSuggested.Text = stats.SuggestedCount.ToString(CultureInfo.CurrentCulture);
            StatNoLabel.Text = stats.NoLabelCount.ToString(CultureInfo.CurrentCulture);
        }

        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            Refresh();
        }
    }
}
