using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Netch.Controllers;
using Netch.Forms;
using Netch.Interfaces;
using Netch.Models;
using Netch.Utils;

namespace Netch.Servers;

public sealed class Hysteria2Server : Server
{
    public override string Type { get; } = "Hysteria2";
    public string Password { get; set; } = string.Empty;
    public string ServerName { get; set; } = string.Empty;
    public bool AllowInsecure { get; set; }
    public int UpMbps { get; set; }
    public int DownMbps { get; set; }
    public string ObfsType { get; set; } = string.Empty;
    public string ObfsPassword { get; set; } = string.Empty;
    public override string MaskedData() => "Hysteria 2 / QUIC";
}

public sealed class AnyTlsServer : Server
{
    public override string Type { get; } = "AnyTLS";
    public string Password { get; set; } = string.Empty;
    public string ServerName { get; set; } = string.Empty;
    public bool AllowInsecure { get; set; }
    public override string MaskedData() => "AnyTLS / TLS";
}

public sealed class MieruServer : Server
{
    public override string Type { get; } = "Mieru";
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Transport { get; set; } = "TCP";
    public string Multiplexing { get; set; } = "MULTIPLEXING_HIGH";
    public override string MaskedData() => $"mieru {Transport}";
}

public sealed class Hysteria2Util : ModernProtocolUtil<Hysteria2Server>
{
    public override ushort Priority => 11;
    public override string TypeName => "Hysteria2";
    public override string FullName => "Hysteria 2";
    public override string ShortName => "HY2";
    public override string[] UriScheme => new[] { "hysteria2", "hy2" };
    protected override ServerForm CreateForm(Hysteria2Server server) => new Hysteria2Form(server);
    public override bool CheckServer(Server server) => base.CheckServer(server) && server is Hysteria2Server node && !string.IsNullOrWhiteSpace(node.Password);
    public override string GetShareLink(Server server)
    {
        var node = (Hysteria2Server)server;
        var query = new List<string>();
        Add(query, "sni", node.ServerName);
        Add(query, "insecure", node.AllowInsecure ? "1" : null);
        Add(query, "upmbps", node.UpMbps > 0 ? node.UpMbps.ToString() : null);
        Add(query, "downmbps", node.DownMbps > 0 ? node.DownMbps.ToString() : null);
        Add(query, "obfs", node.ObfsType);
        Add(query, "obfs-password", node.ObfsPassword);
        return $"hysteria2://{Uri.EscapeDataString(node.Password)}@{FormatHost(node.Hostname)}:{node.Port}/{(query.Count > 0 ? "?" + string.Join("&", query) : "")}{Fragment(node.Remark)}";
    }

    protected override Hysteria2Server Parse(Uri uri)
    {
        var query = ParseQuery(uri.Query);
        return new Hysteria2Server
        {
            Hostname = uri.Host,
            Port = RequiredPort(uri),
            Password = Uri.UnescapeDataString(uri.UserInfo),
            ServerName = Get(query, "sni"),
            AllowInsecure = Get(query, "insecure") is "1" or "true",
            UpMbps = ParseInt(Get(query, "upmbps")),
            DownMbps = ParseInt(Get(query, "downmbps")),
            ObfsType = Get(query, "obfs"),
            ObfsPassword = Get(query, "obfs-password"),
            Remark = Uri.UnescapeDataString(uri.Fragment.TrimStart('#'))
        };
    }

    public override IServerController GetController() => new SingBoxController();
}

public sealed class AnyTlsUtil : ModernProtocolUtil<AnyTlsServer>
{
    public override ushort Priority => 12;
    public override string TypeName => "AnyTLS";
    public override string FullName => "AnyTLS";
    public override string ShortName => "ATLS";
    public override string[] UriScheme => new[] { "anytls" };
    protected override ServerForm CreateForm(AnyTlsServer server) => new AnyTlsForm(server);
    public override bool CheckServer(Server server) => base.CheckServer(server) && server is AnyTlsServer node && !string.IsNullOrWhiteSpace(node.Password);
    public override string GetShareLink(Server server)
    {
        var node = (AnyTlsServer)server;
        var query = new List<string>();
        Add(query, "sni", node.ServerName);
        Add(query, "insecure", node.AllowInsecure ? "1" : null);
        return $"anytls://{Uri.EscapeDataString(node.Password)}@{FormatHost(node.Hostname)}:{node.Port}/{(query.Count > 0 ? "?" + string.Join("&", query) : "")}{Fragment(node.Remark)}";
    }

