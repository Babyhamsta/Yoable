using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using YoableWPF.Managers;
using System.Linq;

namespace YoableWPF
{
    public class ModelListItem : INotifyPropertyChanged
    {
        private string _displayName;
        private string _modelType;
        private YoloModel _model;

        public string DisplayName
        {
            get => _displayName;
            set
            {
                _displayName = value;
                OnPropertyChanged();
            }
        }

        public string ModelType
        {
            get => _modelType;
            set
            {
                _modelType = value;
                OnPropertyChanged();
            }
        }

        public YoloModel Model
        {
            get => _model;
            set
            {
                _model = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public partial class ModelManagerDialog : Window
    {
        private YoloAI yoloAI;
        private ObservableCollection<ModelListItem> modelItems;
        private List<LabelClass> projectClasses;
        private Dictionary<string, Dictionary<int, int>> savedMappings;

        public ModelManagerDialog(YoloAI ai, List<LabelClass> projectClasses = null, Dictionary<string, Dictionary<int, int>> savedMappings = null)
        {
            InitializeComponent();
            yoloAI = ai;
            this.projectClasses = projectClasses ?? new List<LabelClass>();
            this.savedMappings = savedMappings ?? new Dictionary<string, Dictionary<int, int>>();
            modelItems = new ObservableCollection<ModelListItem>();
            ModelListBox.ItemsSource = modelItems;

            // Subscribe to selection changed
            ModelListBox.SelectionChanged += ModelListBox_SelectionChanged;

            RefreshModelList();
            UpdateInfoText();
        }

        private void RefreshModelList()
        {
            modelItems.Clear();
            foreach (var model in yoloAI.GetLoadedModels())
            {
                string modelType = model.ModelInfo.Format switch
                {
                    YoloFormat.YoloV5 => "YOLOv5",
                    YoloFormat.YoloV8 => "YOLOv8",
                    _ => "Unknown"
                };

                modelItems.Add(new ModelListItem
                {
                    DisplayName = model.Name,
                    ModelType = modelType,
                    Model = model
                });
            }
        }

        private void UpdateInfoText()
        {
            int modelCount = yoloAI.GetLoadedModelsCount();
            if (modelCount == 0)
            {
                InfoText.Text = LanguageManager.Instance.GetString("ModelManager_NoModelsLoaded") ?? "No models loaded yet. Click 'Add Model' to load your first YOLO model.";
            }
            else if (modelCount == 1)
            {
                InfoText.Text = LanguageManager.Instance.GetString("ModelManager_OneModelLoaded") ?? "1 model loaded. Add more models to enable ensemble detection.";
            }
            else
            {
                string template = LanguageManager.Instance.GetString("ModelManager_MultipleModelsLoaded") ?? "{0} models loaded for ensemble detection.";
                InfoText.Text = string.Format(template, modelCount);
            }
        }

        private void ModelListBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            bool hasSelection = ModelListBox.SelectedItem != null;
            RemoveButton.IsEnabled = hasSelection;
            EditButton.IsEnabled = hasSelection && projectClasses != null && projectClasses.Count > 0;
        }

        private void AddButton_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "ONNX Model Files|*.onnx",
                Title = LanguageManager.Instance.GetString("ModelManager_SelectModel") ?? "Select YOLO ONNX Model",
                Multiselect = true
            };

            // Set initial directory to last used location
            if (!string.IsNullOrEmpty(Properties.Settings.Default.LastModelDirectory) &&
                Directory.Exists(Properties.Settings.Default.LastModelDirectory))
            {
                openFileDialog.InitialDirectory = Properties.Settings.Default.LastModelDirectory;
            }

            if (openFileDialog.ShowDialog() == true)
            {
                foreach (var file in openFileDialog.FileNames)
                {
                    var loadedModel = yoloAI.LoadModelFromPath(file);
                    if (loadedModel != null)
                    {
                        // Restore saved mapping if available
                        if (savedMappings != null && savedMappings.TryGetValue(file, out var savedMapping))
                        {
                            loadedModel.ClassMapping = new Dictionary<int, int>(savedMapping);
                        }

                        // Open class mapping dialog if project classes exist
                        if (projectClasses != null && projectClasses.Count > 0)
                        {
                            var mappingDialog = new ModelClassMappingDialog(loadedModel, projectClasses);
                            mappingDialog.Owner = this;
                            if (mappingDialog.ShowDialog() == true)
                            {
                                // Mapping is already saved in the model
                            }
                        }
                        else
                        {
                            string message = LanguageManager.Instance.GetString("ModelManager_NeedProjectClasses") ?? 
                                "Please create classes in the project first, then configure model class mapping.\n\n" +
                                "You can add classes in the 'Classes' area on the right panel.";
                            string title = LanguageManager.Instance.GetString("ModelManager_NeedProjectClassesTitle") ?? 
                                "Project Classes Required";
                            MessageBox.Show(message, title,
                                MessageBoxButton.OK,
                                MessageBoxImage.Information);
                        }
                    }
                }
                RefreshModelList();
                UpdateInfoText();
            }
        }

        private void EditButton_Click(object sender, RoutedEventArgs e)
        {
            if (ModelListBox.SelectedItem is ModelListItem selectedItem)
            {
                if (projectClasses == null || projectClasses.Count == 0)
                {
                    string message = LanguageManager.Instance.GetString("ModelManager_NeedProjectClasses") ?? 
                        "Please create classes in the project first, then configure model class mapping.\n\n" +
                        "You can add classes in the 'Classes' area on the right panel.";
                    string title = LanguageManager.Instance.GetString("ModelManager_NeedProjectClassesTitle") ?? 
                        "Project Classes Required";
                    MessageBox.Show(message, title,
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                var mappingDialog = new ModelClassMappingDialog(selectedItem.Model, projectClasses);
                mappingDialog.Owner = this;
                if (mappingDialog.ShowDialog() == true)
                {
                    // Mapping is already saved in the model
                    // The mapping will be persisted when the project is saved
                }
            }
        }

        private void RemoveButton_Click(object sender, RoutedEventArgs e)
        {
            if (ModelListBox.SelectedItem is ModelListItem selectedItem)
            {
                string formatName = selectedItem.Model.ModelInfo.Format switch
                {
                    YoloFormat.YoloV5 => "YOLOv5",
                    YoloFormat.YoloV8 => "YOLOv8",
                    _ => "Unknown"
                };

                string modelIdentifier = $"{selectedItem.Model.Name} ({formatName})";
                yoloAI.RemoveModel(modelIdentifier);
                RefreshModelList();
                UpdateInfoText();
            }
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            string message = LanguageManager.Instance.GetString("ModelManager_ConfirmClearAll") ?? "Remove all loaded models?";
            string title = LanguageManager.Instance.GetString("ModelManager_ClearModels") ?? "Clear Models";
            var result = MessageBox.Show(message, title,
                MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                yoloAI.ClearAllModels();
                RefreshModelList();
                UpdateInfoText();
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}