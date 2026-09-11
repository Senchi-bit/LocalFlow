using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using LocalFlow.Protocol;

namespace LocalFlow.Net;

internal readonly record struct TransferProgress(double Percent, string Status);

internal sealed class TransferException(string message) : Exception(message);

internal static class TcpFileSender
{
    public static async Task SendAsync(
        string filePath,
        IPEndPoint remote,
        IProgress<TransferProgress>? progress,
        CancellationToken cancellationToken)
    {
        var file = new FileInfo(filePath);
        if (!file.Exists)
            throw new TransferException("Файл не найден.");
        if (file.Length > LocalFlowSettings.MaxFileSizeBytes)
            throw new TransferException("Файл больше 10 ГиБ.");

        var transferId = Guid.NewGuid();
        await using var fileStream = new FileStream(
            file.FullName,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: LocalFlowSettings.StreamBufferSize,
            FileOptions.SequentialScan | FileOptions.Asynchronous);

        using var tcp = new TcpClient();
        tcp.NoDelay = true;
        tcp.ReceiveBufferSize = LocalFlowSettings.SocketBufferSize;
        tcp.SendBufferSize = LocalFlowSettings.SocketBufferSize;

        progress?.Report(new TransferProgress(0, "Подключение…"));
        await ConnectAsync(tcp, remote, cancellationToken);

        var stream = tcp.GetStream();
        var helloAccepted = false;
        try
        {
            await PacketCodec.WriteAsync(
                stream,
                new HelloPacket(transferId, file.Name, file.Length),
                cancellationToken);

            var helloAck = await ReadExpectedAsync<HelloAckPacket>(
                stream,
                transferId,
                LocalFlowSettings.HelloAckTimeout,
                cancellationToken);
            if (helloAck.Status != HelloAckStatus.Ok)
            {
                var reason = string.IsNullOrWhiteSpace(helloAck.Reason)
                    ? "сервер отклонил передачу"
                    : helloAck.Reason;
                throw new TransferException(reason);
            }

            helloAccepted = true;
            progress?.Report(new TransferProgress(0, "Отправка…"));

            var hash = await SendBodyAsync(stream, fileStream, file.Length, progress, cancellationToken);

            await PacketCodec.WriteAsync(stream, new FinPacket(transferId, hash), cancellationToken);

            var finAck = await ReadExpectedAsync<FinAckPacket>(
                stream,
                transferId,
                LocalFlowSettings.FinAckTimeout,
                cancellationToken);
            switch (finAck.Status)
            {
                case FinAckStatus.Ok:
                    progress?.Report(new TransferProgress(100, "Готово"));
                    return;
                case FinAckStatus.HashMismatch:
                    throw new TransferException("не совпал SHA-256");
                default:
                    throw new TransferException(
                        string.IsNullOrWhiteSpace(finAck.Reason) ? "ошибка сервера" : finAck.Reason);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            if (helloAccepted)
                await TrySendAbortAsync(stream, transferId, "таймаут простоя");
            throw new TransferException("таймаут передачи");
        }
        catch (OperationCanceledException)
        {
            if (helloAccepted)
                await TrySendAbortAsync(stream, transferId, "передача отменена");
            throw;
        }
        catch (TransferException)
        {
            if (helloAccepted)
                await TrySendAbortAsync(stream, transferId, "ошибка клиента");
            throw;
        }
        catch (InvalidDataException ex)
        {
            if (helloAccepted)
                await TrySendAbortAsync(stream, transferId, "ошибка клиента");
            throw new TransferException(ex.Message);
        }
        catch (IOException ex)
        {
            throw new TransferException(ex.Message);
        }
    }

    private static async Task ConnectAsync(TcpClient tcp, IPEndPoint remote, CancellationToken cancellationToken)
    {
        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        connectCts.CancelAfter(LocalFlowSettings.ConnectTimeout);
        try
        {
            await tcp.ConnectAsync(remote, connectCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TransferException("таймаут подключения");
        }
        catch (SocketException ex)
        {
            throw new TransferException($"не удалось подключиться: {ex.Message}");
        }
    }

    private static async Task<byte[]> SendBodyAsync(
        NetworkStream stream,
        FileStream fileStream,
        long fileSize,
        IProgress<TransferProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        if (fileSize == 0)
        {
            progress?.Report(new TransferProgress(100, "Отправка…"));
            return hasher.GetHashAndReset();
        }

        using var idleCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var buffer = new byte[LocalFlowSettings.StreamBufferSize];
        long sent = 0;
        while (sent < fileSize)
        {
            idleCts.CancelAfter(LocalFlowSettings.IdleTimeout);
            var toRead = (int)Math.Min(buffer.Length, fileSize - sent);
            var read = await fileStream.ReadAsync(buffer.AsMemory(0, toRead), idleCts.Token);
            if (read == 0)
                throw new TransferException("неожиданный конец файла");

            hasher.AppendData(buffer.AsSpan(0, read));
            await stream.WriteAsync(buffer.AsMemory(0, read), idleCts.Token);
            sent += read;

            var percent = sent * 100.0 / fileSize;
            progress?.Report(new TransferProgress(percent, "Отправка…"));
        }

        await stream.FlushAsync(cancellationToken);
        return hasher.GetHashAndReset();
    }

    private static async Task<T> ReadExpectedAsync<T>(
        NetworkStream stream,
        Guid transferId,
        TimeSpan timeout,
        CancellationToken cancellationToken)
        where T : ProtocolPacket
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        ProtocolPacket packet;
        try
        {
            packet = await PacketCodec.ReadAsync(stream, timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TransferException("таймаут ответа сервера");
        }
        catch (EndOfStreamException)
        {
            throw new TransferException("сервер закрыл соединение");
        }

        if (packet is AbortPacket abort)
        {
            var reason = string.IsNullOrWhiteSpace(abort.Reason) ? "передача отменена сервером" : abort.Reason;
            throw new TransferException(reason);
        }

        if (packet.TransferId != transferId)
            throw new TransferException("несовпадение идентификатора передачи");

        if (packet is not T expected)
            throw new TransferException("неожиданный ответ сервера");

        return expected;
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
        }
    }
}