    protected override AnyTlsServer Parse(Uri uri)
    {
        var query = ParseQuery(uri.Query);
        return new AnyTlsServer
        {
            Hostname = uri.Host,
            Port = RequiredPort(uri),
            Password = Uri.UnescapeDataString(uri.UserInfo),
            ServerName = Get(query, "sni"),
            AllowInsecure = Get(query, "insecure") is "1" or "true",
            Remark = Uri.UnescapeDataString(uri.Fragment.TrimStart('#'))
        };
    }

    public override IServerController GetController() => new SingBoxController();
}

public sealed class MieruUtil : ModernProtocolUtil<MieruServer>
{
    public override ushort Priority => 13;
    public override string TypeName => "Mieru";
    public override string FullName => "mieru";
    public override string ShortName => "MIERU";
    public override string[] UriScheme => new[] { "mierus", "mieru" };
    protected override ServerForm CreateForm(MieruServer server) => new MieruForm(server);
    public override bool CheckServer(Server server) => base.CheckServer(server) && server is MieruServer node &&
        !string.IsNullOrWhiteSpace(node.Username) && !string.IsNullOrWhiteSpace(node.Password) &&
        node.Transport is "TCP" or "UDP";
    public override string GetShareLink(Server server)
    {
        var node = (MieruServer)server;
        var query = new[]
        {
            $"profile=netch",
            $"multiplexing={Uri.EscapeDataString(node.Multiplexing)}",
            $"port={node.Port}",
            $"protocol={Uri.EscapeDataString(node.Transport)}"
        };
        return $"mierus://{Uri.EscapeDataString(node.Username)}:{Uri.EscapeDataString(node.Password)}@{FormatHost(node.Hostname)}?{string.Join("&", query)}{Fragment(node.Remark)}";
    }

    protected override MieruServer Parse(Uri uri)
    {
        var queryParts = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .Where(pair => pair.Length == 2)
            .Select(pair => (Key: Uri.UnescapeDataString(pair[0]), Value: Uri.UnescapeDataString(pair[1])))
            .ToArray();
        var query = queryParts.GroupBy(part => part.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last().Value, StringComparer.OrdinalIgnoreCase);
        var legacyLink = uri.Scheme.Equals("mieru", StringComparison.OrdinalIgnoreCase) && query.ContainsKey("username");
        var userInfo = uri.UserInfo.Split(':', 2);
        var portText = queryParts.FirstOrDefault(part => part.Key.Equals("port", StringComparison.OrdinalIgnoreCase)).Value;
        var transport = queryParts.FirstOrDefault(part => part.Key.Equals("protocol", StringComparison.OrdinalIgnoreCase)).Value;
        return new MieruServer
        {
            Hostname = uri.Host,
            Port = legacyLink ? RequiredPort(uri) : ushort.TryParse(portText, out var parsedPort) && parsedPort > 0 ? parsedPort : RequiredPort(uri),
            Username = legacyLink ? Get(query, "username") : userInfo.Length > 0 ? Uri.UnescapeDataString(userInfo[0]) : string.Empty,
            Password = legacyLink ? Get(query, "password") : userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : string.Empty,
            Transport = (legacyLink ? Get(query, "network") : transport).ToUpperInvariant() is "UDP" ? "UDP" : "TCP",
            Multiplexing = Get(query, "multiplexing") is { Length: > 0 } multiplexing ? multiplexing : "MULTIPLEXING_HIGH",
            Remark = Uri.UnescapeDataString(uri.Fragment.TrimStart('#'))
        };
    }

    public override IServerController GetController() => new MieruController();
}

