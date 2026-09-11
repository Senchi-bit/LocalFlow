using System.Buffers.Binary;
using System.Text;

namespace LocalFlow.Protocol;

/// <summary>
/// Двоичный формат LocalFlow TCP v2 (младший байт первым).
///
/// Кадр: магическое "LFLW" | version=2 | type | transferId (16-байтовый .NET Guid) | payloadLen uint32 | payload.
///
/// Hello:    uint16 nameLen | utf8-имя | int64 fileSize
/// HelloAck: uint8 status | uint16 reasonLen | utf8-причина
/// Fin:      SHA-256 (32 байта)
/// FinAck:   uint8 status | uint16 reasonLen | utf8-причина
/// Abort:    uint16 reasonLen | utf8-причина
///
/// Тело файла идёт сырыми байтами между HelloAck и Fin, ровно fileSize байт.
/// </summary>
internal static class PacketCodec
{
    private static ReadOnlySpan<byte> Magic => "LFLW"u8;
    private const byte Version = 2;
    public const int HeaderSize = 26;
    public const int Sha256Size = 32;

    public static async Task WriteAsync(Stream stream, ProtocolPacket packet, CancellationToken cancellationToken)
    {
        var payload = EncodePayload(packet);
        if (payload.Length > LocalFlowSettings.MaxControlPayload)
            throw new InvalidDataException("Слишком большой управляющий кадр.");

        var frame = new byte[HeaderSize + payload.Length];
        WriteHeader(frame, TypeOf(packet), packet.TransferId, (uint)payload.Length);
        payload.CopyTo(frame.AsSpan(HeaderSize));
        await stream.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async Task<ProtocolPacket> ReadAsync(Stream stream, CancellationToken cancellationToken)
    {
        var header = new byte[HeaderSize];
        await stream.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);

        if (!header.AsSpan(0, 4).SequenceEqual(Magic))
            throw new InvalidDataException("Неверная магия кадра.");
        if (header[4] != Version)
            throw new InvalidDataException("Неподдерживаемая версия протокола.");

        var type = (PacketType)header[5];
        var transferId = new Guid(header.AsSpan(6, 16));
        var payloadLen = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(22, 4));
        if (payloadLen > LocalFlowSettings.MaxControlPayload)
            throw new InvalidDataException("Слишком большой управляющий кадр.");

        var payload = payloadLen == 0 ? [] : new byte[payloadLen];
        if (payloadLen > 0)
            await stream.ReadExactlyAsync(payload, cancellationToken).ConfigureAwait(false);

