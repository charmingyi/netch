using Netch.Forms;
using Netch.Utils;

namespace Netch.Servers;

[Fody.ConfigureAwait(true)]
public sealed class HttpProxyForm : ServerForm
{
    public HttpProxyForm(HttpProxyServer? server = null)
    {
        server ??= new HttpProxyServer();
        Server = server;
        CreateTextBox("Username", "Username", _ => true, value => server.Username = value, server.Username.ValueOrDefault());
        CreateTextBox("Password", "Password", _ => true, value => server.Password = value, server.Password.ValueOrDefault());
    }

    protected override string TypeName { get; } = "HTTP Proxy";
}
