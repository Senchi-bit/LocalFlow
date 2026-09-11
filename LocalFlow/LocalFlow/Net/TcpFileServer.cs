using System.Net;
using System.Net.Sockets;
using LocalFlow.Transfer;

namespace LocalFlow.Net;

internal sealed class TcpFileServer : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly TransferManager _transfers;

    public TcpFileServer(InboxStore inbox)
    {
        _listener = new TcpListener(IPAddress.Any, LocalFlowSettings.ListenPort);
        _listener.Server.ReceiveBufferSize = LocalFlowSettings.SocketBufferSize;
        _listener.Server.SendBufferSize = LocalFlowSettings.SocketBufferSize;
        _transfers = new TransferManager(inbox);
        _transfers.TransferChanged += update => TransferChanged?.Invoke(update);
    }

    public event Action<IncomingTransferUpdate>? TransferChanged;

    public int Port => LocalFlowSettings.ListenPort;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        _listener.Start();
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (SocketException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (SocketException)
                {
                    continue;
                }

                _ = HandleClientAsync(client, cancellationToken);
            }
        }
        finally
        {
            try
            {
                _listener.Stop();
            }
            catch
            {
                // ignored
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        try
        {
            _listener.Stop();
        }
        catch
        {
            // ignored
        }

        _transfers.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        try
        {
            using (client)
                await _transfers.HandleClientAsync(client, cancellationToken);
        }
        catch
        {
            // connection-level failures are reported by TransferManager when possible
        }
    }
}
