using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PhotoCut.Models;
using PhotoCut.Services;

namespace PhotoCut;

public sealed partial class PreviewWindow : Window
{
    private readonly PhotoItem _photo;
    private readonly ImageService _imageService;

    public PreviewWindow(PhotoItem photo, ImageService imageService)
    {
        _photo = photo;
        _imageService = imageService;

        InitializeComponent();
        
        // 设置窗口大小
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
        appWindow.Resize(new Windows.Graphics.SizeInt32(1000, 800));
        
        Title = photo.FileName;

        FileNameText.Text = photo.FileName;

        LoadImageAsync();
    }

    private async void LoadImageAsync()
    {
        try
        {
            var image = await _imageService.LoadFullImageAsync(_photo.FilePath, _photo.IsArw);
            if (image != null)
            {
                PreviewImage.Source = image;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Preview load error: {ex.Message}");
        }
    }

    private void OpenFileLocation_Click(object sender, RoutedEventArgs e)
    {
        var directory = System.IO.Path.GetDirectoryName(_photo.FilePath);
        if (directory != null)
        {
            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{_photo.FilePath}\"");
        }
    }
}
