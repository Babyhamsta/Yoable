using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Yoable.Managers;
using Yoable.Services;

namespace Yoable.Desktop
{
    public class ModelListItem : INotifyPropertyChanged
    {
        private string _displayName = "";
        private string _modelType = "";
        private YoloModel? _model;

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

        public YoloModel? Model
        {
            get => _model;
            set
            {
                _model = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public partial class ModelManagerDialog : Window
    {
        private YoloAI? _yoloAI;
        private IDialogService _dialogService;
        private ObservableCollection<ModelListItem> _modelItems;

        private TextBlock? _infoText;
        private ListBox? _modelListBox;
        private Button? _addButton;
        private Button? _removeButton;
        private Button? _clearButton;
        private Button? _closeButton;

        public ModelManagerDialog()
        {
            InitializeComponent();
            _dialogService = new AvaloniaDialogService();
            _modelItems = new ObservableCollection<ModelListItem>();
            GetControls();
            WireUpEventHandlers();
        }

        public ModelManagerDialog(YoloAI ai) : this()
        {
            _yoloAI = ai;
            if (_modelListBox != null)
                _modelListBox.ItemsSource = _modelItems;
            RefreshModelList();
            UpdateInfoText();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void GetControls()
        {
            _infoText = this.FindControl<TextBlock>("InfoText");
            _modelListBox = this.FindControl<ListBox>("ModelListBox");
            _addButton = this.FindControl<Button>("AddButton");
            _removeButton = this.FindControl<Button>("RemoveButton");
            _clearButton = this.FindControl<Button>("ClearButton");
            _closeButton = this.FindControl<Button>("CloseButton");
        }

        private void WireUpEventHandlers()
        {
            if (_modelListBox != null)
                _modelListBox.SelectionChanged += ModelListBox_SelectionChanged;
            if (_addButton != null)
                _addButton.Click += AddButton_Click;
            if (_removeButton != null)
                _removeButton.Click += RemoveButton_Click;
            if (_clearButton != null)
                _clearButton.Click += ClearButton_Click;
            if (_closeButton != null)
                _closeButton.Click += CloseButton_Click;
        }

        private void RefreshModelList()
        {
            if (_yoloAI == null) return;

            _modelItems.Clear();
            foreach (var model in _yoloAI.GetLoadedModels())
            {
                string modelType = model.ModelInfo.Format switch
                {
                    YoloFormat.YoloV5 => "YOLOv5",
                    YoloFormat.YoloV8 => "YOLOv8",
                    _ => "Unknown"
                };

                _modelItems.Add(new ModelListItem
                {
                    DisplayName = model.Name,
                    ModelType = modelType,
                    Model = model
                });
            }
        }

        private void UpdateInfoText()
        {
            if (_yoloAI == null || _infoText == null) return;

            int modelCount = _yoloAI.GetLoadedModelsCount();
            if (modelCount == 0)
            {
                _infoText.Text = "No models loaded yet. Click 'Add Model' to load your first YOLO model.";
            }
            else if (modelCount == 1)
            {
                _infoText.Text = "1 model loaded. Add more models to enable ensemble detection.";
            }
            else
            {
                _infoText.Text = $"{modelCount} models loaded for ensemble detection.";
            }
        }

        private void ModelListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_removeButton != null)
                _removeButton.IsEnabled = _modelListBox?.SelectedItem != null;
        }

        private async void AddButton_Click(object? sender, RoutedEventArgs e)
        {
            if (_yoloAI == null) return;

            var storageProvider = StorageProvider;
            if (storageProvider == null) return;

            var filePickerOptions = new FilePickerOpenOptions
            {
                Title = "Select YOLO ONNX Model",
                AllowMultiple = true,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("ONNX Model Files")
                    {
                        Patterns = new[] { "*.onnx" }
                    }
                }
            };

            var files = await storageProvider.OpenFilePickerAsync(filePickerOptions);

            if (files.Count > 0)
            {
                foreach (var file in files)
                {
                    var filePath = file.Path.LocalPath;
                    _yoloAI.LoadModelFromPath(filePath);
                }
                RefreshModelList();
                UpdateInfoText();
            }
        }

        private void RemoveButton_Click(object? sender, RoutedEventArgs e)
        {
            if (_yoloAI == null || _modelListBox == null) return;

            if (_modelListBox.SelectedItem is ModelListItem selectedItem && selectedItem.Model != null)
            {
                string formatName = selectedItem.Model.ModelInfo.Format switch
                {
                    YoloFormat.YoloV5 => "YOLOv5",
                    YoloFormat.YoloV8 => "YOLOv8",
                    _ => "Unknown"
                };

                string modelIdentifier = $"{selectedItem.Model.Name} ({formatName})";
                _yoloAI.RemoveModel(modelIdentifier);
                RefreshModelList();
                UpdateInfoText();
            }
        }

        private async void ClearButton_Click(object? sender, RoutedEventArgs e)
        {
            if (_yoloAI == null) return;

            var result = await _dialogService.ShowYesNoCancelAsync(
                "Clear Models",
                "Remove all loaded models?");

            if (result == DialogResult.Yes)
            {
                _yoloAI.ClearAllModels();
                RefreshModelList();
                UpdateInfoText();
            }
        }

        private void CloseButton_Click(object? sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
