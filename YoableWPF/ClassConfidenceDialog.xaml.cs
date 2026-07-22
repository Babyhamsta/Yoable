using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using YoableWPF.Managers;

namespace YoableWPF
{
    public sealed class ClassConfidenceThresholdItem : INotifyPropertyChanged
    {
        private double thresholdPercent;

        public int ClassId { get; }
        public string Name { get; }
        public Brush ColorBrush { get; }

        public double ThresholdPercent
        {
            get => thresholdPercent;
            set
            {
                double clampedValue = Math.Clamp(value, 0, 100);
                if (Math.Abs(thresholdPercent - clampedValue) < 0.001)
                    return;

                thresholdPercent = clampedValue;
                OnPropertyChanged();
            }
        }

        public ClassConfidenceThresholdItem(LabelClass labelClass, float threshold)
        {
            ClassId = labelClass.ClassId;
            Name = labelClass.Name;
            ColorBrush = labelClass.ColorBrush;
            thresholdPercent = Math.Clamp(threshold, 0f, 1f) * 100;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public partial class ClassConfidenceDialog : Window
    {
        private readonly float globalConfidence;

        public ObservableCollection<ClassConfidenceThresholdItem> ThresholdItems { get; }
        public Dictionary<int, float> ConfidenceThresholds => ThresholdItems.ToDictionary(
            item => item.ClassId,
            item => (float)(item.ThresholdPercent / 100));

        public ClassConfidenceDialog(
            IEnumerable<LabelClass> classes,
            IReadOnlyDictionary<int, float>? savedThresholds,
            float globalConfidence)
        {
            this.globalConfidence = Math.Clamp(globalConfidence, 0f, 1f);
            ThresholdItems = new ObservableCollection<ClassConfidenceThresholdItem>(
                classes
                    .OrderBy(labelClass => labelClass.ClassId)
                    .Select(labelClass => new ClassConfidenceThresholdItem(
                        labelClass,
                        savedThresholds != null && savedThresholds.TryGetValue(
                            labelClass.ClassId,
                            out float savedThreshold)
                            ? savedThreshold
                            : this.globalConfidence)));

            InitializeComponent();
            DataContext = this;
        }

        private void Reset_Click(object sender, RoutedEventArgs e)
        {
            foreach (ClassConfidenceThresholdItem item in ThresholdItems)
                item.ThresholdPercent = globalConfidence * 100;
        }

        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
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
