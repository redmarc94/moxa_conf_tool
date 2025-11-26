using System.Net.NetworkInformation;

namespace MoxaConfigApp;

internal sealed class NetworkInterfaceItem
{
    public NetworkInterfaceItem(NetworkInterface networkInterface)
    {
        Interface = networkInterface;
        Name = networkInterface.Name;
        Description = networkInterface.Description;
        Status = networkInterface.OperationalStatus;
    }

    public string Name { get; }
    public string Description { get; }
    public OperationalStatus Status { get; }
    public NetworkInterface Interface { get; }

    public string Display => $"{Name} ({Description}) - {Status}";
}
