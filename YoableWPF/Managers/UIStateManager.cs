using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace YoableWPF.Managers
{
    public class UIStateManager
    {
        private readonly MainWindow mainWindow;
        private List<ImageListItem> allImages = new List<ImageListItem>();
        private Dictionary<string, ImageListItem> imageListItemCache = new Dictionary<string, ImageListItem>();
        private readonly Brush needsReviewDefaultForeground;
        private readonly Brush unverifiedDefaultForeground;
        private readonly Brush verifiedDefaultForeground;
        private readonly Brush suggestedDefaultForeground;

        private static SolidColorBrush CreateFrozenBrush(byte a, byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
            brush.Freeze();
            return brush;
        }

        private static readonly SolidColorBrush NeedsReviewBrush = CreateFrozenBrush(0xFF, 0xFF, 0xB7, 0x4D);
        private static readonly SolidColorBrush UnverifiedBrush = CreateFrozenBrush(0xFF, 0xE5, 0x73, 0x73);
        private static readonly SolidColorBrush VerifiedBrush = CreateFrozenBrush(0xFF, 0x81, 0xC7, 0x84);
        private static readonly SolidColorBrush SuggestedBrush = CreateFrozenBrush(0xFF, 0x64, 0xB5, 0xF6);

        private static readonly SolidColorBrush OrangeInactive = CreateFrozenBrush(0x44, 0xFF, 0xB7, 0x4D);
        private static readonly SolidColorBrush OrangeActive = CreateFrozenBrush(0xFF, 0xFF, 0xB7, 0x4D);
        private static readonly SolidColorBrush RedInactive = CreateFrozenBrush(0x44, 0xE5, 0x73, 0x73);
        private static readonly SolidColorBrush RedActive = CreateFrozenBrush(0xFF, 0xE5, 0x73, 0x73);
        private static readonly SolidColorBrush GreenInactive = CreateFrozenBrush(0x44, 0x81, 0xC7, 0x84);
        private static readonly SolidColorBrush GreenActive = CreateFrozenBrush(0xFF, 0x81, 0xC7, 0x84);
        private static readonly SolidColorBrush BlueInactive = CreateFrozenBrush(0x44, 0x64, 0xB5, 0xF6);
        private static readonly SolidColorBrush BlueActive = CreateFrozenBrush(0xFF, 0x64, 0xB5, 0xF6);
        private static readonly SolidColorBrush DefaultLabelBrush = CreateFrozenBrush(0xFF, 0xE5, 0x73, 0x73);

        public UIStateManager(MainWindow mainWindow)
        {
            this.mainWindow = mainWindow;
            needsReviewDefaultForeground = mainWindow.NeedsReviewCount.Foreground;
            unverifiedDefaultForeground = mainWindow.UnverifiedCount.Foreground;
            verifiedDefaultForeground = mainWindow.VerifiedCount.Foreground;
            suggestedDefaultForeground = mainWindow.SuggestedCount.Foreground;
        }

        // Cache management methods
        public Dictionary<string, ImageListItem> ImageListItemCache => imageListItemCache;

        public void AddToCache(string fileName, ImageListItem item)
        {
            imageListItemCache[fileName] = item;
        }

        public bool TryGetFromCache(string fileName, out ImageListItem item)
        {
            return imageListItemCache.TryGetValue(fileName, out item);
        }

        public void ClearCache()
        {
            imageListItemCache.Clear();
        }

        public void BuildCache(ItemCollection items)
        {
            imageListItemCache.Clear();
            foreach (var item in items)
            {
                if (item is ImageListItem imageItem)
                {
                    imageListItemCache[imageItem.FileName] = imageItem;
                }
            }
        }

        // Direct port of UpdateStatusCounts
        public void UpdateStatusCounts()
        {
            int needsReview = 0;
            int unverified = 0;
            int verified = 0;
            int suggested = 0;

            foreach (var item in mainWindow.ImageListBox.Items.Cast<ImageListItem>())
            {
                switch (item.Status)
                {
                    case ImageStatus.VerificationNeeded:
                        needsReview++;
                        break;
                    case ImageStatus.Suggested:
                        suggested++;
                        break;
                    case ImageStatus.NoLabel:
                        unverified++;
                        break;
                    case ImageStatus.Verified:
                        verified++;
                        break;
                }
            }

            // Update the text blocks with counts
            mainWindow.NeedsReviewCount.Text = needsReview.ToString();
            mainWindow.NeedsReviewCount.Foreground = needsReview > 0
                ? NeedsReviewBrush
                : needsReviewDefaultForeground;

            mainWindow.SuggestedCount.Text = suggested.ToString();
            mainWindow.SuggestedCount.Foreground = suggested > 0
                ? SuggestedBrush
                : suggestedDefaultForeground;

            mainWindow.UnverifiedCount.Text = unverified.ToString();
            mainWindow.UnverifiedCount.Foreground = unverified > 0
                ? UnverifiedBrush
                : unverifiedDefaultForeground;

            mainWindow.VerifiedCount.Text = verified.ToString();
            mainWindow.VerifiedCount.Foreground = verified > 0
                ? VerifiedBrush
                : verifiedDefaultForeground;
        }

        // Updated RefreshLabelList with class color indicators
        public void RefreshLabelList()
        {
            var projectClasses = mainWindow.ProjectClasses;
            var items = new List<LabelListItemView>();

            foreach (var label in mainWindow.drawingCanvas.Labels)
            {
                // Get class info - fallback to default class if not found
                var labelClass = projectClasses?.FirstOrDefault(c => c.ClassId == label.ClassId);
                
                // If class not found, use default class (ClassId 0)
                if (labelClass == null)
                {
                    labelClass = projectClasses?.FirstOrDefault(c => c.ClassId == 0);
                }
                
                // Final fallback if even default doesn't exist
                string className = labelClass?.Name ?? "default";
                var classBrush = labelClass?.ColorBrush ?? DefaultLabelBrush;

                items.Add(new LabelListItemView(label, className, classBrush));
            }

            var currentFile = Path.GetFileName(mainWindow.imageManager.CurrentImagePath ?? string.Empty);
            if (!string.IsNullOrEmpty(currentFile))
            {
                var suggestions = mainWindow.labelManager.GetSuggestions(currentFile);
                foreach (var suggestion in suggestions)
                {
                    var labelClass = projectClasses?.FirstOrDefault(c => c.ClassId == suggestion.ClassId);
                    if (labelClass == null)
                    {
                        labelClass = projectClasses?.FirstOrDefault(c => c.ClassId == 0);
                    }

                    string className = labelClass?.Name ?? "default";
                    var classBrush = labelClass?.ColorBrush ?? DefaultLabelBrush;
                    string sourceText = suggestion.Source.ToString();

                    items.Add(new LabelListItemView(suggestion, className, classBrush, sourceText));
                }
            }

            mainWindow.LabelListBox.ItemsSource = items;
        }

        // Helper for sorting - direct port
        public void SortImagesByName()
        {
            // Get source list - use allImages if populated (filter-aware), otherwise use current items
            var sourceItems = (allImages != null && allImages.Count > 0)
                ? allImages
                : mainWindow.ImageListBox.Items.Cast<ImageListItem>().ToList();
            var selectedItem = mainWindow.ImageListBox.SelectedItem as ImageListItem;

            // Sort by filename
            var sorted = sourceItems.OrderBy(x => x.FileName).ToList();

            // Update allImages to maintain sort order for filters
            allImages = new List<ImageListItem>(sorted);

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
            // Get source list - use allImages if populated (filter-aware), otherwise use current items
            var sourceItems = (allImages != null && allImages.Count > 0)
                ? allImages
                : mainWindow.ImageListBox.Items.Cast<ImageListItem>().ToList();
            var selectedItem = mainWindow.ImageListBox.SelectedItem as ImageListItem;

            // Custom sort order: Suggested first, then VerificationNeeded, then NoLabel, then Verified
            var sorted = sourceItems.OrderBy(x => {
                switch (x.Status)
                {
                    case ImageStatus.Suggested: return 0;
                    case ImageStatus.VerificationNeeded: return 1;
                    case ImageStatus.NoLabel: return 2;
                    case ImageStatus.Verified: return 3;
                    default: return 3;
                }
            }).ThenBy(x => x.FileName).ToList();

            // Update allImages to maintain sort order for filters
            allImages = new List<ImageListItem>(sorted);

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

        public void FilterImagesByStatus(ImageStatus? status)
        {
            // Store all images if not already stored
            if (allImages == null || allImages.Count == 0)
            {
                allImages = new List<ImageListItem>();
                foreach (ImageListItem item in mainWindow.ImageListBox.Items)
                {
                    allImages.Add(item);
                }
            }

            var selectedItem = mainWindow.ImageListBox.SelectedItem as ImageListItem;

            mainWindow.ImageListBox.SelectionChanged -= mainWindow.ImageListBox_SelectionChanged;
            mainWindow.ImageListBox.Items.Clear();

            if (status == null)
            {
                // Show all images
                foreach (var item in allImages)
                {
                    mainWindow.ImageListBox.Items.Add(item);
                }
            }
            else
            {
                // Filter by status
                foreach (var item in allImages.Where(i => i.Status == status.Value))
                {
                    mainWindow.ImageListBox.Items.Add(item);
                }
            }

            // Restore selection if possible
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
            else if (mainWindow.ImageListBox.Items.Count > 0)
            {
                mainWindow.ImageListBox.SelectedIndex = 0;
            }

            mainWindow.ImageListBox.SelectionChanged += mainWindow.ImageListBox_SelectionChanged;
            UpdateStatusCounts();
        }

        public void RefreshAllImagesList()
        {
            // Refresh the complete list of images (call this when images are added/removed)
            allImages = new List<ImageListItem>();
            foreach (ImageListItem item in mainWindow.ImageListBox.Items)
            {
                allImages.Add(item);
            }
        }

        /// <summary>
        /// Filters images by selected class IDs. Shows images that contain at least one label with a selected class.
        /// </summary>
        /// <param name="selectedClassIds">Set of class IDs to filter by. If null, shows all images.</param>
        public void FilterImagesByClasses(HashSet<int> selectedClassIds)
        {
            // Store all images if not already stored
            if (allImages == null || allImages.Count == 0)
            {
                allImages = new List<ImageListItem>();
                foreach (ImageListItem item in mainWindow.ImageListBox.Items)
                {
                    allImages.Add(item);
                }
            }

            var selectedItem = mainWindow.ImageListBox.SelectedItem as ImageListItem;

            mainWindow.ImageListBox.SelectionChanged -= mainWindow.ImageListBox_SelectionChanged;
            mainWindow.ImageListBox.Items.Clear();

            if (selectedClassIds == null || selectedClassIds.Count == 0)
            {
                // Show all images if no filter or all classes selected
                foreach (var item in allImages)
                {
                    mainWindow.ImageListBox.Items.Add(item);
                }
            }
            else
            {
                // Filter images that contain at least one label with a selected class
                foreach (var item in allImages)
                {
                    // Check if this image has labels with any of the selected classes
                    if (mainWindow.labelManager.LabelStorage.TryGetValue(item.FileName, out var labels))
                    {
                        bool hasSelectedClass = labels.Any(label => selectedClassIds.Contains(label.ClassId));
                        if (hasSelectedClass)
                        {
                            mainWindow.ImageListBox.Items.Add(item);
                        }
                    }
                    else
                    {
                        // If image has no labels, don't show it when filtering by class
                        // (unless we want to show images with no labels, but that doesn't make sense for class filtering)
                    }
                }
            }

            // Restore selection if possible
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
            else if (mainWindow.ImageListBox.Items.Count > 0)
            {
                mainWindow.ImageListBox.SelectedIndex = 0;
            }

            mainWindow.ImageListBox.SelectionChanged += mainWindow.ImageListBox_SelectionChanged;
            UpdateStatusCounts();
        }

        public void UpdateFilterButtonStyles(
            Button allButton,
            Button reviewButton,
            Button suggestedButton,
            Button noLabelButton,
            Button verifiedButton,
            Button activeButton = null)
        {
            // Define colors once
            var transparent = System.Windows.Media.Brushes.Transparent;
            var white = System.Windows.Media.Brushes.White;
            var baseHigh = (System.Windows.Media.Brush)mainWindow
                .FindResource("SystemControlForegroundBaseHighBrush");
            var baseLow = (System.Windows.Media.Brush)mainWindow
                .FindResource("SystemControlBackgroundBaseLowBrush");
            var baseLowFore = (System.Windows.Media.Brush)mainWindow
                .FindResource("SystemControlForegroundBaseLowBrush");

            var orangeFore = OrangeActive;
            var redFore = RedActive;
            var greenFore = GreenActive;
            var blueFore = BlueActive;

            // Set all button styles based on active button
            allButton.Background = activeButton == allButton ? baseLow : transparent;
            allButton.Foreground = baseHigh;
            allButton.BorderThickness = activeButton == allButton ? new Thickness(1) : new Thickness(0);
            allButton.BorderBrush = activeButton == allButton ? baseLowFore : null;

            reviewButton.Background = activeButton == reviewButton ? OrangeActive : OrangeInactive;
            reviewButton.Foreground = activeButton == reviewButton ? white : orangeFore;

            suggestedButton.Background = activeButton == suggestedButton ? BlueActive : BlueInactive;
            suggestedButton.Foreground = activeButton == suggestedButton ? white : blueFore;

            noLabelButton.Background = activeButton == noLabelButton ? RedActive : RedInactive;
            noLabelButton.Foreground = activeButton == noLabelButton ? white : redFore;

            verifiedButton.Background = activeButton == verifiedButton ? GreenActive : GreenInactive;
            verifiedButton.Foreground = activeButton == verifiedButton ? white : greenFore;
        }
    }
}
