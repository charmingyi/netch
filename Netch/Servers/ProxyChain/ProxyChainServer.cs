using Netch.Models;

namespace Netch.Servers;

public sealed class ProxyChainServer : Server
{
    public override string Type { get; } = "Chain";

    public List<Server> Nodes { get; set; } = new();

    public override string Hostname
    {
        get => Nodes.LastOrDefault()?.Hostname ?? string.Empty;
        set { }
    }

    public override ushort Port
    {
        get => Nodes.LastOrDefault()?.Port ?? 0;
        set { }
    }

    public override string MaskedData() => $"{Nodes.Count} hop(s)";

    public override string ToString()
    {
        var name = string.IsNullOrWhiteSpace(Remark) ? "Proxy chain" : Remark;
        return $"[CHAIN][{Group}] {name} ({string.Join(" → ", Nodes.Select(node => !string.IsNullOrWhiteSpace(node.Remark) ? node.Remark : node.Hostname))})";
    }
}
