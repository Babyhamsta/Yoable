using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using YoableWPF.Managers;

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

        public ModelManagerDialog(YoloAI ai)
        {
            InitializeComponent();
            yoloAI = ai;
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
                modelItems.Add(new ModelListItem
                {
                    DisplayName = model.Name,
                    ModelType = model.IsYoloV5 ? "YOLOv5" : "YOLOv8",
                    Model = model
                });
            }
        }

        private void UpdateInfoText()
        {
            int modelCount = yoloAI.GetLoadedModelsCount();
            if (modelCount == 0)
            {
                InfoText.Text = "No models loaded yet. Click 'Add Model' to load your first YOLO model.";
            }
            else if (modelCount == 1)
            {
                InfoText.Text = "1 model loaded. Add more models to enable ensemble detection.";
            }
            else
            {
                InfoText.Text = $"{modelCount} models loaded for ensemble detection.";
            }
        }

        private void ModelListBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            RemoveButton.IsEnabled = ModelListBox.SelectedItem != null;
        }

        private void AddButton_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "ONNX Model Files|*.onnx",
                Title = "Select YOLO ONNX Model",
                Multiselect = true
            };

            if (openFileDialog.ShowDialog() == true)
            {
                foreach (var file in openFileDialog.FileNames)
                {
                    yoloAI.LoadModelFromPath(file);
                }
                RefreshModelList();
                UpdateInfoText();
            }
        }

        private void RemoveButton_Click(object sender, RoutedEventArgs e)
        {
            if (ModelListBox.SelectedItem is ModelListItem selectedItem)
            {
                string modelIdentifier = $"{selectedItem.Model.Name} ({(selectedItem.Model.IsYoloV5 ? "YOLOv5" : "YOLOv8")})";
                yoloAI.RemoveModel(modelIdentifier);
                RefreshModelList();
                UpdateInfoText();
            }
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("Remove all loaded models?", "Clear Models",
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