using System.Windows;
using System.Windows.Media;
using Xceed.Wpf.Toolkit;
using YoableWPF.Managers;

namespace YoableWPF
{
    public partial class ClassInputDialog : Window
    {
        public string ClassName { get; private set; }
        public string ClassColor { get; private set; }
        public bool ShouldMerge { get; private set; }
        public LabelClass MergeTargetClass { get; private set; }
        private bool isEditMode = false;
        private LabelClass currentClass;
        private List<LabelClass> availableClasses;

        public ClassInputDialog(LabelClass existingClass = null, List<LabelClass> availableClasses = null)
        {
            InitializeComponent();
            this.availableClasses = availableClasses ?? new List<LabelClass>();
            this.currentClass = existingClass;
            
            if (existingClass != null)
            {
                // Edit mode
                isEditMode = true;
                Title = "Edit Class";
                OKButton.Content = "OK";
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

                // Show merge option in edit mode
                MergeOptionBorder.Visibility = Visibility.Visible;
                
                // Populate merge target combo box (exclude current class)
                MergeTargetComboBox.ItemsSource = this.availableClasses
                    .Where(c => c.ClassId != existingClass.ClassId)
                    .ToList();
            }
            else
            {
                Title = "Add Class";
                OKButton.Content = "Add Class";
            }
            
            ClassNameTextBox.Focus();
            
            // Subscribe to color changes for live preview
            ClassColorPicker.SelectedColorChanged += ColorPicker_SelectedColorChanged;
            ClassNameTextBox.TextChanged += ClassNameTextBox_TextChanged;
            
            // Initial preview update
            UpdatePreview();
        }

        private void MergeCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            MergeTargetComboBox.IsEnabled = true;
            if (MergeTargetComboBox.Items.Count > 0 && MergeTargetComboBox.SelectedItem == null)
            {
                MergeTargetComboBox.SelectedIndex = 0;
            }
        }

        private void MergeCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            MergeTargetComboBox.IsEnabled = false;
            MergeTargetComboBox.SelectedItem = null;
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
                CustomMessageBox.Show(
                    LanguageManager.Instance.GetString("Msg_Class_NameRequired") ?? "Please enter a class name.",
                    LanguageManager.Instance.GetString("Msg_ValidationError") ?? "Validation Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                ClassNameTextBox.Focus();
                return;
            }

            // Check merge option
            ShouldMerge = MergeCheckBox.IsChecked == true;
            if (ShouldMerge)
            {
                if (MergeTargetComboBox.SelectedItem == null)
                {
                    CustomMessageBox.Show(
                        LanguageManager.Instance.GetString("Msg_Class_SelectMergeTarget") ?? "Please select a target class to merge into.",
                        LanguageManager.Instance.GetString("Msg_ValidationError") ?? "Validation Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    MergeTargetComboBox.Focus();
                    return;
                }
                MergeTargetClass = MergeTargetComboBox.SelectedItem as LabelClass;
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
