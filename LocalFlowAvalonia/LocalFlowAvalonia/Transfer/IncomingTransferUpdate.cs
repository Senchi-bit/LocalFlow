namespace LocalFlowAvalonia.Transfer;

public enum IncomingTransferStatus
{
    Receiving,
    Completed,
    Failed,
    Cancelled
}

public readonly record struct IncomingTransferUpdate(
    Guid TransferId,
    string FileName,
    string RemoteAddress,
    long FileSize,
    long BytesReceived,
    IncomingTransferStatus Status,
    string Message,
    string? SavedPath);
