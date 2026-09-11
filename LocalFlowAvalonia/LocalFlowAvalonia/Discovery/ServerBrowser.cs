using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Haukcode.Mdns;

namespace LocalFlowAvalonia.Discovery;

internal sealed class ServerBrowser : IDisposable
{
    private readonly object _sync = new();
    private MdnsBrowser? _browser;
    private bool _disposed;

    public event Action<DiscoveredServer>? ServerFound;
    public event Action<string>? ServerLost;
    public event Action<string>? Error;

    public void Start()
    {
        lock (_sync)
        {
            if (_disposed)
                return;

            StopCore();
            try
            {
                _browser = new MdnsBrowser(LocalFlowSettings.ServiceType);
                _browser.ServiceFound += OnServiceFound;
                _browser.ServiceLost += OnServiceLost;
                _browser.Start();
            }
            catch (Exception ex)
            {
                _browser?.Dispose();
                _browser = null;
                Error?.Invoke($"Не удалось запустить поиск mDNS: {ex.Message}");
            }
        }
    }

    public void Refresh() => Start();

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
                return;
            _disposed = true;
            StopCore();
        }
    }

    private void StopCore()
    {
        if (_browser is null)
            return;

        _browser.ServiceFound -= OnServiceFound;
        _browser.ServiceLost -= OnServiceLost;
        _browser.Dispose();
        _browser = null;
    }

    private void OnServiceFound(ServiceProfile profile)
    {
        var address = PickAddress(profile);
        if (address is null)
            return;

        var port = profile.Port == 0 ? LocalFlowSettings.DefaultPort : profile.Port;
        ServerFound?.Invoke(new DiscoveredServer(profile.InstanceName, address, port));
    }

    private void OnServiceLost(ServiceProfile profile)
    {
        ServerLost?.Invoke(profile.InstanceName);
    }

    private static IPAddress? PickAddress(ServiceProfile profile)
    {
        var candidates = new List<IPAddress>();
        if (profile.Addresses is { Count: > 0 })
            candidates.AddRange(profile.Addresses);
        else if (profile.Address is not null)
            candidates.Add(profile.Address);

        var ipv4 = candidates
            .Where(a => a.AddressFamily == AddressFamily.InterNetwork)
            .ToList();
        if (ipv4.Count == 0)
            return candidates.FirstOrDefault();

        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up)
                continue;

            foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
            {
                if (unicast.Address.AddressFamily != AddressFamily.InterNetwork)
                    continue;
                if (unicast.IPv4Mask is null)
                    continue;

                var local = unicast.Address.GetAddressBytes();
                var mask = unicast.IPv4Mask.GetAddressBytes();
                foreach (var candidate in ipv4)
                {
                    var remote = candidate.GetAddressBytes();
                    if (SameSubnet(local, remote, mask))
                        return candidate;
                }
            }
        }

        return ipv4[0];
    }

    private static bool SameSubnet(byte[] local, byte[] remote, byte[] mask)
    {
        if (local.Length != 4 || remote.Length != 4 || mask.Length != 4)
            return false;

        for (var i = 0; i < 4; i++)
        {
            if ((local[i] & mask[i]) != (remote[i] & mask[i]))
                return false;
        }

        return true;
    }
}
