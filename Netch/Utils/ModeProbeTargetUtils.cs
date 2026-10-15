namespace Netch.Utils;

public static class ModeProbeTargetUtils
{
    public static bool IsValid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return true;

        var target = value.Trim();
        if (target.StartsWith("[", StringComparison.Ordinal))
        {
            if (!Uri.TryCreate($"tcp://{target}", UriKind.Absolute, out var ipv6Uri))
                return false;

            return ipv6Uri.Port is > 0 and <= ushort.MaxValue &&
                   Uri.CheckHostName(ipv6Uri.Host) == UriHostNameType.IPv6;
        }

        var separator = target.LastIndexOf(':');
        if (separator > 0 && target.IndexOf(':') == separator &&
            ushort.TryParse(target[(separator + 1)..], out var port) && port > 0)
            target = target[..separator];

        return Uri.CheckHostName(target) != UriHostNameType.Unknown;
    }
}
