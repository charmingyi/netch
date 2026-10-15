using Netch.Models;
using Netch.Servers;

namespace Netch.Interfaces;

public interface IServerController : IController
{
    public ushort? Socks5LocalPort { get; set; }

    public string? LocalAddress { get; set; }

    public Task<Socks5Server> StartAsync(Server s);
}

/// <summary>
///     A server controller that can dial its remote proxy through an existing local SOCKS5 hop.
/// </summary>
public interface IUpstreamProxyController : IServerController
{
    Task<Socks5Server> StartAsync(Server server, Socks5Server? upstreamProxy);
}

public static class ServerControllerExtension
{
    public static ushort Socks5LocalPort(this IServerController controller)
    {
        return controller.Socks5LocalPort ?? Global.Settings.Socks5LocalPort;
    }

    public static string LocalAddress(this IServerController controller)
    {
        return controller.LocalAddress ?? Global.Settings.LocalAddress;
    }
}
