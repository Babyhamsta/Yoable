using System;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Media;

namespace YoableWPF.Managers
{
    public class UIStateManager
    {
        private readonly MainWindow mainWindow;

        public UIStateManager(MainWindow mainWindow)
        {
            this.mainWindow = mainWindow;
        }

        // Direct port of UpdateStatusCounts
        public void UpdateStatusCounts()
        {
            var needsReview = mainWindow.ImageListBox.Items.Cast<ImageListItem>()
                .Count(x => x.Status == ImageStatus.VerificationNeeded);
            var unverified = mainWindow.ImageListBox.Items.Cast<ImageListItem>()
                .Count(x => x.Status == ImageStatus.NoLabel);

            // Update the text blocks with counts
            mainWindow.NeedsReviewCount.Text = needsReview > 0
                ? $"{needsReview} need{(needsReview == 1 ? "s" : "")} review"
                : "0 need review";
            mainWindow.NeedsReviewCount.Foreground = needsReview > 0
                ? (SolidColorBrush)(new BrushConverter().ConvertFrom("#CC7A00"))
                : mainWindow.NeedsReviewCount.Foreground;

            mainWindow.UnverifiedCount.Text = unverified > 0
                ? $"{unverified} unverified"
                : "0 unverified";
            mainWindow.UnverifiedCount.Foreground = unverified > 0
                ? (SolidColorBrush)(new BrushConverter().ConvertFrom("#CC3300"))
                : mainWindow.UnverifiedCount.Foreground;
        }

        // Direct port of RefreshLabelList
        public void RefreshLabelList()
        {
            mainWindow.LabelListBox.Items.Clear();
            foreach (var label in mainWindow.drawingCanvas.Labels)
            {
                mainWindow.LabelListBox.Items.Add(label.Name);
            }
        }

        // Helper for sorting - direct port
        public void SortImagesByName()
        {
            var items = mainWindow.ImageListBox.Items.Cast<ImageListItem>().ToList();
            var selectedItem = mainWindow.ImageListBox.SelectedItem as ImageListItem;

            // Sort by filename
            var sorted = items.OrderBy(x => x.FileName).ToList();

            // Update ListBox while preserving selection
            mainWindow.ImageListBox.SelectionChanged -= mainWindow.ImageListBox_SelectionChanged;
            mainWindow.ImageListBox.Items.Clear();
            foreach (var item in sorted)
            {
                mainWindow.ImageListBox.Items.Add(item);
            }

            // Restore selection
            if (selectedItem != null)
            {
                for (int i = 0; i < mainWindow.ImageListBox.Items.Count; i++)
                {
                    if (mainWindow.ImageListBox.Items[i] is ImageListItem item &&
                        item.FileName == selectedItem.FileName)
                    {
                        mainWindow.ImageListBox.SelectedIndex = i;
                        mainWindow.ImageListBox.ScrollIntoView(mainWindow.ImageListBox.SelectedItem);
                        break;
                    }
                }
            }
            mainWindow.ImageListBox.SelectionChanged += mainWindow.ImageListBox_SelectionChanged;
        }

        public void SortImagesByStatus()
        {
            var items = mainWindow.ImageListBox.Items.Cast<ImageListItem>().ToList();
            var selectedItem = mainWindow.ImageListBox.SelectedItem as ImageListItem;

            // Custom sort order: VerificationNeeded first, then NoLabel, then Verified
            var sorted = items.OrderBy(x => {
                switch (x.Status)
                {
                    case ImageStatus.VerificationNeeded: return 0;
                    case ImageStatus.NoLabel: return 1;
                    case ImageStatus.Verified: return 2;
                    default: return 3;
                }
            }).ThenBy(x => x.FileName).ToList();

            // Update ListBox while preserving selection
            mainWindow.ImageListBox.SelectionChanged -= mainWindow.ImageListBox_SelectionChanged;
            mainWindow.ImageListBox.Items.Clear();
            foreach (var item in sorted)
            {
                mainWindow.ImageListBox.Items.Add(item);
            }

            // Restore selection
            if (selectedItem != null)
            {
                for (int i = 0; i < mainWindow.ImageListBox.Items.Count; i++)
                {
                    if (mainWindow.ImageListBox.Items[i] is ImageListItem item &&
                        item.FileName == selectedItem.FileName)
                    {
                        mainWindow.ImageListBox.SelectedIndex = i;
                        mainWindow.ImageListBox.ScrollIntoView(mainWindow.ImageListBox.SelectedItem);
                        break;
                    }
                }
            }
            mainWindow.ImageListBox.SelectionChanged += mainWindow.ImageListBox_SelectionChanged;
        }
    }
}