public abstract class ModernProtocolUtil<TServer> : IServerUtil where TServer : Server, new()
{
    public abstract ushort Priority { get; }
    public abstract string TypeName { get; }
    public abstract string FullName { get; }
    public abstract string ShortName { get; }
    public abstract string[] UriScheme { get; }
    public Type ServerType => typeof(TServer);

    public void Edit(Server server) => CreateForm((TServer)server).ShowDialog();
    public void Create() => CreateForm(new TServer()).ShowDialog();
    protected abstract ServerForm CreateForm(TServer server);
    protected abstract TServer Parse(Uri uri);
    public abstract string GetShareLink(Server server);
    public abstract IServerController GetController();
    public IEnumerable<Server> ParseUri(string text)
    {
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || !UriScheme.Contains(uri.Scheme, StringComparer.OrdinalIgnoreCase))
            throw new FormatException($"Invalid {FullName} URI");
        return new[] { Parse(uri) };
    }

    public virtual bool CheckServer(Server server) => server is TServer node && !string.IsNullOrWhiteSpace(node.Hostname) && node.Port != 0;

    protected static ushort RequiredPort(Uri uri) => uri.Port is > 0 and <= ushort.MaxValue ? checked((ushort)uri.Port) : throw new FormatException("A valid server port is required.");
    protected static string FormatHost(string host) => IPAddress.TryParse(host, out var address) && address.AddressFamily == AddressFamily.InterNetworkV6 ? $"[{host}]" : host;
    protected static string Fragment(string value) => string.IsNullOrWhiteSpace(value) ? string.Empty : $"#{Uri.EscapeDataString(value)}";
    protected static void Add(List<string> query, string key, string? value)
    {
        if (!string.IsNullOrEmpty(value)) query.Add($"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value)}");
    }
    protected static Dictionary<string, string> ParseQuery(string query) => query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
        .Select(part => part.Split('=', 2)).Where(pair => pair.Length == 2)
        .ToDictionary(pair => Uri.UnescapeDataString(pair[0]), pair => Uri.UnescapeDataString(pair[1]), StringComparer.OrdinalIgnoreCase);
    protected static string Get(Dictionary<string, string> query, string key) => query.GetValueOrDefault(key) ?? string.Empty;
    protected static int ParseInt(string value) => int.TryParse(value, out var parsed) && parsed > 0 ? parsed : 0;
}

public sealed class Hysteria2Form : ServerForm
{
    public Hysteria2Form(Hysteria2Server? server = null)
    {
        server ??= new Hysteria2Server(); Server = server;
        CreateTextBox("Password", "Password", _ => true, value => server.Password = value, server.Password);
        CreateTextBox("ServerName", "TLS server name", _ => true, value => server.ServerName = value, server.ServerName);
        CreateCheckBox("AllowInsecure", "Allow insecure TLS", value => server.AllowInsecure = value, server.AllowInsecure);
        CreateTextBox("UpMbps", "Upload Mbps (0=auto)", value => int.TryParse(value, out var parsed) && parsed >= 0, value => server.UpMbps = int.Parse(value), server.UpMbps.ToString());
        CreateTextBox("DownMbps", "Download Mbps (0=auto)", value => int.TryParse(value, out var parsed) && parsed >= 0, value => server.DownMbps = int.Parse(value), server.DownMbps.ToString());
        CreateComboBox("Obfs", "Obfuscation", new() { "", "salamander" }, value => server.ObfsType = value, server.ObfsType);
        CreateTextBox("ObfsPassword", "Obfuscation password", _ => true, value => server.ObfsPassword = value, server.ObfsPassword);
    }
    protected override string TypeName => "Hysteria 2";
}

public sealed class AnyTlsForm : ServerForm
{
    public AnyTlsForm(AnyTlsServer? server = null)
    {
        server ??= new AnyTlsServer(); Server = server;
        CreateTextBox("Password", "Password", _ => true, value => server.Password = value, server.Password);
        CreateTextBox("ServerName", "TLS server name", _ => true, value => server.ServerName = value, server.ServerName);
        CreateCheckBox("AllowInsecure", "Allow insecure TLS", value => server.AllowInsecure = value, server.AllowInsecure);
    }
    protected override string TypeName => "AnyTLS";
}

