using System.Collections.ObjectModel;
using System.IO;
using System.Net;
using System.Windows.Input;
using System.Windows.Threading;
using LocalFlow.Discovery;
using LocalFlow.Net;
using Microsoft.Win32;

namespace LocalFlow.ViewModels;

internal sealed class SendViewModel : ViewModelBase, IDisposable
{
    private readonly Dispatcher _ui;
    private readonly ServerBrowser _browser = new();
    private DiscoveredServer? _selectedServer;
    private string _manualAddress = $"127.0.0.1:{LocalFlowSettings.DefaultPort}";
    private string _filePath = "";
    private string _status = "Выберите сервер и файл.";
    private double _progress;
    private bool _isSending;
    private CancellationTokenSource? _sendCts;

    public SendViewModel(Dispatcher ui, Action goBack)
    {
        _ui = ui;
        BackCommand = new RelayCommand(goBack, () => !IsSending);
        BrowseFileCommand = new RelayCommand(BrowseFile, () => !IsSending);
        RefreshServersCommand = new RelayCommand(RefreshServers);
        SendCommand = new RelayCommand(SendAsync, CanSend);
        CancelCommand = new RelayCommand(Cancel, () => IsSending);

        _browser.ServerFound += OnServerFound;
        _browser.ServerLost += OnServerLost;
        _browser.Error += OnBrowserError;
        _browser.Start();
    }

    public ObservableCollection<DiscoveredServer> Servers { get; } = [];

    public DiscoveredServer? SelectedServer
    {
        get => _selectedServer;
        set
        {
            if (Set(ref _selectedServer, value))
                RaiseCommands();
        }
    }

    public string ManualAddress
    {
        get => _manualAddress;
        set
        {
            if (Set(ref _manualAddress, value))
                RaiseCommands();
        }
    }

    public string FilePath
    {
        get => _filePath;
        set
        {
            if (Set(ref _filePath, value))
            {
                OnPropertyChanged(nameof(HasFile));
                RaiseCommands();
            }
        }
    }

    public bool HasFile => !string.IsNullOrWhiteSpace(FilePath);

    public string Status
    {
        get => _status;
        set => Set(ref _status, value);
    }

    public double Progress
    {
        get => _progress;
        set => Set(ref _progress, value);
    }

    public bool IsSending
    {
        get => _isSending;
        set
        {
            if (Set(ref _isSending, value))
            {
                OnPropertyChanged(nameof(IsIdle));
                RaiseCommands();
            }
        }
    }

    public bool IsIdle => !IsSending;

    public ICommand BackCommand { get; }
    public ICommand BrowseFileCommand { get; }
    public ICommand RefreshServersCommand { get; }
    public ICommand SendCommand { get; }
    public ICommand CancelCommand { get; }

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

    private void BrowseFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Выберите файл для отправки",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog() == true)
            SetDroppedFile(dialog.FileName);
    }

    private void RefreshServers()
    {
        Servers.Clear();
        SelectedServer = null;
        Status = "Поиск серверов…";
        _browser.Refresh();
    }

    private bool CanSend() =>
        !IsSending
        && !string.IsNullOrWhiteSpace(FilePath)
        && (SelectedServer is not null || !string.IsNullOrWhiteSpace(ManualAddress));

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

    private void Cancel()
    {
        _sendCts?.Cancel();
        Status = "Отмена…";
    }

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
        _ = _ui.InvokeAsync(() =>
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
        _ = _ui.InvokeAsync(() =>
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
        _ = _ui.InvokeAsync(() =>
        {
            if (!IsSending)
                Status = message;
        });
    }

    private void RaiseCommands()
    {
        (BackCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (BrowseFileCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (SendCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (CancelCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }
}
