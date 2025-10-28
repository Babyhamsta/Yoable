using System.ComponentModel;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Media;

namespace YoableWPF
{
    // Shared models used across managers

    public enum ImageStatus
    {
        NoLabel,
        VerificationNeeded,
        Verified
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
                    OnPropertyChanged(nameof(ColorHex));
                    OnPropertyChanged(nameof(ColorBrush));
                }
            }
        }
        
        // Helper properties for UI binding
        [JsonIgnore]
        public string DisplayText => $"{Name} (ID: {ClassId})";
        
        [JsonIgnore]
        public SolidColorBrush ColorBrush => new SolidColorBrush(
            (Color)ColorConverter.ConvertFromString(ColorHex));
        
        // Parameterless constructor for JSON deserialization
        public LabelClass()
        {
            _name = "default";
            _colorHex = "#E57373";
            ClassId = 0;
        }
        
        public LabelClass(string name, string colorHex, int classId)
        {
            Name = name;
            ColorHex = colorHex;
            ClassId = classId;
        }
        
        public event PropertyChangedEventHandler PropertyChanged;
        
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
