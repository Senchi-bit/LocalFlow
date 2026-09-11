using System.Collections.ObjectModel;
using System.Net;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalFlowAvalonia.Discovery;
using LocalFlowAvalonia.Net;
using LocalFlowAvalonia.Services;

namespace LocalFlowAvalonia.ViewModels;

public partial class SendViewModel : ViewModelBase, IDisposable
{
    private readonly IUiServices _ui;
    private readonly Action _goBack;
    private readonly ServerBrowser _browser = new();
    private CancellationTokenSource? _sendCts;

    public SendViewModel(IUiServices ui, Action goBack)
    {
        _ui = ui;
        _goBack = goBack;

        _browser.ServerFound += OnServerFound;
        _browser.ServerLost += OnServerLost;
        _browser.Error += OnBrowserError;
        _browser.Start();
    }

    public ObservableCollection<DiscoveredServer> Servers { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    public partial DiscoveredServer? SelectedServer { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    public partial string ManualAddress { get; set; } = $"127.0.0.1:{LocalFlowSettings.DefaultPort}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFile))]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    public partial string FilePath { get; set; } = "";

    public bool HasFile => !string.IsNullOrWhiteSpace(FilePath);

    [ObservableProperty]
    public partial string Status { get; set; } = "Выберите сервер и файл.";

    [ObservableProperty]
    public partial double Progress { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(BackCommand))]
    [NotifyCanExecuteChangedFor(nameof(BrowseFileCommand))]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    public partial bool IsSending { get; set; }

    public bool IsIdle => !IsSending;

    public void SetDroppedFile(string path)
    {
        if (IsSending)
            return;
        if (!File.Exists(path))
            return;

        FilePath = path;
        Status = $"Файл: {Path.GetFileName(path)}";
    }

    public void Dispose()
    {
        _sendCts?.Cancel();
        _sendCts?.Dispose();
        _browser.Dispose();
    }

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void Back() => _goBack();

    private bool CanGoBack() => !IsSending;

    [RelayCommand(CanExecute = nameof(CanBrowse))]
    private async Task BrowseFileAsync()
    {
        var path = await _ui.PickFileAsync("Выберите файл для отправки");
        if (path is not null)
            SetDroppedFile(path);
    }

    private bool CanBrowse() => !IsSending;

    [RelayCommand]
    private void RefreshServers()
    {
        Servers.Clear();
        SelectedServer = null;
        Status = "Поиск серверов…";
        _browser.Refresh();
    }

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        if (!TryResolveEndpoint(out var remote, out var error))
        {
            Status = error;
            return;
        }

        _sendCts?.Dispose();
        _sendCts = new CancellationTokenSource();
        IsSending = true;
        Progress = 0;
        Status = "Подключение…";

        var progress = new Progress<TransferProgress>(update =>
        {
            Progress = update.Percent;
            Status = update.Status;
        });

        try
        {
            await TcpFileSender.SendAsync(FilePath, remote, progress, _sendCts.Token);
        }
        catch (OperationCanceledException)
        {
            Status = "Отменено.";
        }
        catch (Exception ex)
        {
            Status = $"Ошибка: {ex.Message}";
        }
        finally
        {
            IsSending = false;
        }
    }

    private bool CanSend() =>
        !IsSending
        && !string.IsNullOrWhiteSpace(FilePath)
        && (SelectedServer is not null || !string.IsNullOrWhiteSpace(ManualAddress));

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        _sendCts?.Cancel();
        Status = "Отмена…";
    }

    private bool CanCancel() => IsSending;

    private bool TryResolveEndpoint(out IPEndPoint endpoint, out string error)
    {
        endpoint = new IPEndPoint(IPAddress.None, 0);
        error = "";

        if (SelectedServer is not null)
        {
            endpoint = new IPEndPoint(SelectedServer.Address, SelectedServer.Port);
            return true;
        }

        var text = ManualAddress.Trim();
        if (IPEndPoint.TryParse(text, out var parsed))
        {
            endpoint = parsed.Port == 0
                ? new IPEndPoint(parsed.Address, LocalFlowSettings.DefaultPort)
                : parsed;
            return true;
        }

        if (IPAddress.TryParse(text, out var address))
        {
            endpoint = new IPEndPoint(address, LocalFlowSettings.DefaultPort);
            return true;
        }

        error = "Укажите сервер из списка или адрес вида 192.168.1.5:45123.";
        return false;
    }

    private void OnServerFound(DiscoveredServer server)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (Servers.Any(s => s.Name == server.Name))
            {
                var existing = Servers.First(s => s.Name == server.Name);
                var index = Servers.IndexOf(existing);
                Servers[index] = server;
                if (ReferenceEquals(SelectedServer, existing))
                    SelectedServer = server;
                return;
            }

            Servers.Add(server);
            SelectedServer ??= server;
            if (!IsSending)
                Status = $"Найден сервер: {server.Name}";
        });
    }

    private void OnServerLost(string name)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var existing = Servers.FirstOrDefault(s => s.Name == name);
            if (existing is null)
                return;

            var wasSelected = ReferenceEquals(SelectedServer, existing);
            Servers.Remove(existing);
            if (wasSelected)
                SelectedServer = Servers.FirstOrDefault();
        });
    }

    private void OnBrowserError(string message)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!IsSending)
                Status = message;
        });
    }
}
