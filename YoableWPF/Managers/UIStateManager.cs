using System;
using System.Collections.Generic;
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

        public UIStateManager(MainWindow mainWindow)
        {
            this.mainWindow = mainWindow;
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
            var needsReview = mainWindow.ImageListBox.Items.Cast<ImageListItem>()
                .Count(x => x.Status == ImageStatus.VerificationNeeded);
            var unverified = mainWindow.ImageListBox.Items.Cast<ImageListItem>()
                .Count(x => x.Status == ImageStatus.NoLabel);
            var verified = mainWindow.ImageListBox.Items.Cast<ImageListItem>()
                .Count(x => x.Status == ImageStatus.Verified);

            // Update the text blocks with counts
            mainWindow.NeedsReviewCount.Text = needsReview.ToString();
            mainWindow.NeedsReviewCount.Foreground = needsReview > 0
                ? (SolidColorBrush)(new BrushConverter().ConvertFrom("#FFB74D"))
                : mainWindow.NeedsReviewCount.Foreground;

            mainWindow.UnverifiedCount.Text = unverified.ToString();
            mainWindow.UnverifiedCount.Foreground = unverified > 0
                ? (SolidColorBrush)(new BrushConverter().ConvertFrom("#E57373"))
                : mainWindow.UnverifiedCount.Foreground;

            mainWindow.VerifiedCount.Text = verified.ToString();
            mainWindow.VerifiedCount.Foreground = verified > 0
                ? (SolidColorBrush)(new BrushConverter().ConvertFrom("#81C784"))
                : mainWindow.VerifiedCount.Foreground;
        }

        // Cache for project classes to avoid repeated reflection
        private List<LabelClass> cachedProjectClasses = null;
        
        // Updated RefreshLabelList with class color indicators
        public void RefreshLabelList()
        {
            // Clear the label list
            mainWindow.LabelListBox.Items.Clear();
            
            // Get project classes - use cached value or fetch via reflection
            if (cachedProjectClasses == null)
            {
                var mainWindowType = mainWindow.GetType();
                var projectClassesField = mainWindowType.GetField("projectClasses", 
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                cachedProjectClasses = projectClassesField?.GetValue(mainWindow) as List<LabelClass>;
            }
            
            var projectClasses = cachedProjectClasses;
            
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
                string colorHex = labelClass?.ColorHex ?? "#E57373";
                
                // Create styled item with class color bar
                var stackPanel = new System.Windows.Controls.StackPanel 
                { 
                    Orientation = System.Windows.Controls.Orientation.Horizontal 
                };
                
                // Color indicator bar
                stackPanel.Children.Add(new System.Windows.Controls.Border
                {
                    Width = 4,
                    Height = 20,
                    Background = new SolidColorBrush(
                        (Color)ColorConverter.ConvertFromString(colorHex)),
                    Margin = new Thickness(0, 0, 8, 0),
                    CornerRadius = new System.Windows.CornerRadius(2)
                });
                
                // Label text
                stackPanel.Children.Add(new System.Windows.Controls.TextBlock
                {
                    Text = $"[{className}] {label.Name}",
                    VerticalAlignment = VerticalAlignment.Center
                });
                
                mainWindow.LabelListBox.Items.Add(stackPanel);
            }
        }
        
        // Call this method when project classes change to refresh the cache
        public void RefreshProjectClassesCache()
        {
            cachedProjectClasses = null;
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

        public void UpdateFilterButtonStyles(
            Button allButton,
            Button reviewButton,
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

            var orangeInactive = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromArgb(0x44, 0xFF, 0xB7, 0x4D));
            var orangeActive = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromArgb(0xFF, 0xFF, 0xB7, 0x4D));
            var orangeFore = orangeActive;

            var redInactive = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromArgb(0x44, 0xE5, 0x73, 0x73));
            var redActive = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromArgb(0xFF, 0xE5, 0x73, 0x73));
            var redFore = redActive;

            var greenInactive = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromArgb(0x44, 0x81, 0xC7, 0x84));
            var greenActive = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromArgb(0xFF, 0x81, 0xC7, 0x84));
            var greenFore = greenActive;

            // Set all button styles based on active button
            allButton.Background = activeButton == allButton ? baseLow : transparent;
            allButton.Foreground = baseHigh;
            allButton.BorderThickness = activeButton == allButton ? new Thickness(1) : new Thickness(0);
            allButton.BorderBrush = activeButton == allButton ? baseLowFore : null;

            reviewButton.Background = activeButton == reviewButton ? orangeActive : orangeInactive;
            reviewButton.Foreground = activeButton == reviewButton ? white : orangeFore;

            noLabelButton.Background = activeButton == noLabelButton ? redActive : redInactive;
            noLabelButton.Foreground = activeButton == noLabelButton ? white : redFore;

            verifiedButton.Background = activeButton == verifiedButton ? greenActive : greenInactive;
            verifiedButton.Foreground = activeButton == verifiedButton ? white : greenFore;
        }
    }
}