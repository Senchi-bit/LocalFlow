namespace LocalFlow.Transfer;

internal enum IncomingTransferStatus
{
    Receiving,
    Completed,
    Failed,
    Cancelled
}

internal readonly record struct IncomingTransferUpdate(
    Guid TransferId,
    string FileName,
    string RemoteAddress,
    long FileSize,
    long BytesReceived,
    IncomingTransferStatus Status,
    string Message,
    string? SavedPath);
