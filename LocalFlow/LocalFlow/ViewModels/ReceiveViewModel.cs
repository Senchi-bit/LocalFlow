using System.Diagnostics;
using System.Windows.Input;
using LocalFlow.Transfer;

namespace LocalFlow.ViewModels;

internal sealed class IncomingTransferItem : ViewModelBase
{
    private string _fileName;
    private string _remoteAddress;
    private string _detail;
    private string _statusText;
    private double _progress;
    private IncomingTransferStatus _status;

    public IncomingTransferItem(IncomingTransferUpdate update)
    {
        TransferId = update.TransferId;
        _fileName = update.FileName;
        _remoteAddress = update.RemoteAddress;
        _status = update.Status;
        _progress = PercentOf(update);
        _statusText = update.Message;
        _detail = BuildDetail(update);
    }

    public Guid TransferId { get; }

    public string FileName
    {
        get => _fileName;
        private set => Set(ref _fileName, value);
    }

    public string RemoteAddress
    {
        get => _remoteAddress;
        private set => Set(ref _remoteAddress, value);
    }

    public string Detail
    {
        get => _detail;
        private set => Set(ref _detail, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => Set(ref _statusText, value);
    }

    public double Progress
    {
        get => _progress;
        private set => Set(ref _progress, value);
    }

    public IncomingTransferStatus Status
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    public void Apply(IncomingTransferUpdate update)
    {
        FileName = update.FileName;
        RemoteAddress = update.RemoteAddress;
        Status = update.Status;
        Progress = PercentOf(update);
        StatusText = update.Message;
        Detail = BuildDetail(update);
    }

    private static double PercentOf(IncomingTransferUpdate update)
    {
        if (update.Status == IncomingTransferStatus.Completed)
            return 100;
        if (update.FileSize <= 0)
            return update.Status == IncomingTransferStatus.Receiving ? 0 : 100;
        return Math.Clamp(update.BytesReceived * 100.0 / update.FileSize, 0, 100);
    }

    private static string BuildDetail(IncomingTransferUpdate update)
    {
        var size = ByteFormatter.FormatSize(update.FileSize);
        var from = string.IsNullOrWhiteSpace(update.RemoteAddress)
            ? size
            : $"от {update.RemoteAddress} · {size}";
        return from;
    }
}

internal sealed class ReceiveViewModel : ViewModelBase, IDisposable
{
    private readonly System.Windows.Threading.Dispatcher _ui;
    private readonly InboxStore _inbox;
    private readonly LocalFlow.Net.TcpFileServer _server;
    private readonly LocalFlow.Discovery.MdnsAdvertiser _mdns = new();
    private readonly CancellationTokenSource _cts = new();
    private string _listenStatus;
    private string _mdnsStatus;
    private bool _isListening;

    public ReceiveViewModel(System.Windows.Threading.Dispatcher ui, Action goBack)
    {
        _ui = ui;
        BackCommand = new RelayCommand(goBack);
        OpenInboxCommand = new RelayCommand(OpenInbox);

        InboxPath = LocalFlowSettings.DefaultInboxPath;
        _inbox = new InboxStore(InboxPath);
        _inbox.EnsureCreated();
        _inbox.DeleteLeftoverPartials();

        _server = new LocalFlow.Net.TcpFileServer(_inbox);
        _server.TransferChanged += OnTransferChanged;

        _listenStatus = $"Слушаю порт {_server.Port}";
        _mdnsStatus = "Запуск mDNS…";
        _isListening = true;

        var mdnsOk = _mdns.TryStart();
        _mdnsStatus = mdnsOk
            ? $"mDNS: {_mdns.FullInstanceName}"
            : "mDNS недоступен — приём по адресу и порту всё равно работает.";

        _ = RunServerAsync();
    }

    public string InboxPath { get; }

    public string ListenStatus
    {
        get => _listenStatus;
        private set => Set(ref _listenStatus, value);
    }

    public string MdnsStatus
    {
        get => _mdnsStatus;
        private set => Set(ref _mdnsStatus, value);
    }

    public bool IsListening
    {
        get => _isListening;
        private set => Set(ref _isListening, value);
    }

    public System.Collections.ObjectModel.ObservableCollection<IncomingTransferItem> Transfers { get; } = [];

    public ICommand BackCommand { get; }
    public ICommand OpenInboxCommand { get; }

    public void Dispose()
    {
        _server.TransferChanged -= OnTransferChanged;
        _cts.Cancel();
        _ = _server.DisposeAsync();
        _ = _mdns.DisposeAsync();
    }

    private async Task RunServerAsync()
    {
        try
        {
            await _server.RunAsync(_cts.Token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            await _ui.InvokeAsync(() =>
            {
                IsListening = false;
                ListenStatus = $"Не удалось начать приём: {ex.Message}";
            });
        }
    }

    private void OnTransferChanged(IncomingTransferUpdate update)
    {
        _ = _ui.InvokeAsync(() =>
        {
            var existing = Transfers.FirstOrDefault(t => t.TransferId == update.TransferId);
            if (existing is null)
            {
                Transfers.Insert(0, new IncomingTransferItem(update));
                return;
            }

            existing.Apply(update);
        });
    }

    private void OpenInbox()
    {
        Directory.CreateDirectory(InboxPath);
        Process.Start(new ProcessStartInfo
        {
            FileName = InboxPath,
            UseShellExecute = true
        });
    }
}
