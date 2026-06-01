using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using PhotoCut.Models;
using PhotoCut.Services;
using PhotoCut.ViewModels;

namespace PhotoCut;

public sealed partial class MainWindow : Window
{
    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    private const int SW_MAXIMIZE = 3;

    private readonly MainViewModel _viewModel = new();
    private readonly ImageService _imageService = new();

    // UI 控件引用
    private TextBlock _folderPathText = null!;
    private TextBlock _totalCountText = null!;
    private TextBlock _selectedCountText = null!;
    private Button _deleteButton = null!;
    private ProgressRing _loadingRing = null!;
    private GridView _photoGridView = null!;
    private TextBlock _emptyMessage = null!;
    private ComboBox _formatFilter = null!;
    private ComboBox _dateFilterBox = null!;
    private Image _previewImage = null!;
    private TextBlock _previewPlaceholder = null!;
    private StackPanel _previewInfoBar = null!;
    private TextBlock _previewFileName = null!;
    private TextBlock _previewFileSize = null!;
    private TextBlock _previewDate = null!;

    public MainWindow()
    {
        InitializeComponent();
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        ShowWindow(hwnd, SW_MAXIMIZE);
        Title = "PhotoCut - 照片批量删除工具";

        BuildUI();

        _viewModel.PropertyChanged += (s, e) =>
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (e.PropertyName == nameof(MainViewModel.SelectedPhoto))
                    _ = LoadPreviewAsync();
                UpdateUI();
            });
        };
        UpdateUI();
    }

    private void BuildUI()
    {
        // 根布局
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // === 顶部工具栏 ===
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Padding = new Thickness(12), Spacing = 12 };

        var selectBtn = new Button { Content = "选择文件夹", Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        selectBtn.Click += SelectFolder_Click;
        toolbar.Children.Add(selectBtn);

        _folderPathText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, MaxWidth = 400, TextTrimming = TextTrimming.CharacterEllipsis };
        toolbar.Children.Add(_folderPathText);

        _totalCountText = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        toolbar.Children.Add(_totalCountText);

        _selectedCountText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Foreground = new SolidColorBrush(Microsoft.UI.Colors.DodgerBlue) };
        toolbar.Children.Add(_selectedCountText);

        _deleteButton = new Button { Content = "删除选中", Foreground = new SolidColorBrush(Microsoft.UI.Colors.White), Background = new SolidColorBrush(Microsoft.UI.Colors.Red), Visibility = Visibility.Collapsed, Margin = new Thickness(24, 0, 0, 0) };
        _deleteButton.Click += Delete_Click;
        toolbar.Children.Add(_deleteButton);

        Grid.SetRow(toolbar, 0);
        root.Children.Add(toolbar);

        // === 主内容区 ===
        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(420) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // --- 左侧 ---
        var leftPanel = new Grid();
        leftPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        leftPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // 筛选栏
        var filterBar = new StackPanel { Orientation = Orientation.Horizontal, Padding = new Thickness(8), Spacing = 8 };

        _formatFilter = new ComboBox { Header = "格式", Width = 90 };
        _formatFilter.Items.Add(new ComboBoxItem { Content = "全部", IsSelected = true });
        _formatFilter.Items.Add(new ComboBoxItem { Content = "JPG" });
        _formatFilter.Items.Add(new ComboBoxItem { Content = "ARW" });
        _formatFilter.SelectionChanged += FormatFilter_Changed;
        filterBar.Children.Add(_formatFilter);

        _dateFilterBox = new ComboBox { Header = "日期", Width = 110 };
        _dateFilterBox.Items.Add(new ComboBoxItem { Content = "全部", IsSelected = true });
        _dateFilterBox.Items.Add(new ComboBoxItem { Content = "今天" });
        _dateFilterBox.Items.Add(new ComboBoxItem { Content = "昨天" });
        _dateFilterBox.Items.Add(new ComboBoxItem { Content = "最近7天" });
        _dateFilterBox.Items.Add(new ComboBoxItem { Content = "最近30天" });
        _dateFilterBox.SelectionChanged += DateFilter_Changed;
        filterBar.Children.Add(_dateFilterBox);

        var selectAllBtn = new Button { Content = "全选", VerticalAlignment = VerticalAlignment.Bottom };
        selectAllBtn.Click += SelectAll_Click;
        filterBar.Children.Add(selectAllBtn);

        var deselectBtn = new Button { Content = "取消", VerticalAlignment = VerticalAlignment.Bottom };
        deselectBtn.Click += DeselectAll_Click;
        filterBar.Children.Add(deselectBtn);

        Grid.SetRow(filterBar, 0);
        leftPanel.Children.Add(filterBar);

        // 照片列表区
        var listArea = new Grid();

        _loadingRing = new ProgressRing { IsActive = false, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Width = 48, Height = 48 };
        listArea.Children.Add(_loadingRing);

        _photoGridView = new GridView { SelectionMode = ListViewSelectionMode.None, IsItemClickEnabled = true, Padding = new Thickness(8) };
        _photoGridView.ItemClick += PhotoGrid_ItemClick;

        // 创建 DataTemplate
        var templateXaml = @"
            <DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>
                <StackPanel Width='120' Padding='4' Spacing='2'>
                    <Grid Height='80'>
                        <Image Stretch='UniformToFill' Source='{Binding Thumbnail}'/>
                        <Border HorizontalAlignment='Right' VerticalAlignment='Top' Margin='4'
                                Background='#AA000000' CornerRadius='3' Padding='2'>
                            <CheckBox IsChecked='{Binding IsSelected, Mode=TwoWay}'
                                      MinWidth='0' MinHeight='0' Padding='0'/>
                        </Border>
                        <Border Background='#99000000' VerticalAlignment='Bottom' Padding='3,1'>
                            <TextBlock Text='{Binding Extension}' FontSize='9' Foreground='White'/>
                        </Border>
                    </Grid>
                    <TextBlock Text='{Binding FileName}' FontSize='10' TextTrimming='CharacterEllipsis' HorizontalAlignment='Center'/>
                </StackPanel>
            </DataTemplate>";
        _photoGridView.ItemTemplate = (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(templateXaml);
        listArea.Children.Add(_photoGridView);

        _emptyMessage = new TextBlock
        {
            Text = "选择一个文件夹开始扫描",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"]
        };
        listArea.Children.Add(_emptyMessage);

        Grid.SetRow(listArea, 1);
        leftPanel.Children.Add(listArea);

        Grid.SetColumn(leftPanel, 0);
        content.Children.Add(leftPanel);

        // --- 右侧预览 ---
        var rightPanel = new Grid { Background = new SolidColorBrush(Microsoft.UI.Colors.Black) };
        rightPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        rightPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _previewImage = new Image { Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetRow(_previewImage, 0);
        rightPanel.Children.Add(_previewImage);

        _previewPlaceholder = new TextBlock
        {
            Text = "点击左侧照片预览",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(102, 255, 255, 255)),
            FontSize = 18
        };
        Grid.SetRow(_previewPlaceholder, 0);
        rightPanel.Children.Add(_previewPlaceholder);

        _previewInfoBar = new StackPanel { Orientation = Orientation.Horizontal, Padding = new Thickness(12), Spacing = 16, Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(204, 0, 0, 0)), Visibility = Visibility.Collapsed };
        _previewFileName = new TextBlock { Foreground = new SolidColorBrush(Microsoft.UI.Colors.White), VerticalAlignment = VerticalAlignment.Center };
        _previewInfoBar.Children.Add(_previewFileName);
        _previewFileSize = new TextBlock { Foreground = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(170, 255, 255, 255)), VerticalAlignment = VerticalAlignment.Center };
        _previewInfoBar.Children.Add(_previewFileSize);
        _previewDate = new TextBlock { Foreground = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(170, 255, 255, 255)), VerticalAlignment = VerticalAlignment.Center };
        _previewInfoBar.Children.Add(_previewDate);
        var openLocationBtn = new Button { Content = "打开文件位置", VerticalAlignment = VerticalAlignment.Center };
        openLocationBtn.Click += OpenFileLocation_Click;
        _previewInfoBar.Children.Add(openLocationBtn);
        Grid.SetRow(_previewInfoBar, 1);
        rightPanel.Children.Add(_previewInfoBar);

        Grid.SetColumn(rightPanel, 1);
        content.Children.Add(rightPanel);

        Grid.SetRow(content, 1);
        root.Children.Add(content);

        Content = root;
    }

    private void UpdateUI()
    {
        _folderPathText.Text = _viewModel.FolderPath;
        _totalCountText.Text = $"总数: {_viewModel.TotalCount}";
        _selectedCountText.Text = $"已选: {_viewModel.SelectedCount}";
        _deleteButton.Visibility = _viewModel.SelectedCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        _loadingRing.IsActive = _viewModel.IsLoading;
        _emptyMessage.Visibility = _viewModel.Photos.Count == 0 && !_viewModel.IsLoading ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void SelectFolder_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Windows.Storage.Pickers.FolderPicker();
        picker.FileTypeFilter.Add("*");
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        var folder = await picker.PickSingleFolderAsync();
        if (folder == null) return;

        _viewModel.FolderPath = folder.Path;
        await _viewModel.LoadPhotosAsync();
        _photoGridView.ItemsSource = _viewModel.Photos;
        UpdateUI();
        _ = LoadAllThumbnailsAsync();
    }

    private async Task LoadAllThumbnailsAsync()
    {
        var photos = _viewModel.Photos.ToList();
        var semaphore = new SemaphoreSlim(4);
        var tasks = photos.Select(async photo =>
        {
            if (photo.Thumbnail != null) return;
            await semaphore.WaitAsync();
            try
            {
                var thumbnail = await _imageService.LoadThumbnailAsync(photo.FilePath, photo.IsArw);
                photo.Thumbnail = thumbnail ?? await _imageService.CreatePlaceholderSource();
            }
            catch { try { photo.Thumbnail = await _imageService.CreatePlaceholderSource(); } catch { } }
            finally { semaphore.Release(); }
        });
        await Task.WhenAll(tasks);
    }

    private async Task LoadPreviewAsync()
    {
        var photo = _viewModel.SelectedPhoto;
        if (photo == null)
        {
            _previewImage.Source = null;
            _previewPlaceholder.Visibility = Visibility.Visible;
            _previewInfoBar.Visibility = Visibility.Collapsed;
            return;
        }

        _previewPlaceholder.Visibility = Visibility.Collapsed;
        _previewInfoBar.Visibility = Visibility.Visible;
        _previewFileName.Text = photo.FileName;
        _previewFileSize.Text = photo.SizeText;
        _previewDate.Text = photo.DateModified.ToString("yyyy-MM-dd HH:mm");

        try
        {
            var image = await _imageService.LoadFullImageAsync(photo.FilePath, photo.IsArw);
            if (image != null && _viewModel.SelectedPhoto == photo)
                _previewImage.Source = image;
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Preview error: {ex.Message}"); }
    }

    private void OpenFileLocation_Click(object sender, RoutedEventArgs e)
    {
        var photo = _viewModel.SelectedPhoto;
        if (photo == null) return;
        var dir = System.IO.Path.GetDirectoryName(photo.FilePath);
        if (dir != null)
            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{photo.FilePath}\"");
    }

    private void PhotoGrid_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is PhotoItem photo)
            _viewModel.SelectedPhoto = photo;
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.SelectAllItems();
        UpdateUI();
    }

    private void DeselectAll_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.DeselectAllItems();
        UpdateUI();
    }

    private void FormatFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_formatFilter.SelectedIndex == 0) _viewModel.SelectedFormat = FileFormat.All;
        else if (_formatFilter.SelectedIndex == 1) _viewModel.SelectedFormat = FileFormat.Jpg;
        else _viewModel.SelectedFormat = FileFormat.Arw;
        ApplyFiltersAndReload();
    }

    private void DateFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        _viewModel.SelectedDateFilter = _dateFilterBox.SelectedIndex switch
        {
            0 => Models.DateFilter.All,
            1 => Models.DateFilter.Today,
            2 => Models.DateFilter.Yesterday,
            3 => Models.DateFilter.Last7Days,
            4 => Models.DateFilter.Last30Days,
            _ => Models.DateFilter.All
        };
        ApplyFiltersAndReload();
    }

    private void ApplyFiltersAndReload()
    {
        _viewModel.ApplyFilters();
        UpdateUI();
        _ = LoadAllThumbnailsAsync();
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        var selected = _viewModel.Photos.Where(p => p.IsSelected).ToList();
        if (selected.Count == 0) return;

        var dialog = new ContentDialog
        {
            Title = "确认删除",
            Content = $"确定要删除选中的 {selected.Count} 张照片吗？\n同名的 JPG/ARW 文件将同步删除。",
            PrimaryButtonText = "删除",
            SecondaryButtonText = "取消",
            DefaultButton = ContentDialogButton.Secondary
        };
        dialog.XamlRoot = ((Grid)Content).XamlRoot;
        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary) return;

        _loadingRing.IsActive = true;
        try
        {
            await _viewModel.DeleteSelectedAsync(DeleteMode.RecycleBin);
            _photoGridView.ItemsSource = _viewModel.Photos;
            UpdateUI();
        }
        finally { _loadingRing.IsActive = false; }
    }
}
