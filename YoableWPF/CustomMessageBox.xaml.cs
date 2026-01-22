using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace YoableWPF
{
    public partial class CustomMessageBox : Window
    {
        private MessageBoxResult result = MessageBoxResult.None;

        // Icon colors
        private static readonly SolidColorBrush InfoBrush = CreateFrozenBrush(0x64, 0xB5, 0xF6);
        private static readonly SolidColorBrush WarningBrush = CreateFrozenBrush(0xFF, 0xB7, 0x4D);
        private static readonly SolidColorBrush ErrorBrush = CreateFrozenBrush(0xE5, 0x73, 0x73);
        private static readonly SolidColorBrush QuestionBrush = CreateFrozenBrush(0x81, 0xC7, 0x84);

        private static SolidColorBrush CreateFrozenBrush(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }

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

            if (isPrimary)
            {
                button.Style = (Style)FindResource("AccentButtonStyle");
            }

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