public sealed class MieruForm : ServerForm
{
    public MieruForm(MieruServer? server = null)
    {
        server ??= new MieruServer(); Server = server;
        CreateTextBox("Username", "Username", value => value.Length > 0, value => server.Username = value, server.Username);
        CreateTextBox("Password", "Password", value => value.Length > 0, value => server.Password = value, server.Password);
        CreateComboBox("Transport", "Transport", new() { "TCP", "UDP" }, value => server.Transport = value, server.Transport);
        CreateComboBox("Multiplexing", "Multiplexing", new() { "MULTIPLEXING_OFF", "MULTIPLEXING_LOW", "MULTIPLEXING_MIDDLE", "MULTIPLEXING_HIGH" }, value => server.Multiplexing = value, server.Multiplexing);
    }
    protected override string TypeName => "mieru";
}

public sealed class SingBoxController : Guard, IUpstreamProxyController
{
    public SingBoxController() : base("sing-box.exe") { }
    public override string Name => "sing-box";
    public ushort? Socks5LocalPort { get; set; }
    public string? LocalAddress { get; set; }
    public Task<Socks5Server> StartAsync(Server server) => StartAsync(server, null);

    public async Task<Socks5Server> StartAsync(Server server, Socks5Server? upstreamProxy)
    {
        var port = this.Socks5LocalPort();
        var listen = this.LocalAddress();
        var outbound = CreateOutbound(server);
        var outbounds = new List<object> { outbound };
        if (upstreamProxy != null)
        {
            var upstream = new Dictionary<string, object?>
            {
                ["type"] = "socks",
                ["tag"] = "netch-upstream",
                ["server"] = upstreamProxy.Hostname,
                ["server_port"] = upstreamProxy.Port,
                ["version"] = "5"
            };
            if (upstreamProxy.Auth())
            {
                upstream["username"] = upstreamProxy.Username;
                upstream["password"] = upstreamProxy.Password;
            }
            outbounds.Add(upstream);
            outbound["detour"] = "netch-upstream";
        }

        var config = new
        {
            log = new { level = "warn", timestamp = true },
            inbounds = new[] { new { type = "socks", tag = "netch-in", listen, listen_port = port, udp = true } },
            outbounds,
            route = new { final = outbound["tag"] }
        };
        await File.WriteAllTextAsync(Constants.TempConfig, JsonSerializer.Serialize(config, Global.NewCustomJsonSerializerOptions()));
        await StartGuardAsync("run -c ..\\data\\last.json");
        await WaitForListenerAsync(port, "sing-box");
        var remote = server.Hostname;
        return new Socks5Server(IPAddress.Loopback.ToString(), port, remote);
    }

    private static Dictionary<string, object?> CreateOutbound(Server server)
    {
        var common = new Dictionary<string, object?> { ["tag"] = "netch-proxy", ["server"] = server.Hostname, ["server_port"] = server.Port };
        switch (server)
        {
            case Hysteria2Server hy2:
                common["type"] = "hysteria2";
                common["password"] = hy2.Password;
                common["up_mbps"] = hy2.UpMbps == 0 ? null : hy2.UpMbps;
                common["down_mbps"] = hy2.DownMbps == 0 ? null : hy2.DownMbps;
                common["tls"] = new { enabled = true, server_name = string.IsNullOrWhiteSpace(hy2.ServerName) ? hy2.Hostname : hy2.ServerName, insecure = hy2.AllowInsecure };
                if (!string.IsNullOrWhiteSpace(hy2.ObfsType)) common["obfs"] = new { type = hy2.ObfsType, password = hy2.ObfsPassword };
                break;
            case AnyTlsServer anytls:
                common["type"] = "anytls";
                common["password"] = anytls.Password;
                common["tls"] = new { enabled = true, server_name = string.IsNullOrWhiteSpace(anytls.ServerName) ? anytls.Hostname : anytls.ServerName, insecure = anytls.AllowInsecure };
                break;
            case ShadowsocksServer shadowsocks when SSGlobal.Is2022Method(shadowsocks.EncryptMethod):
                common["type"] = "shadowsocks";
                common["method"] = shadowsocks.EncryptMethod;
                common["password"] = shadowsocks.Password;
                break;
            default:
                throw new NotSupportedException($"sing-box does not support {server.Type} nodes.");
        }

        return common;
    }

