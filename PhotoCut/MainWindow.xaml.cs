using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using PhotoCut.Models;
using PhotoCut.Services;
using PhotoCut.ViewModels;
using Windows.Foundation;

namespace PhotoCut;

public sealed partial class MainWindow : Window
{
    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    private const int SW_MAXIMIZE = 3;

    private readonly MainViewModel _viewModel = new();
    private readonly ImageService _imageService = new();

    // UI 控件
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

    // 框选相关
    private Canvas _selectionCanvas = null!;
    private Rectangle _selectionRect = null!;
    private bool _isSelecting;
    private Point _selectStart;
    private List<PhotoItem> _lastAreaSelected = new();
    private Button _enterLocalBtn = null!;
    private Button _exitLocalBtn = null!;
    private Button _deleteUnretainedBtn = null!;
    private Border _localBanner = null!;
    private TextBlock _localBannerText = null!;

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
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // === 工具栏 ===
        var toolbarBg = new Border
        {
            Background = new SolidColorBrush(ColorHelper.FromArgb(255, 243, 243, 243)),
            Padding = new Thickness(16, 10, 16, 10),
            BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(255, 220, 220, 220)),
            BorderThickness = new Thickness(0, 0, 0, 1)
        };
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };

        var selectBtn = new Button { Content = "📁 选择文件夹", Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        selectBtn.Click += SelectFolder_Click;
        toolbar.Children.Add(selectBtn);

        _folderPathText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, MaxWidth = 400, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 100, 100, 100)) };
        toolbar.Children.Add(_folderPathText);
        toolbar.Children.Add(new TextBlock { Text = "│", Foreground = new SolidColorBrush(ColorHelper.FromArgb(60, 0, 0, 0)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) });
        _totalCountText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Foreground = new SolidColorBrush(ColorHelper.FromArgb(180, 0, 0, 0)) };
        toolbar.Children.Add(_totalCountText);
        _selectedCountText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 0, 120, 215)) };
        toolbar.Children.Add(_selectedCountText);

        _deleteButton = new Button { Content = "🗑 删除选中", Foreground = new SolidColorBrush(Colors.White), Background = new SolidColorBrush(ColorHelper.FromArgb(255, 220, 53, 69)), Visibility = Visibility.Collapsed, Margin = new Thickness(16, 0, 0, 0) };
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
        var leftPanel = new Grid { Background = new SolidColorBrush(ColorHelper.FromArgb(255, 249, 249, 249)) };
        leftPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 横幅
        leftPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 筛选栏
        leftPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 列表

        // 局部视图横幅
        _localBanner = new Border
        {
            Background = new SolidColorBrush(ColorHelper.FromArgb(255, 0, 120, 215)),
            Padding = new Thickness(12, 8, 12, 8),
            Visibility = Visibility.Collapsed
        };
        var localBannerGrid = new Grid();
        localBannerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        localBannerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        localBannerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _localBannerText = new TextBlock { Foreground = new SolidColorBrush(Colors.White), VerticalAlignment = VerticalAlignment.Center, Text = "局部筛选模式" };
        Grid.SetColumn(_localBannerText, 0);
        localBannerGrid.Children.Add(_localBannerText);
        _deleteUnretainedBtn = new Button { Content = "🗑 删除未保留项", Foreground = new SolidColorBrush(Colors.White), Background = new SolidColorBrush(ColorHelper.FromArgb(255, 220, 53, 69)), Padding = new Thickness(10, 4, 10, 4), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) };
        _deleteUnretainedBtn.Click += DeleteUnretained_Click;
        Grid.SetColumn(_deleteUnretainedBtn, 1);
        localBannerGrid.Children.Add(_deleteUnretainedBtn);
        _exitLocalBtn = new Button { Content = "↩ 返回全局", Padding = new Thickness(10, 4, 10, 4), VerticalAlignment = VerticalAlignment.Center };
        _exitLocalBtn.Click += ExitLocal_Click;
        Grid.SetColumn(_exitLocalBtn, 2);
        localBannerGrid.Children.Add(_exitLocalBtn);
        _localBanner.Child = localBannerGrid;
        Grid.SetRow(_localBanner, 0);
        leftPanel.Children.Add(_localBanner);

        // 筛选栏
        var filterBg = new Border
        {
            Background = new SolidColorBrush(Colors.White),
            Padding = new Thickness(10, 6, 10, 6),
            BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(255, 230, 230, 230)),
            BorderThickness = new Thickness(0, 0, 0, 1)
        };
        var filterBar = new StackPanel { Spacing = 6 };

        // 第一行：格式 + 日期
        var filterRow1 = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        _formatFilter = new ComboBox { Width = 80 };
        _formatFilter.Items.Add(new ComboBoxItem { Content = "全部", IsSelected = true });
        _formatFilter.Items.Add(new ComboBoxItem { Content = "JPG" });
        _formatFilter.Items.Add(new ComboBoxItem { Content = "ARW" });
        _formatFilter.SelectionChanged += FormatFilter_Changed;
        var fmtLabel = new StackPanel { Spacing = 2 };
        fmtLabel.Children.Add(new TextBlock { Text = "格式", FontSize = 10, Foreground = new SolidColorBrush(ColorHelper.FromArgb(150, 0, 0, 0)) });
        fmtLabel.Children.Add(_formatFilter);
        filterRow1.Children.Add(fmtLabel);

        _dateFilterBox = new ComboBox { Width = 100 };
        _dateFilterBox.Items.Add(new ComboBoxItem { Content = "全部", IsSelected = true });
        _dateFilterBox.Items.Add(new ComboBoxItem { Content = "今天" });
        _dateFilterBox.Items.Add(new ComboBoxItem { Content = "昨天" });
        _dateFilterBox.Items.Add(new ComboBoxItem { Content = "最近7天" });
        _dateFilterBox.Items.Add(new ComboBoxItem { Content = "最近30天" });
        _dateFilterBox.SelectionChanged += DateFilter_Changed;
        var dtLabel = new StackPanel { Spacing = 2 };
        dtLabel.Children.Add(new TextBlock { Text = "日期", FontSize = 10, Foreground = new SolidColorBrush(ColorHelper.FromArgb(150, 0, 0, 0)) });
        dtLabel.Children.Add(_dateFilterBox);
        filterRow1.Children.Add(dtLabel);
        filterBar.Children.Add(filterRow1);

        // 第二行：操作按钮
        var filterRow2 = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var selAllBtn = new Button { Content = "全选", Padding = new Thickness(8, 3, 8, 3), FontSize = 12 };
        selAllBtn.Click += SelectAll_Click;
        filterRow2.Children.Add(selAllBtn);
        var deselBtn = new Button { Content = "取消", Padding = new Thickness(8, 3, 8, 3), FontSize = 12 };
        deselBtn.Click += DeselectAll_Click;
        filterRow2.Children.Add(deselBtn);
        _enterLocalBtn = new Button { Content = "🔍 框选筛选", Padding = new Thickness(8, 3, 8, 3), FontSize = 12, Visibility = Visibility.Collapsed };
        _enterLocalBtn.Click += EnterLocal_Click;
        filterRow2.Children.Add(_enterLocalBtn);
        filterBar.Children.Add(filterRow2);

        filterBg.Child = filterBar;
        Grid.SetRow(filterBg, 1);
        leftPanel.Children.Add(filterBg);

        // 照片列表区（含框选 Canvas）
        var listArea = new Grid();
        Grid.SetRow(listArea, 2);

        _loadingRing = new ProgressRing { IsActive = false, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Width = 48, Height = 48 };
        listArea.Children.Add(_loadingRing);

        _photoGridView = new GridView { SelectionMode = ListViewSelectionMode.None, IsItemClickEnabled = true, Padding = new Thickness(12) };
        _photoGridView.ItemClick += PhotoGrid_ItemClick;

        var templateXaml = @"<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
            <Border x:Name='ItemBorder' BorderThickness='2' BorderBrush='Transparent' CornerRadius='4' Padding='1'>
                <StackPanel Width='88' Padding='2' Spacing='2'>
                    <Grid Height='65'>
                        <Image Stretch='UniformToFill' Source='{Binding Thumbnail}'/>
                        <Border HorizontalAlignment='Right' VerticalAlignment='Top' Margin='2' Background='#BB000000' CornerRadius='3' Padding='2'>
                            <CheckBox IsChecked='{Binding IsSelected, Mode=TwoWay}' MinWidth='0' MinHeight='0' Padding='0'/>
                        </Border>
                        <Border Background='#AA000000' VerticalAlignment='Bottom' Padding='3,1' CornerRadius='0,0,0,2'>
                            <TextBlock Text='{Binding Extension}' FontSize='8' Foreground='White'/>
                        </Border>
                    </Grid>
                    <TextBlock Text='{Binding FileName}' FontSize='9' TextTrimming='CharacterEllipsis' HorizontalAlignment='Center' Foreground='#333333'/>
                </StackPanel>
            </Border>
        </DataTemplate>";
        _photoGridView.ItemTemplate = (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(templateXaml);
        listArea.Children.Add(_photoGridView);

        // 框选 Canvas（不拦截事件，仅显示矩形）
        _selectionCanvas = new Canvas { IsHitTestVisible = false };
        _selectionRect = new Rectangle
        {
            Fill = new SolidColorBrush(ColorHelper.FromArgb(40, 0, 120, 215)),
            Stroke = new SolidColorBrush(ColorHelper.FromArgb(180, 0, 120, 215)),
            StrokeThickness = 1.5,
            Visibility = Visibility.Collapsed
        };
        _selectionCanvas.Children.Add(_selectionRect);
        listArea.Children.Add(_selectionCanvas);

        // 框选事件直接绑定到 GridView
        _photoGridView.PointerPressed += GridView_PointerPressed;
        _photoGridView.PointerMoved += GridView_PointerMoved;
        _photoGridView.PointerReleased += GridView_PointerReleased;

        _emptyMessage = new TextBlock
        {
            Text = "选择一个文件夹开始扫描\n💡 拖动鼠标框选照片区域",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(ColorHelper.FromArgb(120, 0, 0, 0)),
            FontSize = 16,
            TextAlignment = Microsoft.UI.Xaml.TextAlignment.Center
        };
        listArea.Children.Add(_emptyMessage);

        leftPanel.Children.Add(listArea);
        Grid.SetColumn(leftPanel, 0);
        content.Children.Add(leftPanel);

        // 分隔线
        var sep = new Border { Background = new SolidColorBrush(ColorHelper.FromArgb(255, 210, 210, 210)), Width = 1, HorizontalAlignment = HorizontalAlignment.Right };
        Grid.SetColumn(sep, 0);
        Grid.SetColumnSpan(sep, 2);
        content.Children.Add(sep);

        // --- 右侧预览 ---
        var rightPanel = new Grid { Background = new SolidColorBrush(ColorHelper.FromArgb(255, 30, 30, 30)) };
        rightPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        rightPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var previewContainer = new Border
        {
            Padding = new Thickness(16, 12, 16, 8),
            Child = (_previewImage = new Image { Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch })
        };
        Grid.SetRow(previewContainer, 0);
        rightPanel.Children.Add(previewContainer);

        _previewPlaceholder = new TextBlock { Text = "🖼 点击左侧照片预览", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = new SolidColorBrush(ColorHelper.FromArgb(80, 255, 255, 255)), FontSize = 18 };
        Grid.SetRow(_previewPlaceholder, 0);
        rightPanel.Children.Add(_previewPlaceholder);

        _previewInfoBar = new StackPanel { Orientation = Orientation.Horizontal, Padding = new Thickness(16, 10, 16, 10), Spacing = 20, Background = new SolidColorBrush(ColorHelper.FromArgb(230, 20, 20, 20)), Visibility = Visibility.Collapsed };
        _previewFileName = new TextBlock { Foreground = new SolidColorBrush(Colors.White), VerticalAlignment = VerticalAlignment.Center, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
        _previewInfoBar.Children.Add(_previewFileName);
        _previewFileSize = new TextBlock { Foreground = new SolidColorBrush(ColorHelper.FromArgb(160, 255, 255, 255)), VerticalAlignment = VerticalAlignment.Center };
        _previewInfoBar.Children.Add(_previewFileSize);
        _previewDate = new TextBlock { Foreground = new SolidColorBrush(ColorHelper.FromArgb(160, 255, 255, 255)), VerticalAlignment = VerticalAlignment.Center };
        _previewInfoBar.Children.Add(_previewDate);
        _previewInfoBar.Children.Add(new Border { HorizontalAlignment = HorizontalAlignment.Stretch });
        var openBtn = new Button { Content = "📂 打开位置", VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(10, 5, 10, 5) };
        openBtn.Click += OpenFileLocation_Click;
        _previewInfoBar.Children.Add(openBtn);
        Grid.SetRow(_previewInfoBar, 1);
        rightPanel.Children.Add(_previewInfoBar);

        Grid.SetColumn(rightPanel, 1);
        content.Children.Add(rightPanel);

        Grid.SetRow(content, 1);
        root.Children.Add(content);
        Content = root;
    }

    // === 框选逻辑 ===

    private void GridView_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_viewModel.IsInLocalView) return;
        var properties = e.GetCurrentPoint(_photoGridView).Properties;
        if (properties.IsLeftButtonPressed)
        {
            _isSelecting = false; // 不立即开始选区，等拖动超过阈值
            _selectStart = e.GetCurrentPoint(_photoGridView).Position;
            // 清除之前的高亮
            ClearAreaHighlights();
        }
    }

    private void GridView_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_viewModel.IsInLocalView) return;
        if (!e.GetCurrentPoint(_photoGridView).Properties.IsLeftButtonPressed)
        {
            _isSelecting = false;
            return;
        }

        var pos = e.GetCurrentPoint(_photoGridView).Position;
        var dx = Math.Abs(pos.X - _selectStart.X);
        var dy = Math.Abs(pos.Y - _selectStart.Y);

        if (!_isSelecting && (dx > 8 || dy > 8))
        {
            // 超过阈值，开始框选
            _isSelecting = true;
            _selectionRect.Visibility = Visibility.Visible;
            _photoGridView.IsItemClickEnabled = false; // 禁用点击，避免拖动时触发
        }

        if (_isSelecting)
        {
            var x = Math.Min(_selectStart.X, pos.X);
            var y = Math.Min(_selectStart.Y, pos.Y);
            Canvas.SetLeft(_selectionRect, x);
            Canvas.SetTop(_selectionRect, y);
            _selectionRect.Width = Math.Abs(pos.X - _selectStart.X);
            _selectionRect.Height = Math.Abs(pos.Y - _selectStart.Y);
        }
    }

    private void GridView_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_viewModel.IsInLocalView)
        {
            _isSelecting = false;
            return;
        }

        if (_isSelecting)
        {
            _isSelecting = false;
            _selectionRect.Visibility = Visibility.Collapsed;
            _photoGridView.IsItemClickEnabled = true;

            var pos = e.GetCurrentPoint(_photoGridView).Position;
            var selRect = new Rect(
                Math.Min(_selectStart.X, pos.X),
                Math.Min(_selectStart.Y, pos.Y),
                Math.Abs(pos.X - _selectStart.X),
                Math.Abs(pos.Y - _selectStart.Y));

            if (selRect.Width < 10 || selRect.Height < 10) return;

            // 找出框选范围内的照片
            _lastAreaSelected.Clear();

            for (int i = 0; i < _photoGridView.Items.Count; i++)
            {
                var container = _photoGridView.ContainerFromIndex(i) as GridViewItem;
                if (container == null) continue;

                try
                {
                    var transform = container.TransformToVisual(_photoGridView);
                    var itemPos = transform.TransformPoint(new Point(0, 0));
                    var itemRect = new Rect(itemPos.X, itemPos.Y, container.ActualWidth, container.ActualHeight);

                    if (RectsIntersect(selRect, itemRect))
                    {
                        var photo = _photoGridView.Items[i] as PhotoItem;
                        if (photo != null) _lastAreaSelected.Add(photo);
                    }
                }
                catch { }
            }

            if (_lastAreaSelected.Count > 0)
            {
                // 高亮框选的照片
                foreach (var photo in _lastAreaSelected) photo.IsAreaSelected = true;
                ApplyAreaHighlights();
                _enterLocalBtn.Visibility = Visibility.Visible;
                _enterLocalBtn.Content = $"🔍 进入局部筛选 ({_lastAreaSelected.Count})";
            }
        }
        else
        {
            // 普通点击，恢复点击功能
            _photoGridView.IsItemClickEnabled = true;
        }
    }

    private static bool RectsIntersect(Rect a, Rect b)
    {
        return a.Left < b.Right && a.Right > b.Left && a.Top < b.Bottom && a.Bottom > b.Top;
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject parent)
    {
        for (int i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is ScrollViewer sv) return sv;
            var result = FindScrollViewer(child);
            if (result != null) return result;
        }
        return null;
    }

    private void ClearAreaHighlights()
    {
        foreach (var photo in _viewModel.Photos)
        {
            if (photo.IsAreaSelected) photo.IsAreaSelected = false;
        }
        // 清除容器上的高亮边框
        for (int i = 0; i < _photoGridView.Items.Count; i++)
        {
            var container = _photoGridView.ContainerFromIndex(i) as GridViewItem;
            if (container == null) continue;
            var border = FindChildBorder(container);
            if (border != null) border.BorderBrush = new SolidColorBrush(Colors.Transparent);
        }
    }

    private void ApplyAreaHighlights()
    {
        for (int i = 0; i < _photoGridView.Items.Count; i++)
        {
            var container = _photoGridView.ContainerFromIndex(i) as GridViewItem;
            if (container == null) continue;
            var photo = _photoGridView.Items[i] as PhotoItem;
            var border = FindChildBorder(container);
            if (border != null && photo != null)
            {
                border.BorderBrush = photo.IsAreaSelected
                    ? new SolidColorBrush(ColorHelper.FromArgb(255, 0, 120, 215))
                    : new SolidColorBrush(Colors.Transparent);
            }
        }
    }

    private static Border? FindChildBorder(DependencyObject parent)
    {
        for (int i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is Border b && b.Name == "ItemBorder") return b;
            if (child is Border b2 && Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(b2) > 0)
            {
                var inner = FindChildBorder(b2);
                if (inner != null) return inner;
            }
            var result = FindChildBorder(child);
            if (result != null) return result;
        }
        return null;
    }

    // === 局部视图 ===

    private void EnterLocal_Click(object sender, RoutedEventArgs e)
    {
        if (_lastAreaSelected.Count == 0) return;
        _viewModel.EnterLocalView(_lastAreaSelected.ToList());
        _photoGridView.ItemsSource = _viewModel.Photos;
        _localBanner.Visibility = Visibility.Visible;
        _localBannerText.Text = $"已选 {_lastAreaSelected.Count} 张 → 勾选保留项";
        _enterLocalBtn.Visibility = Visibility.Collapsed;
        _formatFilter.IsEnabled = false;
        _dateFilterBox.IsEnabled = false;
        UpdateUI();
    }

    private void ExitLocal_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.ExitLocalView();
        _photoGridView.ItemsSource = _viewModel.Photos;
        _localBanner.Visibility = Visibility.Collapsed;
        _enterLocalBtn.Visibility = Visibility.Collapsed;
        _formatFilter.IsEnabled = true;
        _dateFilterBox.IsEnabled = true;
        UpdateUI();
        _ = LoadAllThumbnailsAsync();
    }

    private async void DeleteUnretained_Click(object sender, RoutedEventArgs e)
    {
        var unretained = _viewModel.Photos.Where(p => !p.IsSelected).ToList();
        if (unretained.Count == 0)
        {
            var infoDialog = new ContentDialog { Title = "提示", Content = "所有照片都已勾选保留，无需删除。", CloseButtonText = "确定" };
            infoDialog.XamlRoot = ((Grid)Content).XamlRoot;
            await infoDialog.ShowAsync();
            return;
        }

        var dialog = new ContentDialog
        {
            Title = "确认删除",
            Content = $"将删除 {unretained.Count} 张未保留的照片（已勾选 {unretained.Count(p => !p.IsSelected)} 张保留）。\n同名 JPG/ARW 将同步删除。",
            PrimaryButtonText = "删除",
            SecondaryButtonText = "取消",
            DefaultButton = ContentDialogButton.Secondary
        };
        dialog.XamlRoot = ((Grid)Content).XamlRoot;
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        _loadingRing.IsActive = true;
        try
        {
            await _viewModel.DeleteUnretainedAsync();
            _photoGridView.ItemsSource = _viewModel.Photos;
            _localBannerText.Text = $"局部筛选模式 — 剩余 {_viewModel.Photos.Count} 张照片";
            UpdateUI();

            if (_viewModel.Photos.Count == 0)
            {
                ExitLocal_Click(sender, e);
            }
        }
        finally { _loadingRing.IsActive = false; }
    }

    // === 通用 UI ===

    private void UpdateUI()
    {
        _folderPathText.Text = _viewModel.FolderPath;
        _totalCountText.Text = _viewModel.IsInLocalView ? $"局部: {_viewModel.TotalCount}" : $"总数: {_viewModel.TotalCount}";
        _selectedCountText.Text = $"已选: {_viewModel.SelectedCount}";
        _deleteButton.Visibility = _viewModel.SelectedCount > 0 && !_viewModel.IsInLocalView ? Visibility.Visible : Visibility.Collapsed;
        _loadingRing.IsActive = _viewModel.IsLoading;
        _emptyMessage.Visibility = _viewModel.Photos.Count == 0 && !_viewModel.IsLoading ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void SelectFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsInLocalView) ExitLocal_Click(sender, e);

        var picker = new Windows.Storage.Pickers.FolderPicker();
        picker.FileTypeFilter.Add("*");
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        var folder = await picker.PickSingleFolderAsync();
        if (folder == null) return;

        _viewModel.FolderPath = folder.Path;
        await _viewModel.LoadPhotosAsync();
        _photoGridView.ItemsSource = _viewModel.Photos;
        _enterLocalBtn.Visibility = Visibility.Collapsed;
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
            if (image != null && _viewModel.SelectedPhoto == photo) _previewImage.Source = image;
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Preview error: {ex.Message}"); }
    }

    private void OpenFileLocation_Click(object sender, RoutedEventArgs e)
    {
        var photo = _viewModel.SelectedPhoto;
        if (photo == null) return;
        var dir = System.IO.Path.GetDirectoryName(photo.FilePath);
        if (dir != null) System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{photo.FilePath}\"");
    }

    private void PhotoGrid_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is PhotoItem photo) _viewModel.SelectedPhoto = photo;
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e) { _viewModel.SelectAllItems(); UpdateUI(); }
    private void DeselectAll_Click(object sender, RoutedEventArgs e) { _viewModel.DeselectAllItems(); UpdateUI(); }

    private void FormatFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_formatFilter.SelectedIndex == 0) _viewModel.SelectedFormat = FileFormat.All;
        else if (_formatFilter.SelectedIndex == 1) _viewModel.SelectedFormat = FileFormat.Jpg;
        else _viewModel.SelectedFormat = FileFormat.Arw;
        _viewModel.ApplyFilters(); UpdateUI(); _ = LoadAllThumbnailsAsync();
    }

    private void DateFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        _viewModel.SelectedDateFilter = _dateFilterBox.SelectedIndex switch
        {
            0 => Models.DateFilter.All, 1 => Models.DateFilter.Today, 2 => Models.DateFilter.Yesterday,
            3 => Models.DateFilter.Last7Days, 4 => Models.DateFilter.Last30Days, _ => Models.DateFilter.All
        };
        _viewModel.ApplyFilters(); UpdateUI(); _ = LoadAllThumbnailsAsync();
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        var selected = _viewModel.Photos.Where(p => p.IsSelected).ToList();
        if (selected.Count == 0) return;
        var dialog = new ContentDialog { Title = "确认删除", Content = $"确定要删除选中的 {selected.Count} 张照片吗？\n同名的 JPG/ARW 文件将同步删除。", PrimaryButtonText = "删除", SecondaryButtonText = "取消", DefaultButton = ContentDialogButton.Secondary };
        dialog.XamlRoot = ((Grid)Content).XamlRoot;
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        _loadingRing.IsActive = true;
        try { await _viewModel.DeleteSelectedAsync(DeleteMode.RecycleBin); _photoGridView.ItemsSource = _viewModel.Photos; UpdateUI(); }
        finally { _loadingRing.IsActive = false; }
    }
}
