using System.Windows;
using System.Windows.Media;
using Xceed.Wpf.Toolkit;

namespace YoableWPF
{
    public partial class ClassInputDialog : Window
    {
        public string ClassName { get; private set; }
        public string ClassColor { get; private set; }
        private bool isEditMode = false;

        public ClassInputDialog(LabelClass existingClass = null)
        {
            InitializeComponent();
            
            if (existingClass != null)
            {
                // Edit mode
                isEditMode = true;
                Title = "Edit Class";
                ClassNameTextBox.Text = existingClass.Name;
                
                // Set the color picker to the existing color
                try
                {
                    var color = (Color)ColorConverter.ConvertFromString(existingClass.ColorHex);
                    ClassColorPicker.SelectedColor = color;
                }
                catch
                {
                    // Fallback to default if color parsing fails
                    ClassColorPicker.SelectedColor = Color.FromRgb(0xE5, 0x73, 0x73);
                }
            }
            
            ClassNameTextBox.Focus();
            
            // Subscribe to color changes for live preview
            ClassColorPicker.SelectedColorChanged += ColorPicker_SelectedColorChanged;
            ClassNameTextBox.TextChanged += ClassNameTextBox_TextChanged;
            
            // Initial preview update
            UpdatePreview();
        }

        private void ColorPicker_SelectedColorChanged(object sender, RoutedPropertyChangedEventArgs<Color?> e)
        {
            UpdatePreview();
        }

        private void ClassNameTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            UpdatePreview();
        }

        private void UpdatePreview()
        {
            // Update preview text
            PreviewText.Text = string.IsNullOrWhiteSpace(ClassNameTextBox.Text) 
                ? "class name" 
                : ClassNameTextBox.Text.Trim();
            
            // Update preview colors
            if (ClassColorPicker.SelectedColor.HasValue)
            {
                var color = ClassColorPicker.SelectedColor.Value;
                PreviewColorIndicator.Fill = new SolidColorBrush(color);
                PreviewBorder.BorderBrush = new SolidColorBrush(color);
                PreviewBorder.Background = new SolidColorBrush(
                    Color.FromArgb(0x44, color.R, color.G, color.B));
            }
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            ClassName = ClassNameTextBox.Text.Trim();
            
            if (string.IsNullOrWhiteSpace(ClassName))
            {
                System.Windows.MessageBox.Show(
                    "Please enter a class name.",
                    "Validation Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                ClassNameTextBox.Focus();
                return;
            }
            
            // Get selected color as hex
            if (ClassColorPicker.SelectedColor.HasValue)
            {
                var color = ClassColorPicker.SelectedColor.Value;
                ClassColor = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
            }
            else
            {
                ClassColor = "#E57373"; // Default red
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
