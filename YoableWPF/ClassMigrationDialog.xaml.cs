using System.Collections.Generic;
using System.Linq;
using System.Windows;
using YoableWPF.Managers;

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
                    CustomMessageBox.Show(
                        LanguageManager.Instance.GetString("Msg_Class_SelectTarget") ?? "Please select a target class.",
                        LanguageManager.Instance.GetString("Msg_ValidationError") ?? "Validation Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }
            }
            else
            {
                // Confirm deletion
                var result = CustomMessageBox.Show(
                    string.Format(LanguageManager.Instance.GetString("Msg_Class_ConfirmDeleteLabels") ?? "Are you sure you want to delete all labels with class '{0}'?\n\nThis action cannot be undone!", classToRemove.Name),
                    LanguageManager.Instance.GetString("Msg_ConfirmDeletion") ?? "Confirm Deletion",
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
