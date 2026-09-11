using Haukcode.Mdns;

namespace LocalFlowAvalonia.Discovery;

internal sealed class MdnsAdvertiser : IAsyncDisposable
{
    private Haukcode.Mdns.MdnsAdvertiser? _inner;

    public MdnsAdvertiser()
    {
        var machine = ServiceProfile.SanitizeHostLabel(Environment.MachineName);
        InstanceName = $"LocalFlow-{machine}";
    }

    public string InstanceName { get; }

    public string ServiceType => LocalFlowSettings.ServiceType;

    public string FullInstanceName => $"{InstanceName}.{ServiceType}.local";

    public string? LastError { get; private set; }

    public bool TryStart()
    {
        try
        {
            var profile = new ServiceProfile(
                InstanceName,
                ServiceType,
                (ushort)LocalFlowSettings.ListenPort,
                new Dictionary<string, string> { ["txtvers"] = "1" });

            _inner = new Haukcode.Mdns.MdnsAdvertiser(profile);
            _inner.Start();
            LastError = null;
            return true;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            _inner = null;
            return false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_inner is null)
            return;

        await _inner.DisposeAsync();
        _inner = null;
    }
}
