using System.Net;
using System.Text.Json;
using Netch.Controllers;
using Netch.Interfaces;
using Netch.Models;

namespace Netch.Servers;

public class V2rayController : Guard, IUpstreamProxyController
{
    public V2rayController() : base("v2ray-sn.exe")
    {
        //if (!Global.Settings.V2RayConfig.XrayCone)
        //    Instance.StartInfo.Environment["XRAY_CONE_DISABLED"] = "true";
    }

    protected override IEnumerable<string> StartedKeywords => new[] { "started" };

    protected override IEnumerable<string> FailedKeywords => new[] { "config file not readable", "failed to" };

    public override string Name => "V2Ray (SagerNet)";

    public ushort? Socks5LocalPort { get; set; }

    public string? LocalAddress { get; set; }

    public virtual Task<Socks5Server> StartAsync(Server s) => StartAsync(s, null);

    public virtual async Task<Socks5Server> StartAsync(Server s, Socks5Server? upstreamProxy)
    {
        await using (var fileStream = new FileStream(Constants.TempConfig, FileMode.Create, FileAccess.Write, FileShare.Read))
        {
            var config = s is ProxyChainServer chain
                ? await V2rayConfigUtils.GenerateClientConfigAsync(chain.Nodes)
                : await V2rayConfigUtils.GenerateClientConfigAsync(s, this.Socks5LocalPort(), this.LocalAddress(), upstreamProxy);
            await JsonSerializer.SerializeAsync(fileStream, config, Global.NewCustomJsonSerializerOptions());
        }

        await StartGuardAsync("run -c ..\\data\\last.json");
        if (this.LocalAddress() == IPAddress.Loopback.ToString())
            await SingBoxController.WaitForListenerAsync(this.Socks5LocalPort(), Name, IPAddress.Loopback);
        return new Socks5Server(IPAddress.Loopback.ToString(), this.Socks5LocalPort(), s.Hostname);
    }
}
