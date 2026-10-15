using System.Net;
using System.Text;
using Netch.Controllers;
using Netch.Interfaces;
using Netch.Models;

namespace Netch.Servers;

/// <summary>Uses sing-box for SIP022 and the legacy Shadowsocks client for older methods.</summary>
public sealed class AdaptiveShadowsocksController : IServerController
{
    private IServerController? _controller;

    public string Name => _controller?.Name ?? "Shadowsocks";
    public ushort? Socks5LocalPort { get; set; }
    public string? LocalAddress { get; set; }

    public async Task<Socks5Server> StartAsync(Server server)
    {
        if (server is not ShadowsocksServer node)
            throw new ArgumentException("Expected a Shadowsocks server", nameof(server));

        _controller = SSGlobal.Is2022Method(node.EncryptMethod)
            ? new SingBoxController()
            : new ShadowsocksController();
        _controller.Socks5LocalPort = Socks5LocalPort;
        _controller.LocalAddress = LocalAddress;
        try
        {
            return await _controller.StartAsync(server);
        }
        catch
        {
            _controller = null;
            throw;
        }
    }

    public async Task StopAsync()
    {
        if (_controller != null)
        {
            await _controller.StopAsync();
            _controller = null;
        }
    }
}

public class ShadowsocksController : Guard, IServerController
{
    public ShadowsocksController() : base("Shadowsocks.exe", encoding: Encoding.UTF8)
    {
    }

    protected override IEnumerable<string> StartedKeywords => new[] { "listening on" };

    protected override IEnumerable<string> FailedKeywords => new[] { "error", "failed to start plguin" };

    public override string Name => "Shadowsocks";

    public ushort? Socks5LocalPort { get; set; }

    public string? LocalAddress { get; set; }

    public async Task<Socks5Server> StartAsync(Server s)
    {
        var server = (ShadowsocksServer)s;

        var arguments = new object?[]
        {
            "-s", $"{await server.AutoResolveHostnameAsync()}:{server.Port}",
            "-b", $"{this.LocalAddress()}:{this.Socks5LocalPort()}",
            "-m", server.EncryptMethod,
            "-k", server.Password,
            "--plugin", server.Plugin,
            "--plugin-opts", server.PluginOption,
            "-U", SpecialArgument.Flag
        };

        await StartGuardAsync(Arguments.Format(arguments));
        return new Socks5Server(IPAddress.Loopback.ToString(), this.Socks5LocalPort(), server.Hostname);
    }
}
