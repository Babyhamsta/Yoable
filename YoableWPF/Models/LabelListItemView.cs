using System.Windows.Media;

namespace YoableWPF
{
    public class LabelListItemView
    {
        public LabelData Label { get; }
        public string DisplayText { get; }
        public SolidColorBrush ClassBrush { get; }

        public LabelListItemView(LabelData label, string className, SolidColorBrush classBrush)
        {
            Label = label;
            DisplayText = $"[{className}] {label.Name}";
            ClassBrush = classBrush;
        }
    }
}
