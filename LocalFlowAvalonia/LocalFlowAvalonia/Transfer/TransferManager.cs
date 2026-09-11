using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using LocalFlowAvalonia.Protocol;

namespace LocalFlowAvalonia.Transfer;

internal sealed class TransferManager(InboxStore inbox) : IDisposable
{
    private readonly ConcurrentDictionary<Guid, TransferSession> _sessions = new();
    private readonly SemaphoreSlim _sessionSlots = new(LocalFlowSettings.MaxSessions, LocalFlowSettings.MaxSessions);

    public event Action<IncomingTransferUpdate>? TransferChanged;

    public async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        var remote = (IPEndPoint?)client.Client.RemoteEndPoint
            ?? new IPEndPoint(IPAddress.None, 0);

        client.NoDelay = true;
        client.ReceiveBufferSize = LocalFlowSettings.SocketBufferSize;
        client.SendBufferSize = LocalFlowSettings.SocketBufferSize;

        var stream = client.GetStream();
        TransferSession? session = null;
        var slotTaken = false;
        var helloAccepted = false;
        var terminal = new TerminalState();

        using var idleCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        idleCts.CancelAfter(LocalFlowSettings.IdleTimeout);

        try
        {
            var first = await PacketCodec.ReadAsync(stream, idleCts.Token);
            if (first is AbortPacket)
                return;
            if (first is not HelloPacket hello)
                throw new InvalidDataException("Ожидался кадр Hello.");

            if (!TryAcceptHello(hello, remote, out session, out var rejectReason, out slotTaken)
                || session is null)
            {
                Raise(new IncomingTransferUpdate(
                    hello.TransferId,
                    hello.FileName,
                    remote.Address.ToString(),
                    hello.FileSize,
                    0,
                    IncomingTransferStatus.Failed,
                    rejectReason,
                    null));
                terminal.Reported = true;
                await PacketCodec.WriteAsync(
                    stream,
                    new HelloAckPacket(hello.TransferId, HelloAckStatus.Rejected, rejectReason),
                    cancellationToken);
                return;
            }

            if (!_sessions.TryAdd(hello.TransferId, session))
            {
                session.Dispose();
                session = null;
                if (slotTaken)
                {
                    ReleaseSlot();
                    slotTaken = false;
                }

                const string duplicate = "повторный идентификатор передачи";
                Raise(new IncomingTransferUpdate(
                    hello.TransferId,
                    hello.FileName,
                    remote.Address.ToString(),
                    hello.FileSize,
                    0,
                    IncomingTransferStatus.Failed,
                    duplicate,
                    null));
                terminal.Reported = true;
                await PacketCodec.WriteAsync(
                    stream,
                    new HelloAckPacket(hello.TransferId, HelloAckStatus.Rejected, duplicate),
                    cancellationToken);
                return;
            }

            await PacketCodec.WriteAsync(
                stream,
                new HelloAckPacket(session.TransferId, HelloAckStatus.Ok, ""),
                cancellationToken);
            helloAccepted = true;
            session.ProgressChanged += () => RaiseFromSession(session, IncomingTransferStatus.Receiving, "Приём…");
            RaiseFromSession(session, IncomingTransferStatus.Receiving, "Приём…");

            await ReceiveBodyAsync(stream, session, idleCts, cancellationToken);

            idleCts.CancelAfter(LocalFlowSettings.IdleTimeout);
            var afterBody = await PacketCodec.ReadAsync(stream, idleCts.Token);
            switch (afterBody)
            {
                case AbortPacket abort:
                    ReportTerminal(
                        terminal,
                        session,
                        IncomingTransferStatus.Cancelled,
                        string.IsNullOrWhiteSpace(abort.Reason) ? "отменено отправителем" : abort.Reason);
                    return;
                case FinPacket fin:
                    await FinishAsync(stream, session, fin, cancellationToken, terminal);
                    break;
                default:
                    throw new InvalidDataException("Ожидался кадр Fin.");
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            if (helloAccepted && session is not null)
                await TrySendAbortAsync(stream, session.TransferId, "таймаут простоя");
            ReportTerminal(terminal, session, IncomingTransferStatus.Failed, "таймаут простоя");
        }
        catch (OperationCanceledException)
        {
            if (helloAccepted && session is not null)
                await TrySendAbortAsync(stream, session.TransferId, "сервер останавливается");
            ReportTerminal(terminal, session, IncomingTransferStatus.Cancelled, "приём остановлен");
        }
        catch (EndOfStreamException)
        {
            ReportTerminal(terminal, session, IncomingTransferStatus.Cancelled, "соединение разорвано");
        }
        catch (IOException ex)
        {
            ReportTerminal(terminal, session, IncomingTransferStatus.Failed, ex.Message);
        }
        catch (InvalidDataException ex)
        {
            if (helloAccepted && session is not null)
                await TrySendAbortAsync(stream, session.TransferId, ex.Message);
            ReportTerminal(terminal, session, IncomingTransferStatus.Failed, ex.Message);
        }
        catch (Exception ex)
        {
            if (helloAccepted && session is not null)
                await TrySendAbortAsync(stream, session.TransferId, "ошибка сервера");
            ReportTerminal(terminal, session, IncomingTransferStatus.Failed, ex.Message);
        }
        finally
        {
            if (session is not null)
            {
                _sessions.TryRemove(session.TransferId, out _);
                session.Dispose();
            }

            if (slotTaken)
                ReleaseSlot();
        }
    }

