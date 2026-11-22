using System.Windows;

namespace FluxFlow.Studio.Desktop;

public partial class MainWindow : Window, IAsyncDisposable
{
    public MainWindow(IServiceProvider services)
    {
        InitializeComponent();
        Studio.Services = services;
        Studio.BlazorWebViewInitializing += (_, args) => args.UserDataFolder =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FluxFlow", "Studio", "WebView2");
    }

    public ValueTask DisposeAsync() => Studio.DisposeAsync();
}
