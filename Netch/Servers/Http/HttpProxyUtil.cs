using Netch.Interfaces;
using Netch.Models;

namespace Netch.Servers;

public sealed class HttpProxyUtil : IServerUtil
{
    public ushort Priority { get; } = 1;
    public string TypeName { get; } = "HTTP";
    public string FullName { get; } = "HTTP Proxy";
    public string ShortName { get; } = "HTTP";
    public string[] UriScheme { get; } = { "http" };
    public Type ServerType { get; } = typeof(HttpProxyServer);

    public void Edit(Server server) => new HttpProxyForm((HttpProxyServer)server).ShowDialog();

    public void Create() => new HttpProxyForm().ShowDialog();

    public string GetShareLink(Server server)
    {
        var proxy = (HttpProxyServer)server;
        var auth = proxy.Auth()
            ? $"{Uri.EscapeDataString(proxy.Username!)}:{Uri.EscapeDataString(proxy.Password ?? string.Empty)}@"
            : string.Empty;
        var remark = string.IsNullOrEmpty(proxy.Remark) ? string.Empty : $"#{Uri.EscapeDataString(proxy.Remark)}";
        return $"http://{auth}{proxy.Hostname}:{proxy.Port}{remark}";
    }

    public IServerController GetController() => new V2rayController();

    public IEnumerable<Server> ParseUri(string text)
    {
        var schemeEnd = text.IndexOf("://", StringComparison.Ordinal);
        var authority = schemeEnd < 0 ? string.Empty : text[(schemeEnd + 3)..].Split('/', '?', '#')[0];
        var at = authority.LastIndexOf('@');
        var hasPort = authority.LastIndexOf(':') > at;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme != "http" ||
            (!hasPort && string.IsNullOrEmpty(uri.UserInfo)) || uri.Port is < 1 or > ushort.MaxValue)
            throw new FormatException("Invalid HTTP proxy URI");

        var userInfo = uri.UserInfo.Split(':', 2);
        var proxy = new HttpProxyServer
        {
            Hostname = uri.Host,
            Port = checked((ushort)uri.Port),
            Remark = Uri.UnescapeDataString(uri.Fragment.TrimStart('#'))
        };
        if (userInfo.Length > 0 && !string.IsNullOrWhiteSpace(userInfo[0]))
        {
            proxy.Username = Uri.UnescapeDataString(userInfo[0]);
            proxy.Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : string.Empty;
        }

        return new[] { proxy };
    }

    public bool CheckServer(Server server) => server is HttpProxyServer proxy && !string.IsNullOrWhiteSpace(proxy.Hostname) && proxy.Port > 0;
}