    public void Dispose()
    {
        foreach (var id in _sessions.Keys)
        {
            if (_sessions.TryRemove(id, out var session))
                session.Dispose();
        }

        _sessionSlots.Dispose();
    }

    private bool TryAcceptHello(
        HelloPacket hello,
        IPEndPoint remote,
        out TransferSession? session,
        out string rejectReason,
        out bool slotTaken)
    {
        session = null;
        rejectReason = "";
        slotTaken = false;

        switch (hello.FileSize)
        {
            case < 0:
                rejectReason = "Ошибочный размер файла";
                return false;
            case > LocalFlowSettings.MaxFileSizeBytes:
                rejectReason = "файл больше 10 ГиБ";
                return false;
        }

        if (!inbox.TrySanitizeFileName(hello.FileName, out var safeName, out var nameError))
        {
            rejectReason = nameError;
            return false;
        }

        if (!inbox.HasEnoughSpace(hello.FileSize))
        {
            rejectReason = "недостаточно места на диске";
            return false;
        }

        if (!_sessionSlots.Wait(0))
        {
            rejectReason = "слишком много одновременных передач";
            return false;
        }

        slotTaken = true;
        try
        {
            session = new TransferSession(
                hello.TransferId,
                remote,
                safeName,
                safeName,
                hello.FileSize,
                inbox);
            return true;
        }
        catch (Exception ex)
        {
            ReleaseSlot();
            slotTaken = false;
            rejectReason = ex.Message;
            return false;
        }
    }

    private static async Task ReceiveBodyAsync(
        NetworkStream stream,
        TransferSession session,
        CancellationTokenSource idleCts,
        CancellationToken cancellationToken)
    {
        var remaining = session.FileSize;
        if (remaining == 0)
            return;

        var buffer = new byte[LocalFlowSettings.StreamBufferSize];
        while (remaining > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            idleCts.CancelAfter(LocalFlowSettings.IdleTimeout);

            var toRead = (int)Math.Min(buffer.Length, remaining);
            var read = await stream.ReadAsync(buffer.AsMemory(0, toRead), idleCts.Token);
            if (read == 0)
                throw new EndOfStreamException();

            session.Write(buffer.AsSpan(0, read));
            remaining -= read;
        }
    }

    private async Task FinishAsync(
        NetworkStream stream,
        TransferSession session,
        FinPacket fin,
        CancellationToken cancellationToken,
        TerminalState terminal)
    {
        var ack = session.Complete(fin.Sha256, out var savedPath);
        await PacketCodec.WriteAsync(stream, ack, cancellationToken);

        switch (ack.Status)
        {
            case FinAckStatus.Ok:
                ReportTerminal(terminal, session, IncomingTransferStatus.Completed, "Готово", savedPath);
                break;
            case FinAckStatus.HashMismatch:
                ReportTerminal(terminal, session, IncomingTransferStatus.Failed, "не совпал SHA-256");
                break;
            case FinAckStatus.Error:
                ReportTerminal(
                    terminal,
                    session,
                    IncomingTransferStatus.Failed,
                    string.IsNullOrWhiteSpace(ack.Reason) ? "ошибка сохранения" : ack.Reason);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private static async Task TrySendAbortAsync(NetworkStream stream, Guid transferId, string reason)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await PacketCodec.WriteAsync(stream, new AbortPacket(transferId, reason), cts.Token);
        }
        catch
        {
            // ignored
        }
    }

    private void ReleaseSlot()
    {
        try
        {
            _sessionSlots.Release();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void ReportTerminal(
        TerminalState terminal,
        TransferSession? session,
        IncomingTransferStatus status,
        string message,
        string? savedPath = null)
    {
        if (terminal.Reported || session is null)
            return;

        terminal.Reported = true;
        RaiseFromSession(session, status, message, savedPath);
    }

    private void RaiseFromSession(
        TransferSession session,
        IncomingTransferStatus status,
        string message,
        string? savedPath = null)
    {
        Raise(new IncomingTransferUpdate(
            session.TransferId,
            session.OriginalName,
            session.Remote.Address.ToString(),
            session.FileSize,
            session.BytesReceived,
            status,
            message,
            savedPath));
    }

    private void Raise(IncomingTransferUpdate update) => TransferChanged?.Invoke(update);

    private sealed class TerminalState
    {
        public bool Reported;
    }
}
