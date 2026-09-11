namespace LocalFlow.Protocol;

/// <summary>Типы кадров LocalFlow TCP v2. Клиент → сервер: Hello, Fin, Abort.</summary>
internal enum PacketType : byte
{
    Hello = 1,
    HelloAck = 2,
    Fin = 3,
    FinAck = 4,
    Abort = 5
}

internal enum HelloAckStatus : byte
{
    Ok = 0,
    Rejected = 1
}

internal enum FinAckStatus : byte
{
    Ok = 0,
    HashMismatch = 1,
    Error = 2
}

internal abstract record ProtocolPacket(Guid TransferId);

internal sealed record HelloPacket(Guid TransferId, string FileName, long FileSize) : ProtocolPacket(TransferId);

internal sealed record HelloAckPacket(Guid TransferId, HelloAckStatus Status, string Reason) : ProtocolPacket(TransferId);

internal sealed record FinPacket(Guid TransferId, byte[] Sha256) : ProtocolPacket(TransferId);

internal sealed record FinAckPacket(Guid TransferId, FinAckStatus Status, string Reason) : ProtocolPacket(TransferId);

internal sealed record AbortPacket(Guid TransferId, string Reason) : ProtocolPacket(TransferId);
