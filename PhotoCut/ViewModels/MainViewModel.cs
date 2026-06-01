using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using PhotoCut.Models;
using PhotoCut.Services;

namespace PhotoCut.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    private readonly FileService _fileService = new();

    private string _folderPath = string.Empty;
    private bool _isLoading;
    private int _totalCount;
    private int _selectedCount;
    private FileFormat _selectedFormat = FileFormat.All;
    private DateFilter _selectedDateFilter = DateFilter.All;
    private DateTimeOffset _customDateStart = DateTimeOffset.Now.AddDays(-7);
    private DateTimeOffset _customDateEnd = DateTimeOffset.Now;
    private PhotoItem? _selectedPhoto;

    public string FolderPath
    {
        get => _folderPath;
        set { _folderPath = value; OnPropertyChanged(); }
    }

    public bool IsLoading
    {
        get => _isLoading;
        set { _isLoading = value; OnPropertyChanged(); }
    }

    public int TotalCount
    {
        get => _totalCount;
        set { _totalCount = value; OnPropertyChanged(); }
    }

    public int SelectedCount
    {
        get => _selectedCount;
        set { _selectedCount = value; OnPropertyChanged(); }
    }

    public FileFormat SelectedFormat
    {
        get => _selectedFormat;
        set { _selectedFormat = value; OnPropertyChanged(); }
    }

    public DateFilter SelectedDateFilter
    {
        get => _selectedDateFilter;
        set { _selectedDateFilter = value; OnPropertyChanged(); }
    }

    public DateTimeOffset CustomDateStart
    {
        get => _customDateStart;
        set { _customDateStart = value; OnPropertyChanged(); }
    }

    public DateTimeOffset CustomDateEnd
    {
        get => _customDateEnd;
        set { _customDateEnd = value; OnPropertyChanged(); }
    }

    public PhotoItem? SelectedPhoto
    {
        get => _selectedPhoto;
        set { _selectedPhoto = value; OnPropertyChanged(); }
    }

    public ObservableCollection<PhotoItem> Photos { get; } = new();
    private List<PhotoItem> _allPhotos = new();

    public async Task LoadPhotosAsync()
    {
        if (string.IsNullOrEmpty(FolderPath)) return;

        IsLoading = true;
        Photos.Clear();
        _allPhotos.Clear();

        try
        {
            var photos = await _fileService.ScanFolderAsync(FolderPath);
            _allPhotos = photos;

            foreach (var photo in photos)
            {
                photo.PropertyChanged += OnPhotoPropertyChanged;
                Photos.Add(photo);
            }

            TotalCount = Photos.Count;
            SelectedCount = 0;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Load error: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void OnPhotoPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PhotoItem.IsSelected))
        {
            UpdateSelectedCount();
        }
    }

    public void ApplyFilters()
    {
        Photos.Clear();

        var filtered = _allPhotos.AsEnumerable();

        if (SelectedFormat != FileFormat.All)
        {
            filtered = SelectedFormat == FileFormat.Arw
                ? filtered.Where(p => p.IsArw)
                : filtered.Where(p => p.IsJpg);
        }

        filtered = SelectedDateFilter switch
        {
            DateFilter.Today => filtered.Where(p => p.DateModified.Date == DateTime.Today),
            DateFilter.Yesterday => filtered.Where(p => p.DateModified.Date == DateTime.Today.AddDays(-1)),
            DateFilter.Last7Days => filtered.Where(p => p.DateModified >= DateTime.Today.AddDays(-7)),
            DateFilter.Last30Days => filtered.Where(p => p.DateModified >= DateTime.Today.AddDays(-30)),
            DateFilter.Custom => filtered.Where(p => p.DateModified >= CustomDateStart.Date &&
                                                      p.DateModified <= CustomDateEnd.Date.AddDays(1)),
            _ => filtered
        };

        foreach (var photo in filtered)
        {
            Photos.Add(photo);
        }

        TotalCount = Photos.Count;
        UpdateSelectedCount();
    }

    public void SelectAllItems()
    {
        foreach (var photo in Photos)
        {
            photo.IsSelected = true;
        }
        UpdateSelectedCount();
    }

    public void DeselectAllItems()
    {
        foreach (var photo in Photos)
        {
            photo.IsSelected = false;
        }
        UpdateSelectedCount();
    }

    public void UpdateSelectedCount()
    {
        SelectedCount = Photos.Count(p => p.IsSelected);
    }

    public async Task DeleteSelectedAsync(DeleteMode mode)
    {
        var selected = Photos.Where(p => p.IsSelected).ToList();
        if (selected.Count == 0) return;

        var toDelete = new HashSet<string>();

        foreach (var photo in selected)
        {
            toDelete.Add(photo.FilePath);
            if (!string.IsNullOrEmpty(photo.PairedFilePath))
            {
                toDelete.Add(photo.PairedFilePath);
            }
        }

        var results = await _fileService.DeleteFilesAsync(toDelete.ToList(), mode == DeleteMode.RecycleBin);

        foreach (var deleted in results)
        {
            var item = Photos.FirstOrDefault(p => p.FilePath == deleted);
            if (item != null) Photos.Remove(item);

            item = _allPhotos.FirstOrDefault(p => p.FilePath == deleted);
            if (item != null) _allPhotos.Remove(item);
        }

        TotalCount = Photos.Count;
        SelectedCount = 0;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
