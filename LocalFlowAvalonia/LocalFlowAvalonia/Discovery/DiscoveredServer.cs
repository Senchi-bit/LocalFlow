using System.Net;

namespace LocalFlowAvalonia.Discovery;

public sealed class DiscoveredServer : IEquatable<DiscoveredServer>
{
    public DiscoveredServer(string name, IPAddress address, int port)
    {
        Name = name;
        Address = address;
        Port = port;
    }

    public string Name { get; }
    public IPAddress Address { get; }
    public int Port { get; }

    public string DisplayName => $"{Name}  —  {Address}:{Port}";

    public bool Equals(DiscoveredServer? other) =>
        other is not null
        && Name == other.Name
        && Address.Equals(other.Address)
        && Port == other.Port;

    public override bool Equals(object? obj) => Equals(obj as DiscoveredServer);

    public override int GetHashCode() => HashCode.Combine(Name, Address, Port);

    public override string ToString() => DisplayName;
}
