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
        var toolbarBg = new Border
        {
            Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 243, 243, 243)),
            Padding = new Thickness(16, 10, 16, 10),
            BorderBrush = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 220, 220, 220)),
            BorderThickness = new Thickness(0, 0, 0, 1)
        };
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };

        var selectBtn = new Button { Content = "📁 选择文件夹", Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        selectBtn.Click += SelectFolder_Click;
        toolbar.Children.Add(selectBtn);

        _folderPathText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, MaxWidth = 400, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 100, 100, 100)) };
        toolbar.Children.Add(_folderPathText);

        var separator1 = new TextBlock { Text = "│", Foreground = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(60, 0, 0, 0)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) };
        toolbar.Children.Add(separator1);

        _totalCountText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Foreground = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(180, 0, 0, 0)) };
        toolbar.Children.Add(_totalCountText);

        _selectedCountText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Foreground = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 0, 120, 215)) };
        toolbar.Children.Add(_selectedCountText);

        _deleteButton = new Button { Content = "🗑 删除选中", Foreground = new SolidColorBrush(Microsoft.UI.Colors.White), Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 220, 53, 69)), Visibility = Visibility.Collapsed, Margin = new Thickness(16, 0, 0, 0) };
        _deleteButton.Click += Delete_Click;
        toolbar.Children.Add(_deleteButton);

        toolbarBg.Child = toolbar;
        Grid.SetRow(toolbarBg, 0);
        root.Children.Add(toolbarBg);

        // === 主内容区 ===
        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(440) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // --- 左侧面板 ---
        var leftPanel = new Grid { Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 249, 249, 249)) };
        leftPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        leftPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // 筛选栏
        var filterBg = new Border
        {
            Background = new SolidColorBrush(Microsoft.UI.Colors.White),
            Padding = new Thickness(12, 8, 12, 8),
            BorderBrush = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 230, 230, 230)),
            BorderThickness = new Thickness(0, 0, 0, 1)
        };
        var filterBar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };

        _formatFilter = new ComboBox { Width = 90, HorizontalAlignment = HorizontalAlignment.Left };
        _formatFilter.Items.Add(new ComboBoxItem { Content = "全部", IsSelected = true });
        _formatFilter.Items.Add(new ComboBoxItem { Content = "JPG" });
        _formatFilter.Items.Add(new ComboBoxItem { Content = "ARW" });
        _formatFilter.SelectionChanged += FormatFilter_Changed;
        var formatLabel = new StackPanel { Spacing = 4 };
        formatLabel.Children.Add(new TextBlock { Text = "格式", FontSize = 11, Foreground = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(180, 0, 0, 0)) });
        formatLabel.Children.Add(_formatFilter);
        filterBar.Children.Add(formatLabel);

        _dateFilterBox = new ComboBox { Width = 110, HorizontalAlignment = HorizontalAlignment.Left };
        _dateFilterBox.Items.Add(new ComboBoxItem { Content = "全部", IsSelected = true });
        _dateFilterBox.Items.Add(new ComboBoxItem { Content = "今天" });
        _dateFilterBox.Items.Add(new ComboBoxItem { Content = "昨天" });
        _dateFilterBox.Items.Add(new ComboBoxItem { Content = "最近7天" });
        _dateFilterBox.Items.Add(new ComboBoxItem { Content = "最近30天" });
        _dateFilterBox.SelectionChanged += DateFilter_Changed;
        var dateLabel = new StackPanel { Spacing = 4 };
        dateLabel.Children.Add(new TextBlock { Text = "日期", FontSize = 11, Foreground = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(180, 0, 0, 0)) });
        dateLabel.Children.Add(_dateFilterBox);
        filterBar.Children.Add(dateLabel);

        var btnPanel = new StackPanel { Spacing = 4 };
        btnPanel.Children.Add(new TextBlock { Text = "操作", FontSize = 11, Foreground = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(180, 0, 0, 0)) });
        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var selectAllBtn = new Button { Content = "全选", Padding = new Thickness(8, 4, 8, 4) };
        selectAllBtn.Click += SelectAll_Click;
        btnRow.Children.Add(selectAllBtn);
        var deselectBtn = new Button { Content = "取消", Padding = new Thickness(8, 4, 8, 4) };
        deselectBtn.Click += DeselectAll_Click;
        btnRow.Children.Add(deselectBtn);
        btnPanel.Children.Add(btnRow);
        filterBar.Children.Add(btnPanel);

        filterBg.Child = filterBar;
        Grid.SetRow(filterBg, 0);
        leftPanel.Children.Add(filterBg);

        // 照片列表区
        var listArea = new Grid();

        _loadingRing = new ProgressRing { IsActive = false, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Width = 48, Height = 48 };
        listArea.Children.Add(_loadingRing);

        _photoGridView = new GridView { SelectionMode = ListViewSelectionMode.None, IsItemClickEnabled = true, Padding = new Thickness(12) };
        _photoGridView.ItemClick += PhotoGrid_ItemClick;

        var templateXaml = @"
            <DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>
                <StackPanel Width='130' Padding='4' Spacing='3'>
                    <Grid Height='85'>
                        <Image Stretch='UniformToFill' Source='{Binding Thumbnail}'/>
                        <Border HorizontalAlignment='Right' VerticalAlignment='Top' Margin='5'
                                Background='#BB000000' CornerRadius='4' Padding='3'>
                            <CheckBox IsChecked='{Binding IsSelected, Mode=TwoWay}'
                                      MinWidth='0' MinHeight='0' Padding='0'/>
                        </Border>
                        <Border Background='#AA000000' VerticalAlignment='Bottom' Padding='4,2'
                                CornerRadius='0,0,0,2'>
                            <TextBlock Text='{Binding Extension}' FontSize='9' Foreground='White'/>
                        </Border>
                    </Grid>
                    <TextBlock Text='{Binding FileName}' FontSize='10.5'
                               TextTrimming='CharacterEllipsis' HorizontalAlignment='Center'
                               Foreground='#333333'/>
                    <TextBlock Text='{Binding SizeText}' FontSize='9'
                               HorizontalAlignment='Center'
                               Foreground='#999999'/>
                </StackPanel>
            </DataTemplate>";
        _photoGridView.ItemTemplate = (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(templateXaml);
        listArea.Children.Add(_photoGridView);

        _emptyMessage = new TextBlock
        {
            Text = "选择一个文件夹开始扫描",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(120, 0, 0, 0)),
            FontSize = 16
        };
        listArea.Children.Add(_emptyMessage);

        Grid.SetRow(listArea, 1);
        leftPanel.Children.Add(listArea);

        Grid.SetColumn(leftPanel, 0);
        content.Children.Add(leftPanel);

        // 分隔线
        var separator = new Border
        {
            Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 210, 210, 210)),
            Width = 1
        };
        Grid.SetColumn(separator, 0);
        Grid.SetColumnSpan(separator, 2);
        separator.HorizontalAlignment = HorizontalAlignment.Right;
        content.Children.Add(separator);

        // --- 右侧预览区 ---
        var rightPanel = new Grid { Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 30, 30, 30)) };
        rightPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        rightPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // 预览图片容器（加 padding）
        var previewContainer = new Border
        {
            Padding = new Thickness(16, 12, 16, 8),
            Child = (_previewImage = new Image
            {
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            })
        };
        Grid.SetRow(previewContainer, 0);
        rightPanel.Children.Add(previewContainer);

        _previewPlaceholder = new TextBlock
        {
            Text = "🖼 点击左侧照片预览",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(80, 255, 255, 255)),
            FontSize = 18
        };
        Grid.SetRow(_previewPlaceholder, 0);
        rightPanel.Children.Add(_previewPlaceholder);

        // 预览信息栏
        _previewInfoBar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Padding = new Thickness(16, 10, 16, 10),
            Spacing = 20,
            Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(230, 20, 20, 20)),
            Visibility = Visibility.Collapsed
        };
        _previewFileName = new TextBlock { Foreground = new SolidColorBrush(Microsoft.UI.Colors.White), VerticalAlignment = VerticalAlignment.Center, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
        _previewInfoBar.Children.Add(_previewFileName);
        _previewFileSize = new TextBlock { Foreground = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(160, 255, 255, 255)), VerticalAlignment = VerticalAlignment.Center };
        _previewInfoBar.Children.Add(_previewFileSize);
        _previewDate = new TextBlock { Foreground = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(160, 255, 255, 255)), VerticalAlignment = VerticalAlignment.Center };
        _previewInfoBar.Children.Add(_previewDate);

        var spacer = new Border { HorizontalAlignment = HorizontalAlignment.Stretch };
        _previewInfoBar.Children.Add(spacer);

        var openLocationBtn = new Button
        {
            Content = "📂 打开位置",
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(10, 5, 10, 5)
        };
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
