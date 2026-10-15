using System.Diagnostics;
using System.Net;

namespace Netch.Utils;

public sealed record GameTrafficSnapshot(
    string GameName,
    string Endpoint,
    IPAddress RemoteAddress,
    ushort RemotePort,
    int ReceivePacketsPerSecond,
    int SendPacketsPerSecond,
    double? EstimatedReceiveLossPercent,
    int ReceiveStalls);

/// <summary>
/// Tracks the selected game's real UDP endpoints from Windows kernel ETW events.
/// The loss percentage is estimated from gaps in the server's incoming packet stream;
/// it is not a game-protocol sequence/acknowledgement measurement.
/// </summary>
public static class GameTrafficMonitor
{
    private static readonly object Sync = new();
    private static readonly Dictionary<string, Flow> Flows = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<int, string> ProcessGameIds = new();
    private static readonly HashSet<int> NonGameProcessIds = new();
    private static string _gameId = string.Empty;

    public static bool IsActive
    {
        get
        {
            lock (Sync)
                return _gameId.Length > 0;
        }
    }

    public static void Start(string gameId)
    {
        lock (Sync)
        {
            _gameId = gameId.Trim();
            Flows.Clear();
            ProcessGameIds.Clear();
            NonGameProcessIds.Clear();
        }
    }

    public static void Stop()
    {
        lock (Sync)
        {
            _gameId = string.Empty;
            Flows.Clear();
            ProcessGameIds.Clear();
            NonGameProcessIds.Clear();
        }
    }

    public static void Record(int processId, string? processName, IPAddress? remoteAddress, int remotePort, bool received)
    {
        if (remoteAddress == null || IPAddress.IsLoopback(remoteAddress) || remotePort is <= 0 or > ushort.MaxValue || remotePort == 53)
            return;

        var gameId = GetGameIdForProcess(processName ?? string.Empty);
        if (gameId.Length == 0)
        {
            lock (Sync)
            {
                if (ProcessGameIds.TryGetValue(processId, out var cachedGameId))
                    gameId = cachedGameId;
                else if (NonGameProcessIds.Contains(processId))
                    return;
            }

            if (gameId.Length == 0)
            {
                try
                {
                    using var process = Process.GetProcessById(processId);
                    gameId = GetGameIdForProcess(process.ProcessName);
                }
                catch (ArgumentException)
                {
                    // The process may exit before its queued ETW event is handled.
                }
                catch (InvalidOperationException)
                {
                    // The process may exit before its queued ETW event is handled.
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    // Protected or already-exited processes are not candidates for monitoring.
                }

                lock (Sync)
                {
                    if (gameId.Length > 0)
                        ProcessGameIds[processId] = gameId;
                    else
                        NonGameProcessIds.Add(processId);
                }
            }
        }

        if (gameId.Length == 0)
            return;

        var endpoint = $"{remoteAddress}:{remotePort}";
        var now = Stopwatch.GetTimestamp();
        lock (Sync)
        {
            if (!string.Equals(gameId, _gameId, StringComparison.OrdinalIgnoreCase))
                return;

            if (!Flows.TryGetValue(endpoint, out var flow))
                Flows.Add(endpoint, flow = new Flow(remoteAddress, remotePort));

            (received ? flow.ReceiveTimes : flow.SendTimes).Enqueue(now);
            Prune(flow.ReceiveTimes, now);
            Prune(flow.SendTimes, now);
        }
    }

    public static GameTrafficSnapshot? GetSnapshot()
    {
        lock (Sync)
        {
            if (_gameId.Length == 0)
                return null;

            var now = Stopwatch.GetTimestamp();
            var active = Flows.Values
                .Select(flow => new
                {
                    Flow = flow,
                    Receive = CountRecent(flow.ReceiveTimes, now, 5),
                    Send = CountRecent(flow.SendTimes, now, 5),
                    LastReceive = flow.ReceiveTimes.LastOrDefault()
                })
                .Where(item => item.Receive > 0 || item.Send > 0)
                .OrderByDescending(item => item.Receive)
                .ThenByDescending(item => item.Send)
                .FirstOrDefault();

            if (active == null)
                return null;

            var loss = EstimateReceiveLoss(active.Flow.ReceiveTimes, now, out var stalls);
            return new GameTrafficSnapshot(
                GetGameDisplayName(_gameId),
                active.Flow.Endpoint,
                active.Flow.Address,
                checked((ushort)active.Flow.Port),
                active.Receive / 5,
                active.Send / 5,
                loss,
                stalls);
        }
    }

    private static string GetGameIdForProcess(string processName)
    {
        var name = Path.GetFileNameWithoutExtension(processName);
        return name.ToLowerInvariant() switch
        {
            "efootball" => "efootball",
            "diablo iv" => "diablo4",
            "bf6" => "battlefield6",
            _ => string.Empty
        };
    }

    private static string GetGameDisplayName(string gameId) => gameId.ToLowerInvariant() switch
    {
        "efootball" => "eFootball",
        "diablo4" => "暗黑破坏神 IV",
        "battlefield6" => "战地 6",
        _ => gameId
    };

    private static void Prune(Queue<long> times, long now)
    {
        var oldest = now - (long)(Stopwatch.Frequency * 30d);
        while (times.TryPeek(out var timestamp) && timestamp < oldest)
            times.Dequeue();
    }

    private static int CountRecent(Queue<long> times, long now, int seconds)
    {
        var oldest = now - (long)(Stopwatch.Frequency * seconds);
        return times.Count(timestamp => timestamp >= oldest);
    }

    private static double? EstimateReceiveLoss(Queue<long> receiveTimes, long now, out int stalls)
    {
        const int windowSeconds = 10;
        var oldest = now - (long)(Stopwatch.Frequency * windowSeconds);
        var recent = receiveTimes.Where(timestamp => timestamp >= oldest).ToArray();
        stalls = 0;
        if (recent.Length < 30)
            return null;

        var intervals = new double[recent.Length - 1];
        for (var index = 1; index < recent.Length; index++)
            intervals[index - 1] = (recent[index] - recent[index - 1]) * 1000d / Stopwatch.Frequency;

        // Estimate the normal game update cadence from short intervals. Exclude long gaps
        // so a burst of stalls does not redefine the baseline as the normal rate.
        var normalIntervals = intervals.Where(milliseconds => milliseconds is >= 2 and <= 150).OrderBy(value => value).ToArray();
        if (normalIntervals.Length < 20)
            return null;

        var baseline = normalIntervals[normalIntervals.Length / 2];
        var estimatedMissing = 0;
        foreach (var interval in intervals)
        {
            if (interval > Math.Max(150, baseline * 2.5))
                stalls++;

            if (interval > Math.Max(80, baseline * 1.8))
                estimatedMissing += Math.Max(0, (int)Math.Round(interval / baseline, MidpointRounding.AwayFromZero) - 1);
        }

        return estimatedMissing * 100d / (intervals.Length + estimatedMissing);
    }

    private sealed class Flow
    {
        public Flow(IPAddress address, int port)
        {
            Address = address;
            Port = port;
        }

        public IPAddress Address { get; }
        public int Port { get; }
        public string Endpoint => Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
            ? $"[{Address}]:{Port}"
            : $"{Address}:{Port}";
        public Queue<long> ReceiveTimes { get; } = new();
        public Queue<long> SendTimes { get; } = new();
    }
}
