using Netch.Interfaces;
using Netch.Models;

namespace Netch.Servers;

public class Socks5Util : IServerUtil
{
    public ushort Priority { get; } = 0;

    public string TypeName { get; } = "SOCKS";

    public string FullName { get; } = "SOCKS";

    public string ShortName { get; } = "SOCKS";

    public string[] UriScheme { get; } = { "socks", "socks4", "socks4a", "socks5" };

    public Type ServerType { get; } = typeof(Socks5Server);

    public void Edit(Server s)
    {
        new Socks5Form((Socks5Server)s).ShowDialog();
    }

    public void Create()
    {
        new Socks5Form().ShowDialog();
    }

    public string GetShareLink(Server s)
    {
        var server = (Socks5Server)s;
        if (server.Version is "4" or "4a")
        {
            var auth = string.IsNullOrWhiteSpace(server.Username) ? string.Empty : $"{Uri.EscapeDataString(server.Username)}@";
            return $"socks{server.Version}://{auth}{server.Hostname}:{server.Port}#{Uri.EscapeDataString(server.Remark)}";
        }

        // https://t.me/socks?server=1.1.1.1&port=443
        return $"https://t.me/socks?server={server.Hostname}&port={server.Port}" +
               $"{(!string.IsNullOrWhiteSpace(server.Username) ? $"&user={Uri.EscapeDataString(server.Username)}" : "")}" +
               $"{(server.Auth() ? $"&pass={Uri.EscapeDataString(server.Password!)}" : "")}";
    }

    public IServerController GetController()
    {
        return new Socks5Controller();
    }

    public IEnumerable<Server> ParseUri(string text)
    {
        if (Uri.TryCreate(text, UriKind.Absolute, out var uri) &&
            uri.Scheme is "socks" or "socks4" or "socks4a" or "socks5")
        {
            var credentials = uri.UserInfo.Split(':', 2);
            var proxy = new Socks5Server
            {
                Hostname = uri.Host,
                Port = checked((ushort)uri.Port),
                Version = uri.Scheme switch
                {
                    "socks4" => "4",
                    "socks4a" => "4a",
                    _ => "5"
                },
                Remark = Uri.UnescapeDataString(uri.Fragment.TrimStart('#'))
            };
            if (credentials.Length > 0 && !string.IsNullOrWhiteSpace(credentials[0]))
            {
                proxy.Username = Uri.UnescapeDataString(credentials[0]);
                proxy.Password = credentials.Length > 1 ? Uri.UnescapeDataString(credentials[1]) : string.Empty;
            }

            return new[] { proxy };
        }

        var dict = text.Replace("tg://socks?", "")
            .Replace("https://t.me/socks?", "")
            .Split('&')
            .Select(str => str.Split('='))
            .ToDictionary(splited => splited[0], splited => splited[1]);

        if (!dict.ContainsKey("server") || !dict.ContainsKey("port"))
            throw new FormatException();

        var data = new Socks5Server
        {
            Hostname = dict["server"],
            Port = ushort.Parse(dict["port"])
        };

        if (dict.ContainsKey("user") && !string.IsNullOrWhiteSpace(dict["user"]))
            data.Username = dict["user"];

        if (dict.ContainsKey("pass") && !string.IsNullOrWhiteSpace(dict["pass"]))
            data.Password = dict["pass"];

        return new[] { data };
    }

    public bool CheckServer(Server s)
    {
        return true;
    }
}