        return Decode(type, transferId, payload);
    }

    private static byte[] EncodePayload(ProtocolPacket packet) => packet switch
    {
        HelloPacket hello => EncodeHello(hello),
        HelloAckPacket ack => EncodeHelloAck(ack),
        FinPacket fin => EncodeFin(fin),
        FinAckPacket ack => EncodeFinAck(ack),
        AbortPacket abort => EncodeAbort(abort),
        _ => throw new InvalidDataException("Неизвестный тип кадра.")
    };

    private static PacketType TypeOf(ProtocolPacket packet) => packet switch
    {
        HelloPacket => PacketType.Hello,
        HelloAckPacket => PacketType.HelloAck,
        FinPacket => PacketType.Fin,
        FinAckPacket => PacketType.FinAck,
        AbortPacket => PacketType.Abort,
        _ => throw new InvalidDataException("Неизвестный тип кадра.")
    };

    private static void WriteHeader(Span<byte> dest, PacketType type, Guid transferId, uint payloadLen)
    {
        Magic.CopyTo(dest);
        dest[4] = Version;
        dest[5] = (byte)type;
        transferId.TryWriteBytes(dest.Slice(6, 16));
        BinaryPrimitives.WriteUInt32LittleEndian(dest[22..], payloadLen);
    }

    private static byte[] EncodeHello(HelloPacket packet)
    {
        var nameBytes = Encoding.UTF8.GetBytes(packet.FileName);
        if (nameBytes.Length > LocalFlowSettings.MaxFileNameBytes)
            throw new InvalidDataException("Имя файла слишком длинное.");

        var payload = new byte[2 + nameBytes.Length + 8];
        var w = 0;
        WriteUtf8(payload, ref w, nameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(payload.AsSpan(w), packet.FileSize);
        return payload;
    }

    private static byte[] EncodeHelloAck(HelloAckPacket packet)
    {
        var reasonBytes = Encoding.UTF8.GetBytes(packet.Reason);
        var payload = new byte[1 + 2 + reasonBytes.Length];
        payload[0] = (byte)packet.Status;
        var w = 1;
        WriteUtf8(payload, ref w, reasonBytes);
        return payload;
    }

    private static byte[] EncodeFin(FinPacket packet)
    {
        if (packet.Sha256.Length != Sha256Size)
            throw new InvalidDataException("Неверный размер SHA-256.");
        return packet.Sha256.ToArray();
    }

    private static byte[] EncodeFinAck(FinAckPacket packet)
    {
        var reasonBytes = Encoding.UTF8.GetBytes(packet.Reason);
        var payload = new byte[1 + 2 + reasonBytes.Length];
        payload[0] = (byte)packet.Status;
        var w = 1;
        WriteUtf8(payload, ref w, reasonBytes);
        return payload;
    }

    private static byte[] EncodeAbort(AbortPacket packet)
    {
        var reasonBytes = Encoding.UTF8.GetBytes(packet.Reason);
        var payload = new byte[2 + reasonBytes.Length];
        var w = 0;
        WriteUtf8(payload, ref w, reasonBytes);
        return payload;
    }

    private static ProtocolPacket Decode(PacketType type, Guid transferId, ReadOnlySpan<byte> payload) => type switch
    {
        PacketType.Hello => DecodeHello(transferId, payload),
        PacketType.HelloAck => DecodeHelloAck(transferId, payload),
        PacketType.Fin => DecodeFin(transferId, payload),
        PacketType.FinAck => DecodeFinAck(transferId, payload),
        PacketType.Abort => DecodeAbort(transferId, payload),
        _ => throw new InvalidDataException("Неизвестный тип кадра.")
    };

    private static HelloPacket DecodeHello(Guid transferId, ReadOnlySpan<byte> payload)
    {
        var r = 0;
        var name = ReadUtf8(payload, ref r, LocalFlowSettings.MaxFileNameBytes);
        Need(payload, r, 8);
        var fileSize = BinaryPrimitives.ReadInt64LittleEndian(payload[r..]);
        return new HelloPacket(transferId, name, fileSize);
    }

    private static HelloAckPacket DecodeHelloAck(Guid transferId, ReadOnlySpan<byte> payload)
    {
        var r = 0;
        Need(payload, r, 1);
        var status = (HelloAckStatus)payload[r++];
        var reason = ReadUtf8(payload, ref r, 1024);
        return new HelloAckPacket(transferId, status, reason);
    }

    private static FinPacket DecodeFin(Guid transferId, ReadOnlySpan<byte> payload)
    {
        Need(payload, 0, Sha256Size);
        if (payload.Length != Sha256Size)
            throw new InvalidDataException("Неверный размер SHA-256.");
        return new FinPacket(transferId, payload[..Sha256Size].ToArray());
    }

    private static FinAckPacket DecodeFinAck(Guid transferId, ReadOnlySpan<byte> payload)
    {
        var r = 0;
        Need(payload, r, 1);
        var status = (FinAckStatus)payload[r++];
        var reason = ReadUtf8(payload, ref r, 1024);
        return new FinAckPacket(transferId, status, reason);
    }

    private static AbortPacket DecodeAbort(Guid transferId, ReadOnlySpan<byte> payload)
    {
        var r = 0;
        var reason = ReadUtf8(payload, ref r, 1024);
        return new AbortPacket(transferId, reason);
    }

    private static void WriteUtf8(Span<byte> dest, ref int w, byte[] utf8)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(dest[w..], (ushort)utf8.Length);
        w += 2;
        utf8.CopyTo(dest[w..]);
        w += utf8.Length;
    }

    private static string ReadUtf8(ReadOnlySpan<byte> payload, ref int r, int maxBytes)
    {
        Need(payload, r, 2);
        var len = BinaryPrimitives.ReadUInt16LittleEndian(payload[r..]);
        r += 2;
        if (len > maxBytes)
            throw new InvalidDataException("Слишком длинная строка.");
        Need(payload, r, len);
        var value = Encoding.UTF8.GetString(payload.Slice(r, len));
        r += len;
        return value;
    }

    private static void Need(ReadOnlySpan<byte> payload, int offset, int count)
    {
        if (offset < 0 || count < 0 || offset + count > payload.Length)
            throw new InvalidDataException("Обрезанный кадр.");
    }
}
