using System.Net;
using Netch.Enums;
using Netch.Interfaces;
using Netch.Models;
using Netch.Servers;
using Netch.Utils;

namespace Netch.Controllers;

/// <summary>
///     Composes proxy clients as local SOCKS5 hops. The last configured node is
///     the application-facing exit; each later node dials through the previous hop.
/// </summary>
public sealed class ProxyChainController : IServerController
{
    private readonly List<IServerController> _startedControllers = new();
    public string Name => "Proxy chain";
    public ushort? Socks5LocalPort { get; set; }
    public string? LocalAddress { get; set; }

    public static bool CanChain(Server server) => server is Socks5Server or HttpProxyServer or VLESSServer or VMessServer or
        ShadowsocksServer or ShadowsocksRServer or TrojanServer or WireGuardServer or SSHServer or
        Hysteria2Server or AnyTlsServer or MieruServer;

    public async Task<Socks5Server> StartAsync(Server server)
    {
        if (server is not ProxyChainServer chain || chain.Nodes.Count is < 2 or > 5)
            throw new MessageException("A proxy chain must contain between two and five nodes.");
        if (chain.Nodes.Any(node => node is ProxyChainServer))
            throw new MessageException("Nested proxy chains are not supported.");
        if (chain.Nodes.Any(node => !CanChain(node) || !ServerHelper.GetUtilByTypeName(node.Type).CheckServer(node)))
            throw new MessageException("The chain contains an invalid or unsupported node.");

        Socks5Server? upstream = null;
        try
        {
            foreach (var node in chain.Nodes)
            {
                IUpstreamProxyController upstreamController = node switch
                {
                    Hysteria2Server or AnyTlsServer => new SingBoxController(),
                    ShadowsocksServer shadowsocks when SSGlobal.Is2022Method(shadowsocks.EncryptMethod) => new SingBoxController(),
                    MieruServer => new MieruController(),
                    _ => new V2rayController()
                };
                var controller = (IServerController)upstreamController;

                controller.Socks5LocalPort = PortHelper.GetAvailablePort(PortType.Both);
                controller.LocalAddress = IPAddress.Loopback.ToString();
                _startedControllers.Add(controller);

                if (upstream == null)
                    upstream = await upstreamController.StartAsync(node, null);
                else
                    upstream = await upstreamController.StartAsync(node, upstream);
            }

            if (upstream == null)
                throw new MessageException("The proxy chain is empty.");

            return upstream;
        }
        catch
        {
            await StopAsync();
            throw;
        }
    }

    public async Task StopAsync()
    {
        for (var index = _startedControllers.Count - 1; index >= 0; index--)
        {
            try
            {
                await _startedControllers[index].StopAsync();
            }
            catch (Exception exception)
            {
                Log.Warning(exception, "Stopping proxy-chain hop {Hop} failed", index + 1);
            }
        }

        _startedControllers.Clear();
    }
}
