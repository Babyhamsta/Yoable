using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Yoable.Models;

/// <summary>
/// Information about a recently opened project
/// </summary>
public class RecentProjectInfo : INotifyPropertyChanged
{
    private string _projectName;
    private string _projectPath;
    private DateTime _lastOpened;
    private string _lastOpenedText;

    public string ProjectName
    {
        get => _projectName;
        set
        {
            _projectName = value;
            OnPropertyChanged();
        }
    }

    public string ProjectPath
    {
        get => _projectPath;
        set
        {
            _projectPath = value;
            OnPropertyChanged();
        }
    }

    public DateTime LastOpened
    {
        get => _lastOpened;
        set
        {
            _lastOpened = value;
            OnPropertyChanged();
            UpdateLastOpenedText();
        }
    }

    public string LastOpenedText
    {
        get => _lastOpenedText;
        private set
        {
            _lastOpenedText = value;
            OnPropertyChanged();
        }
    }

    // Optional: Store preview thumbnail path
    public string PreviewImagePath { get; set; }

    public RecentProjectInfo()
    {
        UpdateLastOpenedText();
    }

    public RecentProjectInfo(string name, string path, DateTime lastOpened)
    {
        ProjectName = name;
        ProjectPath = path;
        LastOpened = lastOpened;
    }

    /// <summary>
    /// Updates the human-readable last opened text
    /// </summary>
    public void UpdateLastOpenedText()
    {
        TimeSpan timeSince = DateTime.Now - LastOpened;

        if (timeSince.TotalMinutes < 1)
        {
            LastOpenedText = "Just now";
        }
        else if (timeSince.TotalMinutes < 60)
        {
            int minutes = (int)timeSince.TotalMinutes;
            LastOpenedText = $"{minutes} minute{(minutes != 1 ? "s" : "")} ago";
        }
        else if (timeSince.TotalHours < 24)
        {
            int hours = (int)timeSince.TotalHours;
            LastOpenedText = $"{hours} hour{(hours != 1 ? "s" : "")} ago";
        }
        else if (timeSince.TotalDays < 7)
        {
            int days = (int)timeSince.TotalDays;
            if (days == 1)
                LastOpenedText = "Yesterday";
            else
                LastOpenedText = $"{days} days ago";
        }
        else if (timeSince.TotalDays < 30)
        {
            int weeks = (int)(timeSince.TotalDays / 7);
            LastOpenedText = $"{weeks} week{(weeks != 1 ? "s" : "")} ago";
        }
        else if (timeSince.TotalDays < 365)
        {
            int months = (int)(timeSince.TotalDays / 30);
            LastOpenedText = $"{months} month{(months != 1 ? "s" : "")} ago";
        }
        else
        {
            LastOpenedText = LastOpened.ToString("MMM dd, yyyy");
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
