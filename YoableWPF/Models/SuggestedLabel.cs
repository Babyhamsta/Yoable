using System;
using System.Windows;

namespace YoableWPF
{
    public enum SuggestionSource
    {
        ImageSimilarity,
        ObjectSimilarity,
        Tracking
    }

    public class SuggestedLabel
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public int ClassId { get; set; }
        public double Score { get; set; }
        public SuggestionSource Source { get; set; }
        public string SourceImage { get; set; }
        public string SourceLabelId { get; set; }

        public Rect ToRect()
        {
            return new Rect(X, Y, Width, Height);
        }

        public static SuggestedLabel FromRect(Rect rect, int classId, SuggestionSource source, double score, string sourceImage = null, string sourceLabelId = null)
        {
            return new SuggestedLabel
            {
                X = rect.X,
                Y = rect.Y,
                Width = rect.Width,
                Height = rect.Height,
                ClassId = classId,
                Source = source,
                Score = score,
                SourceImage = sourceImage,
                SourceLabelId = sourceLabelId
            };
        }
    }
}
