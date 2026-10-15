using Netch.Interfaces;
using Netch.Models;
using Netch.Utils;
using Netch.Controllers;

namespace Netch.Servers;

public sealed class ProxyChainUtil : IServerUtil
{
    public ushort Priority { get; } = 100;
    public string TypeName { get; } = "Chain";
    public string FullName { get; } = "Proxy Chain";
    public string ShortName { get; } = "CHAIN";
    public string[] UriScheme { get; } = Array.Empty<string>();
    public Type ServerType { get; } = typeof(ProxyChainServer);

    public void Edit(Server server) => new ProxyChainForm((ProxyChainServer)server).ShowDialog();

    public void Create() => new ProxyChainForm().ShowDialog();

    public string GetShareLink(Server server) => ShareLink.GetNetchLink(server);

    public IServerController GetController() => new ProxyChainController();

    public IEnumerable<Server> ParseUri(string text) => Array.Empty<Server>();

    public bool CheckServer(Server server) => server is ProxyChainServer chain && chain.Nodes.Count is >= 2 and <= 5 &&
        chain.Nodes.All(node => ProxyChainController.CanChain(node) && ServerHelper.GetUtilByTypeName(node.Type).CheckServer(node));
}
