using System.Net;
using LocalFlowAvalonia.Net;
using LocalFlowAvalonia.Transfer;

namespace LocalFlowAvalonia;

internal static class LoopbackSelfTest
{
    public static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "LocalFlowSelfTest-" + Guid.NewGuid().ToString("N"));
        var inbox = Path.Combine(root, "inbox");
        Directory.CreateDirectory(inbox);
        var sourcePath = Path.Combine(root, "source.bin");
        var payload = "hello localflow"u8.ToArray();
        await File.WriteAllBytesAsync(sourcePath, payload);

        try
        {
            var store = new InboxStore(inbox);
            store.EnsureCreated();
            await using var server = new TcpFileServer(store);
            using var cts = new CancellationTokenSource();
            var completed = new TaskCompletionSource<IncomingTransferUpdate>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            server.TransferChanged += update =>
            {
                if (update.Status is IncomingTransferStatus.Completed
                    or IncomingTransferStatus.Failed
                    or IncomingTransferStatus.Cancelled)
                {
                    completed.TrySetResult(update);
                }
            };

            var run = server.RunAsync(cts.Token);
            await Task.Delay(300);

            await TcpFileSender.SendAsync(
                sourcePath,
                new IPEndPoint(IPAddress.Loopback, LocalFlowSettings.ListenPort),
                null,
                CancellationToken.None);

            var result = await completed.Task.WaitAsync(TimeSpan.FromSeconds(10));
            cts.Cancel();
            try
            {
                await run.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (OperationCanceledException)
            {
            }

            if (result.Status != IncomingTransferStatus.Completed)
                throw new InvalidOperationException(result.Message);

            var saved = Directory.EnumerateFiles(inbox, "source.bin").FirstOrDefault()
                        ?? result.SavedPath;
            if (saved is null || !File.Exists(saved))
                throw new InvalidOperationException("Файл не сохранён.");

            var actual = await File.ReadAllBytesAsync(saved);
            if (!payload.SequenceEqual(actual))
                throw new InvalidOperationException("Содержимое файла не совпало.");
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
                // ignored
            }
        }
    }
}
