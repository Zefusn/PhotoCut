using Microsoft.UI.Xaml;
using System.Diagnostics;

namespace PhotoCut;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        try
        {
            InitializeComponent();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"App constructor error: {ex}");
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "photocut_error.txt"), ex.ToString());
            throw;
        }
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            _window = new MainWindow();
            _window.Activate();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"OnLaunched error: {ex}");
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "photocut_error.txt"), ex.ToString());
            throw;
        }
    }
}
