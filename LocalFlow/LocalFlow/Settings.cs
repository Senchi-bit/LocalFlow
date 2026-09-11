namespace LocalFlow;

internal static class LocalFlowSettings
{
    public const int ListenPort = 45123;
    public const int DefaultPort = ListenPort;
    public const int HeaderSize = 26;
    public const int StreamBufferSize = 64 * 1024;
    public const int MaxControlPayload = 1024;
    public const int SocketBufferSize = 1 << 20;

    public const int MaxSessions = 8;
    public const long MaxFileSizeBytes = 10L * 1024 * 1024 * 1024;
    public const int MaxFileNameBytes = 255;

    public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan HelloAckTimeout = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan FinAckTimeout = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(60);

    public const string ServiceType = "_localflow._tcp";
    public const string InboxFolderName = "LocalFlow";

    public static string DefaultInboxPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            InboxFolderName);
}
