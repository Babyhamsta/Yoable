using System.Windows.Media;

namespace YoableWPF
{
    public class LabelListItemView
    {
        public LabelData Label { get; }
        public SuggestedLabel Suggestion { get; }
        public string DisplayText { get; }
        public SolidColorBrush ClassBrush { get; }
        public bool IsSuggestion => Suggestion != null;
        public string SourceText { get; }
        public string ScoreText { get; }

        public LabelListItemView(LabelData label, string className, SolidColorBrush classBrush)
        {
            Label = label;
            DisplayText = $"[{className}] {label.Name}";
            ClassBrush = classBrush;
        }

        public LabelListItemView(SuggestedLabel suggestion, string className, SolidColorBrush classBrush, string sourceText)
        {
            Suggestion = suggestion;
            DisplayText = $"[{className}] Suggested";
            ClassBrush = classBrush;
            SourceText = sourceText;
            ScoreText = suggestion != null ? $"{suggestion.Score:P0}" : string.Empty;
        }
    }
}
