using System.Windows;
using System.Windows.Controls;
using YoableWPF.Managers;

namespace YoableWPF
{
    public partial class YoutubeDownloadWindow : Window
    {
        public string YoutubeUrl { get; private set; }
        public int desiredFps { get; private set; }
        public int FrameSize { get; private set; }

        public YoutubeDownloadWindow()
        {
            InitializeComponent();
        }

        private void DownloadButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(YoutubeUrlTextBox.Text))
            {
                CustomMessageBox.Show(
                    LanguageManager.Instance.GetString("Msg_YouTube_EnterURL") ?? "Please enter a YouTube URL",
                    LanguageManager.Instance.GetString("Msg_ValidationError") ?? "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            YoutubeUrl = YoutubeUrlTextBox.Text;
            desiredFps = (int)FPSSlider.Value;

            // Extract only the first number (before 'x')
            string selectedSize = (FrameSizeComboBox.SelectedItem as ComboBoxItem)?.Content.ToString();
            FrameSize = int.Parse(selectedSize.Split('x')[0]);

            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
