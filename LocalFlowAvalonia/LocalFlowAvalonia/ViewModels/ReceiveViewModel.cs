using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalFlowAvalonia.Discovery;
using LocalFlowAvalonia.Net;
using LocalFlowAvalonia.Services;
using LocalFlowAvalonia.Transfer;

namespace LocalFlowAvalonia.ViewModels;

public partial class IncomingTransferItem : ObservableObject
{
    public IncomingTransferItem(IncomingTransferUpdate update)
    {
        TransferId = update.TransferId;
        FileName = update.FileName;
        RemoteAddress = update.RemoteAddress;
        Status = update.Status;
        Progress = PercentOf(update);
        StatusText = update.Message;
        Detail = BuildDetail(update);
    }

    public Guid TransferId { get; }

    [ObservableProperty]
    public partial string FileName { get; set; }

    [ObservableProperty]
    public partial string RemoteAddress { get; set; }

    [ObservableProperty]
    public partial string Detail { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; }

    [ObservableProperty]
    public partial double Progress { get; set; }

    [ObservableProperty]
    public partial IncomingTransferStatus Status { get; set; }

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
        return string.IsNullOrWhiteSpace(update.RemoteAddress)
            ? size
            : $"от {update.RemoteAddress} · {size}";
    }
}

public partial class ReceiveViewModel : ViewModelBase, IDisposable
{
    private readonly IUiServices _ui;
    private readonly InboxStore _inbox;
    private readonly TcpFileServer _server;
    private readonly MdnsAdvertiser _mdns = new();
    private readonly CancellationTokenSource _cts = new();

    public ReceiveViewModel(IUiServices ui, Action goBack)
    {
        _ui = ui;
        GoBack = goBack;

        InboxPath = LocalFlowSettings.DefaultInboxPath;
        _inbox = new InboxStore(InboxPath);
        _inbox.EnsureCreated();
        _inbox.DeleteLeftoverPartials();

        _server = new TcpFileServer(_inbox);
        _server.TransferChanged += OnTransferChanged;

        ListenStatus = $"Слушаю порт {_server.Port}";
        IsListening = true;

        var mdnsOk = _mdns.TryStart();
        MdnsStatus = mdnsOk
            ? $"mDNS: {_mdns.FullInstanceName}"
            : "mDNS недоступен — приём по адресу и порту всё равно работает.";

        _ = RunServerAsync();
    }

    private Action GoBack { get; }

    public string InboxPath { get; }

    [ObservableProperty]
    public partial string ListenStatus { get; set; } = "";

    [ObservableProperty]
    public partial string MdnsStatus { get; set; } = "";

    [ObservableProperty]
    public partial bool IsListening { get; set; }

    public ObservableCollection<IncomingTransferItem> Transfers { get; } = [];

    public void Dispose()
    {
        _server.TransferChanged -= OnTransferChanged;
        _cts.Cancel();
        _ = _server.DisposeAsync();
        _ = _mdns.DisposeAsync();
    }

    [RelayCommand]
    private void Back() => GoBack();

    [RelayCommand]
    private Task OpenInboxAsync() => _ui.OpenFolderAsync(InboxPath);

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
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                IsListening = false;
                ListenStatus = $"Не удалось начать приём: {ex.Message}";
            });
        }
    }

    private void OnTransferChanged(IncomingTransferUpdate update)
    {
        Dispatcher.UIThread.Post(() =>
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
}
