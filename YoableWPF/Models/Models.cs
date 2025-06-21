using System.ComponentModel;
using System.Windows;

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
}