    internal static async Task WaitForListenerAsync(ushort port, string controllerName, IPAddress? listenAddress = null)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        Exception? lastError = null;
        var endpointAddress = listenAddress ?? IPAddress.Loopback;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(endpointAddress, port).WaitAsync(TimeSpan.FromMilliseconds(400));
                return;
            }
            catch (Exception exception) when (exception is SocketException or TimeoutException)
            {
                lastError = exception;
                await Task.Delay(150);
            }
        }

        throw new MessageException($"{controllerName} did not open the local SOCKS5 port {port}: {lastError?.Message}");
    }
}

public sealed class MieruController : Guard, IUpstreamProxyController
{
    public MieruController() : base("mieru.exe") { }
    public override string Name => "mieru";
    public ushort? Socks5LocalPort { get; set; }
    public string? LocalAddress { get; set; }
    public Task<Socks5Server> StartAsync(Server server) => StartAsync(server, null);

    public async Task<Socks5Server> StartAsync(Server server, Socks5Server? upstreamProxy)
    {
        var node = (MieruServer)server;
        var port = this.Socks5LocalPort();
        var resolvedAddress = await DnsUtils.LookupAsync(node.Hostname) ?? throw new MessageException($"Unable to resolve mieru server {node.Hostname}");
        var serverConfig = IPAddress.TryParse(node.Hostname, out _)
            ? new Dictionary<string, object?> { ["ipAddress"] = node.Hostname, ["portBindings"] = new[] { new { port = (int)node.Port, protocol = node.Transport } } }
            : new Dictionary<string, object?> { ["ipAddress"] = resolvedAddress.ToString(), ["domainName"] = node.Hostname, ["portBindings"] = new[] { new { port = (int)node.Port, protocol = node.Transport } } };
        var profile = new Dictionary<string, object?>
        {
            ["profileName"] = "netch",
            ["user"] = new { name = node.Username, password = node.Password },
            ["servers"] = new[] { serverConfig },
            ["multiplexing"] = new { level = node.Multiplexing },
            ["loggingLevel"] = "ERROR"
        };
        if (upstreamProxy != null)
        {
            var dialer = new Dictionary<string, object?>
            {
                ["protocol"] = "SOCKS5_PROXY_PROTOCOL",
                ["host"] = upstreamProxy.Hostname,
                ["port"] = (int)upstreamProxy.Port,
                ["socks5UDPAssociate"] = true
            };
            if (upstreamProxy.Auth())
                dialer["socks5Authentication"] = new { user = upstreamProxy.Username, password = upstreamProxy.Password };
            profile["dialer"] = dialer;
        }

        var rpcPort = PortHelper.GetAvailablePort(PortType.TCP);
        if (rpcPort == port)
            rpcPort = PortHelper.GetAvailablePort(PortType.TCP);
        var config = new { profiles = new[] { profile }, activeProfile = "netch", rpcPort = (int)rpcPort, socks5Port = (int)port, socks5ListenLAN = false, loggingLevel = "ERROR" };
        var configPath = Path.Combine(Global.NetchDir, "data", "mieru-client.json");
        await File.WriteAllTextAsync(configPath, JsonSerializer.Serialize(config, Global.NewCustomJsonSerializerOptions()));
        Instance.StartInfo.Environment["MIERU_CONFIG_JSON_FILE"] = configPath;
        await StartGuardAsync("run");
        await SingBoxController.WaitForListenerAsync(port, "mieru");
        return new Socks5Server(IPAddress.Loopback.ToString(), port, server.Hostname);
    }
}
