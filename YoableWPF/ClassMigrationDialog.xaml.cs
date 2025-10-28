using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace YoableWPF
{
    public partial class ClassMigrationDialog : Window
    {
        public int TargetClassId { get; private set; }
        public bool DeleteLabels { get; private set; }

        private List<LabelClass> availableClasses;
        private LabelClass classToRemove;

        public ClassMigrationDialog(List<LabelClass> allClasses, LabelClass classToRemove, int labelCount)
        {
            InitializeComponent();
            
            this.classToRemove = classToRemove;
            
            // Filter out the class being removed
            availableClasses = allClasses.Where(c => c.ClassId != classToRemove.ClassId).ToList();
            
            // Set up UI
            LabelCountText.Text = labelCount.ToString();
            TargetClassComboBox.ItemsSource = availableClasses;
            
            if (availableClasses.Any())
            {
                TargetClassComboBox.SelectedIndex = 0;
            }
        }

        private void MigrateRadio_Checked(object sender, RoutedEventArgs e)
        {
            // Guard against event firing during InitializeComponent()
            if (TargetClassComboBox != null)
            {
                TargetClassComboBox.IsEnabled = true;
                DeleteLabels = false;
            }
        }

        private void DeleteRadio_Checked(object sender, RoutedEventArgs e)
        {
            // Guard against event firing during InitializeComponent()
            if (TargetClassComboBox != null)
            {
                TargetClassComboBox.IsEnabled = false;
                DeleteLabels = true;
            }
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            if (MigrateRadio.IsChecked == true)
            {
                if (TargetClassComboBox.SelectedItem is LabelClass targetClass)
                {
                    TargetClassId = targetClass.ClassId;
                    DeleteLabels = false;
                }
                else
                {
                    MessageBox.Show(
                        "Please select a target class.",
                        "Validation Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }
            }
            else
            {
                // Confirm deletion
                var result = MessageBox.Show(
                    $"Are you sure you want to delete all labels with class '{classToRemove.Name}'?\n\nThis action cannot be undone!",
                    "Confirm Deletion",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                
                if (result != MessageBoxResult.Yes)
                    return;
                
                DeleteLabels = true;
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
