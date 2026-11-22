using System.ComponentModel;
using System.Windows;
using Microsoft.Extensions.Hosting;

namespace FluxFlow.Studio.Desktop;

public partial class App : System.Windows.Application
{
    private IHost? _host;
    private bool _closing;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            var builder = Host.CreateApplicationBuilder(e.Args);
            builder.Services.AddDesktopStudio();
            _host = builder.Build();
            await _host.StartAsync();
            var window = new MainWindow(_host.Services);
            MainWindow = window;
            window.Closing += CloseStudio;
            window.Show();
        }
        catch (Exception error)
        {
            try { await StopHostAsync(); }
            catch (Exception cleanupError) { error = new AggregateException(error, cleanupError); }
            MessageBox.Show(error.ToString(), "Studio could not start", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private async void CloseStudio(object? sender, CancelEventArgs e)
    {
        e.Cancel = true;
        if (_closing) return;
        _closing = true;
        List<Exception> errors = [];
        try
        {
            if (MainWindow is MainWindow window)
            {
                window.IsEnabled = false;
                await window.DisposeAsync();
            }
        }
        catch (Exception error) { errors.Add(error); }
        try { await StopHostAsync(); }
        catch (Exception error) { errors.Add(error); }
        if (errors.Count != 0)
            MessageBox.Show(new AggregateException(errors).ToString(), "Studio cleanup failed", MessageBoxButton.OK, MessageBoxImage.Error);
        Shutdown(errors.Count == 0 ? 0 : 1);
    }

    private async Task StopHostAsync()
    {
        var host = _host;
        _host = null;
        if (host is not null) await DesktopHostLifetime.StopAsync(host);
    }
}
