using System.ComponentModel;

namespace Yoable.Models;

// Shared models used across managers

public enum ImageStatus
{
    NoLabel,
    Negative,              // NEW: Explicitly negative samples
    VerificationNeeded,
    Background,            // NEW: Background/negative training samples
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
                OnPropertyChanged(nameof(StatusText)); // Update StatusText when Status changes
            }
        }
    }

    public string StatusText => Status switch
    {
        ImageStatus.NoLabel => "NO LABEL",
        ImageStatus.VerificationNeeded => "REVIEW",
        ImageStatus.Verified => "VERIFIED",
        ImageStatus.Negative => "NEGATIVE",
        ImageStatus.Background => "BACKGROUND",
        _ => ""
    };

    public ImageListItem(string fileName, ImageStatus status)
    {
        FileName = fileName;
        _status = status;
    }

    public override string ToString()
    {
        return FileName;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
