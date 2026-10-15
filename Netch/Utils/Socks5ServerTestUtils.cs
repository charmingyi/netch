using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Netch.Models;
using Netch.Servers;
using Socks5.Models;
using STUN.Client;
using STUN.Enums;
using STUN.Proxy;
using STUN.StunResult;

namespace Netch.Utils;

public static class Socks5ServerTestUtils
{
    public static async Task<int?> TcpConnectThroughSocksAsync(Socks5Server socks5, string targetHost, ushort targetPort,
        CancellationToken ctx = default)
    {
        var stopwatch = Stopwatch.StartNew();
        using var client = new TcpClient();
        await client.ConnectAsync(socks5.Hostname, socks5.Port, ctx);
        await using var stream = client.GetStream();

        var hasAuth = socks5.Auth();
        await stream.WriteAsync(hasAuth ? new byte[] { 5, 2, 0, 2 } : new byte[] { 5, 1, 0 }, ctx);
        var greeting = new byte[2];
        await ReadExactlyAsync(stream, greeting, ctx);
        if (greeting[0] != 5 || greeting[1] == 0xff)
            return null;

        if (greeting[1] == 2)
        {
            if (!hasAuth)
                return null;
            var username = Encoding.UTF8.GetBytes(socks5.Username ?? string.Empty);
            var password = Encoding.UTF8.GetBytes(socks5.Password ?? string.Empty);
            if (username.Length > byte.MaxValue || password.Length > byte.MaxValue)
                return null;
            await stream.WriteAsync(new[] { (byte)1, (byte)username.Length }.Concat(username).Concat(new[] { (byte)password.Length }).Concat(password).ToArray(), ctx);
            var authReply = new byte[2];
            await ReadExactlyAsync(stream, authReply, ctx);
            if (authReply[1] != 0)
                return null;
        }
        else if (greeting[1] != 0)
        {
            return null;
        }

        byte[] address;
        byte addressType;
        if (IPAddress.TryParse(targetHost, out var targetIp))
        {
            address = targetIp.GetAddressBytes();
            addressType = targetIp.AddressFamily == AddressFamily.InterNetwork ? (byte)1 : (byte)4;
        }
        else
        {
            address = Encoding.UTF8.GetBytes(targetHost);
            if (address.Length is 0 or > byte.MaxValue)
                return null;
            addressType = 3;
            address = new[] { (byte)address.Length }.Concat(address).ToArray();
        }

        var request = new List<byte> { 5, 1, 0, addressType };
        request.AddRange(address);
        request.Add((byte)(targetPort >> 8));
        request.Add((byte)(targetPort & 0xff));
        await stream.WriteAsync(request.ToArray(), ctx);

        var reply = new byte[4];
        await ReadExactlyAsync(stream, reply, ctx);
        if (reply[0] != 5 || reply[1] != 0)
            return null;

        var bindLength = reply[3] switch
        {
            1 => 4,
            4 => 16,
            3 => (int)(await ReadByteAsync(stream, ctx)),
            _ => throw new IOException("Invalid SOCKS5 address type")
        };
        var tail = new byte[bindLength + 2];
        await ReadExactlyAsync(stream, tail, ctx);
        stopwatch.Stop();
        return Convert.ToInt32(stopwatch.Elapsed.TotalMilliseconds);
    }

    private static async Task<byte> ReadByteAsync(NetworkStream stream, CancellationToken ctx)
    {
        var oneByte = new byte[1];
        await ReadExactlyAsync(stream, oneByte, ctx);
        return oneByte[0];
    }

