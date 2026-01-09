using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.IO;
using Microsoft.Win32;
using YoableWPF.Managers;

namespace YoableWPF
{
    public class ClassMappingItem : INotifyPropertyChanged
    {
        private LabelClass _selectedProjectClass;

        public int ModelClassId { get; set; }
        public string ModelClassName { get; set; }
        public List<LabelClass> ProjectClasses { get; set; }
        
        public string ModelIdDisplayText
        {
            get
            {
                return string.Format(LanguageManager.Instance.GetString("Mapping_ModelID"), ModelClassId);
            }
        }
        
        public LabelClass SelectedProjectClass
        {
            get => _selectedProjectClass;
            set
            {
                if (_selectedProjectClass != value)
                {
                    _selectedProjectClass = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        // 公共方法，用於通知 ModelIdDisplayText 屬性變更
        public void NotifyModelIdDisplayTextChanged()
        {
            OnPropertyChanged(nameof(ModelIdDisplayText));
        }
    }

    public partial class ModelClassMappingDialog : Window
    {
        private YoloModel model;
        private List<LabelClass> projectClasses;
        private List<ClassMappingItem> mappingItems;
        private List<string> storedModelClassNames;

        public Dictionary<int, int> ClassMapping { get; private set; }

        public ModelClassMappingDialog(YoloModel model, List<LabelClass> projectClasses)
        {
            InitializeComponent();
            this.model = model;
            this.projectClasses = projectClasses ?? new List<LabelClass>();
            mappingItems = new List<ClassMappingItem>();
            ClassMapping = new Dictionary<int, int>();

            // Subscribe to language changes
            LanguageManager.Instance.LanguageChanged += LanguageManager_LanguageChanged;

            InitializeMapping();
        }

        private void LanguageManager_LanguageChanged(object sender, EventArgs e)
        {
            // Reload window resources when language changes
            Dispatcher.Invoke(() =>
            {
                ReloadWindowResources();
                // Update nan option name in all mapping items
                foreach (var item in mappingItems)
                {
                    if (item.ProjectClasses != null && item.ProjectClasses.Any())
                    {
                        var nanOption = item.ProjectClasses.FirstOrDefault(c => c.ClassId == -1);
                        if (nanOption != null)
                        {
                            nanOption.Name = LanguageManager.Instance.GetString("Mapping_NotDetected");
                        }
                    }
                    // Trigger property change for ModelIdDisplayText
                    item.NotifyModelIdDisplayTextChanged();
                }
                // Update info text
                UpdateInfoText();
            });
        }

        private void ReloadWindowResources()
        {
            // LanguageManager 已經更新了 Application.Current.Resources
            // 由於窗口使用 DynamicResource，它會自動從 Application.Current.Resources 獲取更新的資源
            // 這裡只需要觸發 UI 更新即可
            try
            {
                // 強制刷新窗口標題和所有使用 DynamicResource 的控件
                this.Title = LanguageManager.Instance.GetString("Mapping_Title");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to reload language resources in ModelClassMappingDialog: {ex.Message}");
            }
        }

        private void UpdateInfoText()
        {
            int projectClassCount = projectClasses?.Count ?? 0;
            string infoText = string.Format(LanguageManager.Instance.GetString("Mapping_InfoFormat"), 
                model.Name, 
                model.ModelInfo.NumClasses, 
                projectClassCount);

            // Check if class names are generic (class_0, class_1, etc.)
            if (storedModelClassNames != null && storedModelClassNames.Any())
            {
                bool hasGenericNames = storedModelClassNames.All(name => name.StartsWith("class_") && int.TryParse(name.Substring(6), out _));
                if (hasGenericNames && storedModelClassNames.Count > 0)
                {
                    infoText += "\n\n" + LanguageManager.Instance.GetString("Mapping_WarningGeneric");
                }
            }

            InfoText.Text = infoText;
        }

        private void InitializeMapping()
        {
            // Get model class names
            InitializeMappingWithClassNames(null);
        }

        private void InitializeMappingWithClassNames(List<string> customClassNames)
        {
            // Get model class names
            var modelClassNames = customClassNames ?? YoloAI.GetModelClassNames(model.ModelPath, model.ModelInfo.NumClasses);

            // Use the actual number of class names found (may be more than detected NumClasses)
            // This handles cases where metadata has more classes than detected from output shape
            int actualNumClasses = modelClassNames.Count;
            
            // Update model info if we found more classes than detected
            if (actualNumClasses > model.ModelInfo.NumClasses)
            {
                model.ModelInfo.NumClasses = actualNumClasses;
            }

            // Create "nan" option (ClassId = -1 means skip detection)
            var nanOption = new LabelClass(LanguageManager.Instance.GetString("Mapping_NotDetected"), "#808080", -1);

            // Create mapping items
            for (int i = 0; i < actualNumClasses; i++)
            {
                string className = i < modelClassNames.Count ? modelClassNames[i] : $"class_{i}";
                
                // Build project classes list with "nan" option at the beginning
                var availableClasses = new List<LabelClass> { nanOption };
                if (projectClasses != null && projectClasses.Any())
                {
                    availableClasses.AddRange(projectClasses);
                }

                // Check if there's an existing mapping
                LabelClass selectedClass = null;
                if (model.ClassMapping != null && model.ClassMapping.ContainsKey(i))
                {
                    // Mapping exists - use the mapped class
                    int mappedProjectClassId = model.ClassMapping[i];
                    selectedClass = availableClasses.FirstOrDefault(c => c.ClassId == mappedProjectClassId);
                }
                else if (model.ClassMapping != null && model.ClassMapping.Count > 0)
                {
                    // Mapping has been configured but this class is not in mapping
                    // This means it was set to "nan" (not detected)
                    selectedClass = nanOption;
                }
                else if (projectClasses != null && projectClasses.Any())
                {
                    // No mapping configured yet - try to find a matching class by name
                    var matchingClass = projectClasses.FirstOrDefault(c => 
                        c.Name.Equals(className, System.StringComparison.OrdinalIgnoreCase));
                    if (matchingClass != null)
                    {
                        selectedClass = matchingClass;
                    }
                    else
                    {
                        // Default to "nan" (not detected) for new mappings
                        selectedClass = nanOption;
                    }
                }
                else
                {
                    // No project classes, default to "nan"
                    selectedClass = nanOption;
                }

                mappingItems.Add(new ClassMappingItem
                {
                    ModelClassId = i,
                    ModelClassName = className,
                    ProjectClasses = availableClasses,
                    SelectedProjectClass = selectedClass ?? nanOption
                });
            }

            MappingList.ItemsSource = mappingItems;

            // Store model class names for UpdateInfoText
            storedModelClassNames = modelClassNames;

            // Update info text
            UpdateInfoText();
        }

        private void OKButton_Click(object sender, RoutedEventArgs e)
        {
            // Build mapping dictionary
            // Only add mappings for classes that are not "nan" (ClassId != -1)
            ClassMapping.Clear();
            foreach (var item in mappingItems)
            {
                if (item.SelectedProjectClass != null && item.SelectedProjectClass.ClassId != -1)
                {
                    ClassMapping[item.ModelClassId] = item.SelectedProjectClass.ClassId;
                }
            }

            // Update model's class mapping
            model.ClassMapping = ClassMapping;

            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            // Unsubscribe from language changes
            if (LanguageManager.Instance != null)
            {
                LanguageManager.Instance.LanguageChanged -= LanguageManager_LanguageChanged;
            }
            base.OnClosed(e);
        }

        private void LoadClassFileButton_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Text Files|*.txt;*.names|All Files|*.*",
                Title = LanguageManager.Instance.GetString("Mapping_SelectClassFile"),
                CheckFileExists = true
            };

            // Set initial directory to model directory
            string modelDir = System.IO.Path.GetDirectoryName(model.ModelPath);
            if (!string.IsNullOrEmpty(modelDir) && System.IO.Directory.Exists(modelDir))
            {
                openFileDialog.InitialDirectory = modelDir;
            }

            if (openFileDialog.ShowDialog() == true)
            {
                try
                {
                    var lines = System.IO.File.ReadAllLines(openFileDialog.FileName)
                        .Where(line => !string.IsNullOrWhiteSpace(line))
                        .Select(line => line.Trim())
                        .Where(line => !line.StartsWith("#"))
                        .ToList();

                    if (lines.Count == 0)
                    {
                        MessageBox.Show(
                            LanguageManager.Instance.GetString("Mapping_ClassFileEmpty"), 
                            LanguageManager.Instance.GetString("Mapping_Error"), 
                            MessageBoxButton.OK, 
                            MessageBoxImage.Warning);
                        return;
                    }

                    // Try to parse as dictionary format
                    Dictionary<int, string> classDict = new Dictionary<int, string>();
                    bool isDictFormat = false;

                    foreach (var line in lines)
                    {
                        if (line.Contains(":"))
                        {
                            var parts = line.Split(new[] { ':' }, 2);
                            if (parts.Length == 2 && int.TryParse(parts[0].Trim(), out int key))
                            {
                                classDict[key] = parts[1].Trim();
                                isDictFormat = true;
                            }
                        }
                        else if (line.Contains("="))
                        {
                            var parts = line.Split(new[] { '=' }, 2);
                            if (parts.Length == 2 && int.TryParse(parts[0].Trim(), out int key))
                            {
                                classDict[key] = parts[1].Trim();
                                isDictFormat = true;
                            }
                        }
                    }

                    List<string> classNames;

                    if (isDictFormat && classDict.Count > 0)
                    {
                        classNames = classDict.OrderBy(kvp => kvp.Key)
                            .Select(kvp => kvp.Value)
                            .ToList();
                    }
                    else
                    {
                        classNames = lines;
                    }

                    // Validate class count
                    if (classNames.Count != model.ModelInfo.NumClasses)
                    {
                        var result = MessageBox.Show(
                            string.Format(LanguageManager.Instance.GetString("Mapping_ClassCountMismatch"), 
                                classNames.Count, 
                                model.ModelInfo.NumClasses, 
                                Math.Min(classNames.Count, model.ModelInfo.NumClasses)),
                            LanguageManager.Instance.GetString("Mapping_ClassCountMismatchTitle"),
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Question);

                        if (result != MessageBoxResult.Yes)
                        {
                            return;
                        }
                    }

                    // Reinitialize mapping with custom class names
                    mappingItems.Clear();
                    InitializeMappingWithClassNames(classNames);
                    MappingList.ItemsSource = mappingItems;
                    // Update info text after loading
                    UpdateInfoText();

                    MessageBox.Show(
                        string.Format(LanguageManager.Instance.GetString("Mapping_LoadSuccess"), classNames.Count),
                        LanguageManager.Instance.GetString("Mapping_LoadSuccessTitle"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        string.Format(LanguageManager.Instance.GetString("Mapping_LoadError"), ex.Message),
                        LanguageManager.Instance.GetString("Mapping_Error"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
        }
    }
}

