using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace YoableWPF.Managers
{
    public class OverlayManager
    {
        private Grid overlayGrid;
        private TextBlock overlayLabel;
        private ProgressBar overlayProgressBar;
        private Button cancelButton;
        private Window mainWindow;
        private CancellationTokenSource cancellationTokenSource;

        public OverlayManager(Window window)
        {
            mainWindow = window;
            InitializeOverlayUI();
        }

        private void InitializeOverlayUI()
        {
            overlayGrid = new Grid
            {
                Background = new SolidColorBrush(Color.FromArgb(200, 20, 20, 20)),
                Visibility = Visibility.Collapsed
            };

            // Set the overlay to span all columns and rows
            Grid.SetColumnSpan(overlayGrid, 3); // Span all three columns
            Grid.SetRowSpan(overlayGrid, 3);    // Span all three rows
            Grid.SetZIndex(overlayGrid, 1000);  // Ensure overlay appears above other controls

            // Center content within overlay
            overlayGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            overlayGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            overlayGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            overlayGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            overlayLabel = new TextBlock
            {
                Text = "Processing...",
                Foreground = Brushes.White,
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetRow(overlayLabel, 1);

            overlayProgressBar = new ProgressBar
            {
                Width = 300,
                Height = 20,
                Maximum = 100,
                Visibility = Visibility.Collapsed
            };
            Grid.SetRow(overlayProgressBar, 2);

            cancelButton = new Button
            {
                Content = "Cancel",
                Width = 100,
                Height = 30,
                Background = new SolidColorBrush(Color.FromRgb(200, 0, 0)), // Red background
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                Visibility = Visibility.Collapsed,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            cancelButton.Click += (s, e) => cancellationTokenSource?.Cancel();
            Grid.SetRow(cancelButton, 3);

            overlayGrid.Children.Add(overlayLabel);
            overlayGrid.Children.Add(overlayProgressBar);
            overlayGrid.Children.Add(cancelButton);

            if (mainWindow.Content is Grid mainGrid)
            {
                mainGrid.Children.Add(overlayGrid);
            }
            else
            {
                throw new InvalidOperationException("Main window's content must be a Grid.");
            }
        }

        public void ShowOverlay(string message = "Processing...")
        {
            mainWindow.Dispatcher.Invoke(() =>
            {
                overlayLabel.Text = message;
                overlayProgressBar.Visibility = Visibility.Collapsed;
                cancelButton.Visibility = Visibility.Collapsed;
                overlayGrid.Visibility = Visibility.Visible;
            });
        }

        public void ShowOverlayWithProgress(string message, CancellationTokenSource tokenSource)
        {
            mainWindow.Dispatcher.Invoke(() =>
            {
                cancellationTokenSource = tokenSource;
                overlayLabel.Text = message;
                overlayProgressBar.Value = 0;
                overlayProgressBar.Visibility = Visibility.Visible;
                cancelButton.Visibility = Visibility.Visible;
                overlayGrid.Visibility = Visibility.Visible;
            });
        }

        public void UpdateMessage(string message)
        {
            mainWindow.Dispatcher.Invoke(() =>
            {
                overlayLabel.Text = message;
            });
        }

        public void UpdateProgress(int progress)
        {
            mainWindow.Dispatcher.Invoke(() =>
            {
                overlayProgressBar.Value = progress;
            });
        }

        public void HideOverlay()
        {
            mainWindow.Dispatcher.Invoke(() =>
            {
                overlayGrid.Visibility = Visibility.Collapsed;
            });
        }
    }
}
