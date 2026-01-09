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
    }

    public partial class ModelClassMappingDialog : Window
    {
        private YoloModel model;
        private List<LabelClass> projectClasses;
        private List<ClassMappingItem> mappingItems;

        public Dictionary<int, int> ClassMapping { get; private set; }

        public ModelClassMappingDialog(YoloModel model, List<LabelClass> projectClasses)
        {
            InitializeComponent();
            this.model = model;
            this.projectClasses = projectClasses ?? new List<LabelClass>();
            mappingItems = new List<ClassMappingItem>();
            ClassMapping = new Dictionary<int, int>();

            InitializeMapping();
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
            var nanOption = new LabelClass("nan (不檢測)", "#808080", -1);

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
                        // Default to first project class (not nan) for new mappings
                        selectedClass = projectClasses[0];
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

            // Update info text
            int projectClassCount = projectClasses?.Count ?? 0;
            string infoText = $"模型: {model.Name}\n" +
                          $"類別數量: {model.ModelInfo.NumClasses}\n" +
                          $"項目類別數量: {projectClassCount}\n" +
                          $"提示: 選擇 'nan (不檢測)' 可跳過該類別的檢測";

            // Check if class names are generic (class_0, class_1, etc.)
            bool hasGenericNames = modelClassNames.All(name => name.StartsWith("class_") && int.TryParse(name.Substring(6), out _));
            if (hasGenericNames && modelClassNames.Count > 0)
            {
                infoText += $"\n\n⚠ 警告: 未找到類別文件，顯示通用類別名稱。\n請點擊右上角「載入類別文件」按鈕手動載入正確的類別名稱。";
            }

            InfoText.Text = infoText;
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

        private void LoadClassFileButton_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Text Files|*.txt;*.names|All Files|*.*",
                Title = "選擇類別文件",
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
                        MessageBox.Show("類別文件為空或格式不正確。", "錯誤", MessageBoxButton.OK, MessageBoxImage.Warning);
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
                            $"類別文件包含 {classNames.Count} 個類別，但模型有 {model.ModelInfo.NumClasses} 個類別。\n\n" +
                            $"是否仍要使用此文件？\n" +
                            $"(將使用前 {Math.Min(classNames.Count, model.ModelInfo.NumClasses)} 個類別)",
                            "類別數量不匹配",
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

                    MessageBox.Show(
                        $"成功載入 {classNames.Count} 個類別名稱。",
                        "載入成功",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        $"載入類別文件時發生錯誤：\n{ex.Message}",
                        "錯誤",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
        }
    }
}

