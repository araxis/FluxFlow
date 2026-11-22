using Microsoft.Win32;

namespace FluxFlow.Studio.Desktop.Services;

internal interface IDesktopFileDialogs
{
    Task<string?> OpenAsync(string extensions, string? title = null);
    Task<string?> SaveAsync(string name);
}

internal sealed class DesktopFileDialogs : IDesktopFileDialogs
{
    public async Task<string?> OpenAsync(string extensions, string? title = null) =>
        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var patterns = string.Join(";", extensions.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(value => "*" + value));
            var dialog = new OpenFileDialog { Filter = patterns.Length == 0 ? "All files (*.*)|*.*" : $"Supported files ({patterns})|{patterns}|All files (*.*)|*.*", CheckFileExists = true };
            if (title is not null) dialog.Title = title;
            return dialog.ShowDialog(System.Windows.Application.Current.MainWindow) == true ? dialog.FileName : null;
        });

    public async Task<string?> SaveAsync(string name) =>
        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var extension = Path.GetExtension(name);
            var dialog = new SaveFileDialog
            {
                FileName = Path.GetFileName(name), DefaultExt = extension, AddExtension = true,
                Filter = $"Studio file (*{extension})|*{extension}|All files (*.*)|*.*", OverwritePrompt = true
            };
            return dialog.ShowDialog(System.Windows.Application.Current.MainWindow) == true ? dialog.FileName : null;
        });
}