    private static async Task ReadExactlyAsync(Stream stream, Memory<byte> buffer, CancellationToken ctx)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer[offset..], ctx);
            if (read == 0)
                throw new EndOfStreamException("The SOCKS5 proxy closed the connection unexpectedly.");
            offset += read;
        }
    }

    public static async Task<NatTypeTestResult> DiscoveryNatTypeAsync(Socks5Server socks5, CancellationToken ctx = default)
    {
        var proxyAddress = await DnsUtils.LookupAsync(socks5.Hostname);
        if (proxyAddress == null)
            return new NatTypeTestResult
            {
                Result = "Unknown",
                BindingTestResult = "Not started",
                FailureDetails = $"Cannot resolve proxy host: {socks5.Hostname}"
            };

        var configuredPort = Global.Settings.STUN_Server_Port is > 0 and <= ushort.MaxValue
            ? (ushort)Global.Settings.STUN_Server_Port
            : (ushort)3478;
        // voipgate.com is the default endpoint used by the maintained pion STUN
        // behavior-discovery tool; it supports the alternate-address tests this
        // NAT classifier needs. Several common WebRTC STUN hosts only do binding.
        var candidates = new List<(string Host, ushort Port)>
        {
            ("stun.voipgate.com", 3478),
            (Global.Settings.STUN_Server, configuredPort)
        };
        try
        {
            foreach (var line in File.ReadLines(Constants.STUNServersFile))
            {
                var parts = line.Split(':', 2, StringSplitOptions.TrimEntries);
                if (parts[0].Length == 0)
                    continue;
                var candidatePort = parts.Length > 1 && ushort.TryParse(parts[1], out var parsedPort) ? parsedPort : (ushort)3478;
                if (candidatePort != 0 && !candidates.Contains((parts[0], candidatePort)))
                    candidates.Add((parts[0], candidatePort));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log.Warning(exception, "Unable to read fallback STUN servers");
        }

        var errors = new List<string>();
        NatTypeTestResult? basicBindingResult = null;
        // Try every configured fallback. A STUN endpoint can resolve successfully
        // but lack RFC 5780 behavior tests, while another server in the list works.
        foreach (var (stunServer, port) in candidates)
        {
            ctx.ThrowIfCancellationRequested();
            Log.Debug("Testing NAT through STUN server {StunServer}:{Port}", stunServer, port);
            var ip = IPAddress.TryParse(stunServer, out var literalAddress)
                ? literalAddress
                : await DnsUtils.LookupAsync(stunServer, timeout: 1500);
            if (ip == null)
            {
                errors.Add($"{stunServer}: DNS lookup failed");
                Log.Debug("Skipping STUN server {StunServer}: DNS lookup failed", stunServer);
                continue;
            }

            var socks5Option = new Socks5CreateOption
            {
                Address = proxyAddress,
                Port = socks5.Port,
                UsernamePassword = new UsernamePassword { UserName = socks5.Username, Password = socks5.Password }
            };
            using IUdpProxy proxy = ProxyFactory.CreateProxy(ProxyType.Socks5, new IPEndPoint(IPAddress.Loopback, 0), socks5Option);
            using var client = new StunClient5389UDP(new IPEndPoint(ip, port), new IPEndPoint(IPAddress.Any, 0), proxy);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ctx);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            try
            {
                await client.ConnectProxyAsync(timeout.Token);
                await client.QueryAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (!ctx.IsCancellationRequested)
            {
                errors.Add($"{stunServer}: timed out");
                continue;
            }
            catch (Exception exception) when (!ctx.IsCancellationRequested)
            {
                errors.Add($"{stunServer}: {exception.Message}");
                continue;
            }
            finally
            {
                try
                {
                    await client.CloseProxyAsync(CancellationToken.None);
                }
                catch (Exception exception) when (exception is SocketException or IOException or InvalidOperationException or OperationCanceledException)
                {
                    Log.Debug(exception, "Closing STUN proxy session failed for {StunServer}", stunServer);
                }
            }

            var res = client.State;
            if (res.BindingTestResult != BindingTestResult.Success)
            {
                errors.Add($"{stunServer}: binding test {res.BindingTestResult}");
                Log.Debug("STUN server {StunServer} did not complete the binding test: {BindingTestResult}", stunServer, res.BindingTestResult);
                continue;
            }

            var simpleResult = GetSimpleResult(res);
            if (simpleResult == "Unknown")
            {
                basicBindingResult ??= new NatTypeTestResult
                {
                    Result = "Unknown",
                    LocalEnd = res.LocalEndPoint?.ToString(),
                    PublicEnd = res.PublicEndPoint?.ToString(),
                    MappingBehavior = res.MappingBehavior.ToString(),
                    FilteringBehavior = res.FilteringBehavior.ToString(),
                    BindingTestResult = res.BindingTestResult.ToString()
                };
                errors.Add($"{stunServer}: STUN binding succeeded, but this server did not provide NAT behavior discovery data");
                continue;
            }

            return new NatTypeTestResult
            {
                Result = simpleResult,
                LocalEnd = res.LocalEndPoint?.ToString(),
                PublicEnd = res.PublicEndPoint?.ToString(),
                MappingBehavior = res.MappingBehavior.ToString(),
                FilteringBehavior = res.FilteringBehavior.ToString(),
                BindingTestResult = res.BindingTestResult.ToString(),
                FailureDetails = null
            };
        }

        if (basicBindingResult is { } basicResult)
        {
            basicResult.FailureDetails = string.Join(Environment.NewLine, errors);
            return basicResult;
        }

        return new NatTypeTestResult
        {
            Result = "Unknown",
            BindingTestResult = "Failed",
            FailureDetails = string.Join(Environment.NewLine, errors)
        };
    }

    private static string GetSimpleResult(StunResult5389 res)
    {
        switch (res.BindingTestResult, res.MappingBehavior, res.FilteringBehavior)
        {
            case (BindingTestResult.Fail, _, _):
                return "NoUDP";
            case (not BindingTestResult.Success, _, _):
                return res.BindingTestResult.ToString();
            case (_, MappingBehavior.Direct or MappingBehavior.EndpointIndependent, FilteringBehavior.EndpointIndependent):
                return "1";
            case (_, MappingBehavior.Direct or MappingBehavior.EndpointIndependent, _):
                return "2";
            case (_, MappingBehavior.AddressDependent or MappingBehavior.AddressAndPortDependent, _):
                return "3";
            case (_, MappingBehavior.Fail, _):
                return "Unknown";
            case (_, _, FilteringBehavior.Fail):
                return "Unknown";
            default:
                return res.FilteringBehavior.ToString();
        }
    }

    public static async Task<int?> HttpConnectAsync(Socks5Server socks5, CancellationToken ctx)
    {
        var socks5Option = new Socks5CreateOption
        {
            Address = await DnsUtils.LookupAsync(socks5.Hostname),
            Port = socks5.Port,
            UsernamePassword = new UsernamePassword
            {
                UserName = socks5.Username,
                Password = socks5.Password
            }
        };

        var stopwatch = Stopwatch.StartNew();

        var result = await Socks5.Utils.Socks5TestUtils.Socks5ConnectAsync(socks5Option, token: ctx);

        stopwatch.Stop();
        if (result)
            return Convert.ToInt32(stopwatch.Elapsed.TotalMilliseconds);

        return null;
    }
}
