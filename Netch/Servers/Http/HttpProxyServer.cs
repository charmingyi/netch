using Netch.Models;

namespace Netch.Servers;

public sealed class HttpProxyServer : Server
{
    public override string Type { get; } = "HTTP";

    public string? Username { get; set; }

    public string? Password { get; set; }

    public bool Auth() => !string.IsNullOrWhiteSpace(Username);

    public override string MaskedData() => Auth() ? "Authentication enabled" : "No authentication";
}
