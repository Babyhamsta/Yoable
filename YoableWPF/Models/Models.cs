using System.ComponentModel;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Media;

namespace YoableWPF
{
    // Shared models used across managers

    public enum ImageStatus
    {
        NoLabel = 0,
        VerificationNeeded = 1,
        Verified = 2,
        Suggested = 3
    }

    /// <summary>
    /// How the image-list class filter matches an image against the selected classes.
    /// Include: image has at least one of the selected classes (OR).
    /// All: image contains every selected class at least once (AND).
    /// Only: every label in the image belongs to the selected set (no other class present).
    /// Exclude: image contains none of the selected classes.
    /// </summary>
    public enum ClassFilterMode
    {
        Include = 0,
        All = 2,
        Only = 1,
        Exclude = 3
    }

    /// <summary>
    /// View item for the class-filter checkbox list in the annotate tab's left panel.
    /// </summary>
    public class ClassFilterItem : INotifyPropertyChanged
    {
        private bool _isChecked = true;

        public int ClassId { get; init; }
        public string Name { get; init; }
        public Brush ColorBrush { get; init; }

        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                if (_isChecked != value)
                {
                    _isChecked = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }

    public class ImageListItem : INotifyPropertyChanged
    {
        private ImageStatus _status;

        public string FileName { get; set; }

        public ImageStatus Status
        {
            get => _status;
            set
            {
                if (_status != value)
                {
                    _status = value;
                    OnPropertyChanged(nameof(Status));
                }
            }
        }

        public ImageListItem(string fileName, ImageStatus status)
        {
            FileName = fileName;
            _status = status;
        }

        public override string ToString()
        {
            return FileName;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class LabelClass : INotifyPropertyChanged
    {
        private string _name;
        private string _colorHex;
        private SolidColorBrush _colorBrush;
        
        public int ClassId { get; set; }
        
        public string Name
        {
            get => _name;
            set
            {
                if (_name != value)
                {
                    _name = value;
                    OnPropertyChanged(nameof(Name));
                    OnPropertyChanged(nameof(DisplayText));
                }
            }
        }
        
        public string ColorHex
        {
            get => _colorHex;
            set
            {
                if (_colorHex != value)
                {
                    _colorHex = value;
                    _colorBrush = CreateFrozenBrush(_colorHex);
                    OnPropertyChanged(nameof(ColorHex));
                    OnPropertyChanged(nameof(ColorBrush));
                }
            }
        }
        
        // Helper properties for UI binding
        [JsonIgnore]
        public string DisplayText => ClassId == -1 ? Name : $"{Name} (ID: {ClassId})";
        
        [JsonIgnore]
        public SolidColorBrush ColorBrush => _colorBrush ??= CreateFrozenBrush(_colorHex);
        
        // Parameterless constructor for JSON deserialization
        public LabelClass()
        {
            _name = "default";
            ColorHex = "#E57373";
            ClassId = 0;
        }
        
        public LabelClass(string name, string colorHex, int classId)
        {
            Name = name;
            ColorHex = colorHex;
            ClassId = classId;
        }

        private static SolidColorBrush CreateFrozenBrush(string colorHex)
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(colorHex);
                var brush = new SolidColorBrush(color);
                brush.Freeze();
                return brush;
            }
            catch
            {
                var fallback = new SolidColorBrush(Color.FromRgb(0xE5, 0x73, 0x73));
                fallback.Freeze();
                return fallback;
            }
        }
        
        public event PropertyChangedEventHandler PropertyChanged;
        
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
