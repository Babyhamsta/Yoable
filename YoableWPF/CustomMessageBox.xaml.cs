using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace YoableWPF
{
    public partial class CustomMessageBox : Window
    {
        private MessageBoxResult result = MessageBoxResult.None;

        // Icon colors sourced from the shared palette (Themes/Palette.xaml)
        private static SolidColorBrush Pal(string key) => (SolidColorBrush)Application.Current.FindResource(key);
        private static readonly SolidColorBrush InfoBrush = Pal("StatusSuggestedBrush");
        private static readonly SolidColorBrush WarningBrush = Pal("StatusReviewBrush");
        private static readonly SolidColorBrush ErrorBrush = Pal("StatusNoLabelBrush");
        private static readonly SolidColorBrush QuestionBrush = Pal("StatusVerifiedBrush");

        private CustomMessageBox()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // Ensure window is focused when loaded
            Activate();
            Focus();
        }

        public static MessageBoxResult Show(string message)
        {
            return Show(message, "Message", MessageBoxButton.OK, MessageBoxImage.None);
        }

        public static MessageBoxResult Show(string message, string title)
        {
            return Show(message, title, MessageBoxButton.OK, MessageBoxImage.None);
        }

        public static MessageBoxResult Show(string message, string title, MessageBoxButton buttons)
        {
            return Show(message, title, buttons, MessageBoxImage.None);
        }

        public static MessageBoxResult Show(string message, string title, MessageBoxButton buttons, MessageBoxImage icon)
        {
            return Show(null, message, title, buttons, icon);
        }

        public static MessageBoxResult Show(Window owner, string message, string title, MessageBoxButton buttons, MessageBoxImage icon)
        {
            var dialog = new CustomMessageBox
            {
                Title = title
            };

            // Find a suitable owner window
            if (owner != null && owner.IsLoaded)
            {
                dialog.Owner = owner;
                dialog.Topmost = false;
            }
            else
            {
                // Try to find the active window
                var activeWindow = Application.Current?.Windows.OfType<Window>()
                    .FirstOrDefault(w => w.IsActive && w.IsLoaded && w != dialog);

                if (activeWindow != null)
                {
                    dialog.Owner = activeWindow;
                    dialog.Topmost = false;
                }
                else
                {
                    // No owner available - keep topmost and show in taskbar
                    dialog.Topmost = true;
                    dialog.ShowInTaskbar = true;
                    dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                }
            }

            dialog.MessageText.Text = message;
            dialog.SetIcon(icon);
            dialog.CreateButtons(buttons);

            dialog.ShowDialog();
            return dialog.result;
        }

        private void SetIcon(MessageBoxImage icon)
        {
            switch (icon)
            {
                case MessageBoxImage.Information:
                    IconText.Text = "\uE946";
                    IconText.Foreground = InfoBrush;
                    break;
                case MessageBoxImage.Warning:
                    IconText.Text = "\uE7BA";
                    IconText.Foreground = WarningBrush;
                    break;
                case MessageBoxImage.Error:
                    IconText.Text = "\uEA39";
                    IconText.Foreground = ErrorBrush;
                    break;
                case MessageBoxImage.Question:
                    IconText.Text = "\uE9CE";
                    IconText.Foreground = QuestionBrush;
                    break;
                default:
                    IconText.Visibility = Visibility.Collapsed;
                    break;
            }
        }

        private void CreateButtons(MessageBoxButton buttons)
        {
            ButtonPanel.Children.Clear();

            switch (buttons)
            {
                case MessageBoxButton.OK:
                    AddButton("OK", MessageBoxResult.OK, isDefault: true, isPrimary: true);
                    break;

                case MessageBoxButton.OKCancel:
                    AddButton("Cancel", MessageBoxResult.Cancel, isCancel: true);
                    AddButton("OK", MessageBoxResult.OK, isDefault: true, isPrimary: true);
                    break;

                case MessageBoxButton.YesNo:
                    AddButton("No", MessageBoxResult.No, isCancel: true);
                    AddButton("Yes", MessageBoxResult.Yes, isDefault: true, isPrimary: true);
                    break;

                case MessageBoxButton.YesNoCancel:
                    AddButton("Cancel", MessageBoxResult.Cancel, isCancel: true);
                    AddButton("No", MessageBoxResult.No);
                    AddButton("Yes", MessageBoxResult.Yes, isDefault: true, isPrimary: true);
                    break;
            }
        }

        private void AddButton(string content, MessageBoxResult buttonResult, bool isDefault = false, bool isCancel = false, bool isPrimary = false)
        {
            var button = new Button
            {
                Content = content,
                MinWidth = 88,
                Margin = new Thickness(8, 0, 0, 0),
                IsDefault = isDefault,
                IsCancel = isCancel
            };

            // Use the shared theme styles (Themes/Controls.xaml)
            button.Style = (Style)(TryFindResource(isPrimary ? "ModernButton" : "SecondaryButton")
                ?? (isPrimary ? FindResource("AccentButtonStyle") : null));

            button.Click += (s, e) =>
            {
                result = buttonResult;
                DialogResult = buttonResult != MessageBoxResult.Cancel;
                Close();
            };

            ButtonPanel.Children.Add(button);
        }
    }
}
