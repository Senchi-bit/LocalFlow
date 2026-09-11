using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace LocalFlowAvalonia.Services;

internal sealed class AvaloniaUiServices(Window window) : IUiServices
{
    public async Task<string?> PickFileAsync(string title)
    {
        var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false
        });

        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public async Task OpenFolderAsync(string path)
    {
        Directory.CreateDirectory(path);
        try
        {
            await window.Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(path));
        }
        catch
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
    }
}
