using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml.Media;

namespace PhotoCut.Models;

public enum FileFormat
{
    All,
    Jpg,
    Arw
}

public enum DateFilter
{
    All,
    Today,
    Yesterday,
    Last7Days,
    Last30Days,
    Custom
}

public enum DeleteMode
{
    RecycleBin,
    Permanent
}

public class PhotoItem : INotifyPropertyChanged
{
    private bool _isSelected;
    private ImageSource? _thumbnail;

    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string Extension { get; set; } = string.Empty;
    public DateTime DateModified { get; set; }
    public long FileSize { get; set; }
    public bool IsArw => Extension.Equals(".arw", StringComparison.OrdinalIgnoreCase);
    public bool IsJpg => Extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
                          Extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase);
    public bool HasPairedFile { get; set; }
    public string? PairedFilePath { get; set; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                OnPropertyChanged();
            }
        }
    }

    public ImageSource? Thumbnail
    {
        get => _thumbnail;
        set
        {
            _thumbnail = value;
            OnPropertyChanged();
        }
    }

    public string SizeText
    {
        get
        {
            if (FileSize < 1024) return $"{FileSize} B";
            if (FileSize < 1024 * 1024) return $"{FileSize / 1024.0:F1} KB";
            if (FileSize < 1024 * 1024 * 1024) return $"{FileSize / (1024.0 * 1024.0):F1} MB";
            return $"{FileSize / (1024.0 * 1024.0 * 1024.0):F1} GB";
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
