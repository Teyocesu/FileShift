using System.Windows;

namespace FileShift.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var paths = e.Args.Length > 0 && e.Args[0] == "--" ? e.Args[1..] : e.Args;
        MainWindow = new MainWindow(paths);
        MainWindow.Show();
    }
}
