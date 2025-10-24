using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Yoable.Services;

namespace Yoable.Desktop
{
    public partial class YoutubeDownloadWindow : Window
    {
        private IDialogService _dialogService;

        private TextBox? _youtubeUrlTextBox;
        private Slider? _fpsSlider;
        private ComboBox? _frameSizeComboBox;
        private Button? _downloadButton;
        private Button? _cancelButton;

        public string YoutubeUrl { get; private set; } = "";
        public int DesiredFps { get; private set; }
        public int FrameSize { get; private set; }

        public YoutubeDownloadWindow()
        {
            InitializeComponent();
            _dialogService = new AvaloniaDialogService();
            GetControls();
            WireUpEventHandlers();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void GetControls()
        {
            _youtubeUrlTextBox = this.FindControl<TextBox>("YoutubeUrlTextBox");
            _fpsSlider = this.FindControl<Slider>("FPSSlider");
            _frameSizeComboBox = this.FindControl<ComboBox>("FrameSizeComboBox");
            _downloadButton = this.FindControl<Button>("DownloadButton");
            _cancelButton = this.FindControl<Button>("CancelButton");
        }

        private void WireUpEventHandlers()
        {
            if (_downloadButton != null)
                _downloadButton.Click += DownloadButton_Click;
            if (_cancelButton != null)
                _cancelButton.Click += CancelButton_Click;
        }

        private async void DownloadButton_Click(object? sender, RoutedEventArgs e)
        {
            if (_youtubeUrlTextBox == null || _fpsSlider == null || _frameSizeComboBox == null)
                return;

            if (string.IsNullOrWhiteSpace(_youtubeUrlTextBox.Text))
            {
                await _dialogService.ShowWarningAsync("Validation Error", "Please enter a YouTube URL");
                return;
            }

            YoutubeUrl = _youtubeUrlTextBox.Text;
            DesiredFps = (int)_fpsSlider.Value;

            // Extract only the first number (before 'x')
            string? selectedSize = (_frameSizeComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString();
            if (selectedSize != null && selectedSize.Contains('x'))
            {
                FrameSize = int.Parse(selectedSize.Split('x')[0]);
            }
            else
            {
                FrameSize = 640; // Default
            }

            Close(true);
        }

        private void CancelButton_Click(object? sender, RoutedEventArgs e)
        {
            Close(false);
        }
    }
}
