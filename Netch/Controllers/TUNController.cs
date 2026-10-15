using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Netch.Interfaces;
using Netch.Interops;
using Netch.Models;
using Netch.Models.Modes;
using Netch.Models.Modes.TunMode;
using Netch.Servers;
using Netch.Utils;
using static Netch.Interops.tun2socks;

namespace Netch.Controllers
{
    public class TUNController : IModeController
    {
        private readonly DNSController _aioDnsController = new();

        private TunMode _mode = null!;
        private IPAddress? _serverRemoteAddress;
        private TUNConfig _tunConfig = null!;

        private NetRoute _tun;
        private NetRoute _outbound;
        private bool _initialized;
        private bool _dnsStarted;
        private bool _routesMayExist;
        private NetworkInterface? _tunNetworkInterface;
        private IPAddress[] _originalTunDns = Array.Empty<IPAddress>();
        private bool _customDnsApplied;

        public string Name => "tun2socks";
        public string interfaceName => "netch";

        public ModeFeature Features => ModeFeature.SupportSocks5Auth;

        public async Task StartAsync(Socks5Server server, Mode mode)
        {
            if (mode is not TunMode tunMode)
                throw new InvalidOperationException();

            _mode = tunMode;
            _tunConfig = Global.Settings.TUNTAP;

            if (server.RemoteHostname.ValueOrDefault() != null)
                _serverRemoteAddress = await DnsUtils.LookupAsync(server.RemoteHostname!);
            else
                _serverRemoteAddress = await DnsUtils.LookupAsync(server.Hostname);

            if (_serverRemoteAddress != null && IPAddress.IsLoopback(_serverRemoteAddress))
                _serverRemoteAddress = null;

            _outbound = NetRoute.GetBestRouteTemplate();
            CheckDriver();

            Dial(NameList.TYPE_ADAPMTU, "1500");
            Dial(NameList.TYPE_BYPBIND, _outbound.Gateway);
            Dial(NameList.TYPE_BYPLIST, "disabled");

            #region Server

            Dial(NameList.TYPE_TCPREST, "");
            Dial(NameList.TYPE_TCPTYPE, "Socks5");

            Dial(NameList.TYPE_UDPREST, "");
            Dial(NameList.TYPE_UDPTYPE, "Socks5");

            Dial(NameList.TYPE_TCPHOST, $"{await server.AutoResolveHostnameAsync()}:{server.Port}");

            Dial(NameList.TYPE_UDPHOST, $"{await server.AutoResolveHostnameAsync()}:{server.Port}");

            if (server.Auth())
            {
                Dial(NameList.TYPE_TCPUSER, server.Username!);
                Dial(NameList.TYPE_TCPPASS, server.Password!);

                Dial(NameList.TYPE_UDPUSER, server.Username!);
                Dial(NameList.TYPE_UDPPASS, server.Password!);
            }

            #endregion

            #region DNS

            if (_tunConfig.UseCustomDNS)
            {
                Dial(NameList.TYPE_DNSADDR, DnsUtils.AppendPort(_tunConfig.DNS));
            }
            else
            {
                await _aioDnsController.StartAsync();
                _dnsStarted = true;
                Dial(NameList.TYPE_DNSADDR, $"127.0.0.1:{Global.Settings.AioDNS.ListenPort}");
            }

            #endregion

            if (!Init())
                throw new MessageException("tun2socks start failed.");
            _initialized = true;

            // Init creates the Wintun adapter. Wait only after it has succeeded;
            // waiting before Init means a fresh install can never see the adapter.
            var adapterFound = false;
            for (var i = 0; i < 100; i++)
            {
                await Task.Delay(300);
                try
                {
                    _tun.InterfaceIndex = NetworkInterfaceUtils.Get(ni => ni.Name.StartsWith(interfaceName)).GetIndex();
                    adapterFound = true;
                    break;
                }
                catch (Exception exception) when (exception is InvalidOperationException or NetworkInformationException or NotSupportedException)
                {
                    // The Windows network interface list can lag behind adapter creation.
                }
            }

            if (!adapterFound)
                throw new MessageException("Wintun adapter did not become available within 30 seconds after initialization.");

            Log.Information("Wintun adapter is available at interface index {InterfaceIndex}", _tun.InterfaceIndex);

            var tunIndex = (int)RouteHelper.ConvertLuidToIndex(tun_luid());
            _tun = NetRoute.TemplateBuilder(_tunConfig.Gateway, tunIndex);

            RouteHelper.CreateUnicastIP(AddressFamily.InterNetwork,
                _tunConfig.Address,
                (byte)Utils.Utils.SubnetToCidr(_tunConfig.Netmask),
                (ulong)tunIndex);

            _routesMayExist = true;
            SetupRouteTable();
        }

        public async Task StopAsync()
        {
            if (_initialized)
            {
                try
                {
                    var freed = await FreeAsync().WaitAsync(TimeSpan.FromSeconds(10));
                    if (!freed)
                        Log.Warning("tun2socks reported that the adapter did not stop cleanly");
                }
                catch (TimeoutException exception)
                {
                    Log.Error(exception, "Stopping tun2socks exceeded the 10 second limit");
                }
                catch (Exception exception)
                {
                    Log.Error(exception, "Stopping tun2socks failed");
                }
                finally
                {
                    _initialized = false;
                }
            }

            if (_routesMayExist)
                await Task.Run(ClearRouteTable);

            if (_dnsStarted)
            {
                try
                {
                    await _aioDnsController.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
                }
                catch (TimeoutException exception)
                {
                    Log.Error(exception, "Stopping DNS service exceeded the 5 second limit");
                }
                catch (Exception exception)
                {
                    Log.Error(exception, "Stopping DNS service failed");
                }
                finally
                {
                    _dnsStarted = false;
                }
            }

            _tunNetworkInterface = null;
            _originalTunDns = Array.Empty<IPAddress>();
            _customDnsApplied = false;
        }

        private void CheckDriver()
        {
            string binDriver = Path.Combine(Global.NetchDir, Constants.WintunDllFile);
            string sysDriver = $@"{Environment.SystemDirectory}\wintun.dll";

            var binHash = Utils.Utils.Sha256CheckSumAsync(binDriver).Result;
            var sysHash = Utils.Utils.Sha256CheckSumAsync(sysDriver).Result;
            Log.Information("Built-in  wintun.dll Hash: {Hash}", binHash);
            Log.Information("Installed wintun.dll Hash: {Hash}", sysHash);
            if (binHash == sysHash)
                return;

            try
            {
                Log.Information("Copy wintun.dll to System Directory");
                File.Copy(binDriver, sysDriver, true);
            }
            catch (Exception e)
            {
                Log.Error(e, "Copy wintun.dll failed");
                throw new MessageException($"Failed to copy wintun.dll to system directory: {e.Message}");
            }
        }

        #region Route

        private void SetupRouteTable()
        {
            Global.MainForm.StatusText(i18N.Translate("Setup Route Table Rule"));

            var tunNetworkInterface = NetworkInterfaceUtils.Get(_tun.InterfaceIndex);
            _tunNetworkInterface = tunNetworkInterface;
            // Server Address
            if (_serverRemoteAddress != null)
                RouteUtils.CreateRoute(_outbound.FillTemplate(_serverRemoteAddress.ToString(), 32));

            // Global Bypass IPs
            RouteUtils.CreateRouteFill(_outbound, _tunConfig.BypassIPs);

            // rule
            RouteUtils.CreateRouteFill(_tun, _mode.Handle);
            RouteUtils.CreateRouteFill(_outbound, _mode.Bypass);

            // dns

            if (_tunConfig.UseCustomDNS)
            {
                if (_tunConfig.ProxyDNS)
                {
                    // NOTICE: DNS metric is network interface metric
                    RouteUtils.CreateRoute(_tun.FillTemplate(_tunConfig.DNS, 32));
                }

                _originalTunDns = tunNetworkInterface.GetIPProperties().DnsAddresses.ToArray();
                tunNetworkInterface.SetDns(_tunConfig.DNS);
                _customDnsApplied = true;
            }
            else
            {
                RouteUtils.CreateRoute(_outbound.FillTemplate(Utils.Utils.GetHostFromUri(Global.Settings.AioDNS.ChinaDNS), 32));
                RouteUtils.CreateRoute(_tun.FillTemplate(Utils.Utils.GetHostFromUri(Global.Settings.AioDNS.OtherDNS), 32));
            }

            NetworkInterfaceUtils.SetInterfaceMetric(_tun.InterfaceIndex, 0);
        }

        private void ClearRouteTable()
        {
            void TryCleanup(Action cleanup, string description)
            {
                try
                {
                    cleanup();
                }
                catch (Exception exception)
                {
                    Log.Warning(exception, "TUN cleanup failed for {Description}", description);
                }
            }

            if (_routesMayExist && _outbound.Gateway != null)
            {
                if (_serverRemoteAddress != null)
                    TryCleanup(() => RouteUtils.DeleteRoute(_outbound.FillTemplate(_serverRemoteAddress.ToString(), 32)), "proxy server route");
                TryCleanup(() => RouteUtils.DeleteRouteFill(_outbound, _tunConfig.BypassIPs), "bypass IP routes");
                TryCleanup(() => RouteUtils.DeleteRoute(_outbound.FillTemplate(Utils.Utils.GetHostFromUri(Global.Settings.AioDNS.ChinaDNS), 32)), "China DNS route");
                TryCleanup(() => NetworkInterfaceUtils.SetInterfaceMetric(_outbound.InterfaceIndex), "outbound interface metric");
                if (_mode != null)
                    TryCleanup(() => RouteUtils.DeleteRouteFill(_outbound, _mode.Bypass), "mode bypass routes");
            }

            if (_routesMayExist && _tun.InterfaceIndex != 0 && _mode != null)
                TryCleanup(() => RouteUtils.DeleteRouteFill(_tun, _mode.Handle), "mode tunnel routes");

            if (_routesMayExist && _tunConfig.UseCustomDNS && _tunConfig.ProxyDNS && _tun.InterfaceIndex != 0)
                TryCleanup(() => RouteUtils.DeleteRoute(_tun.FillTemplate(_tunConfig.DNS, 32)), "custom DNS route");
            else if (_routesMayExist && !_tunConfig.UseCustomDNS && _tun.InterfaceIndex != 0)
                TryCleanup(() => RouteUtils.DeleteRoute(_tun.FillTemplate(Utils.Utils.GetHostFromUri(Global.Settings.AioDNS.OtherDNS), 32)), "other DNS tunnel route");

            if (_customDnsApplied && _tunNetworkInterface != null)
                TryCleanup(() => _tunNetworkInterface.RestoreDns(_originalTunDns), "tunnel adapter DNS settings");

            _routesMayExist = false;
        }

        #endregion
    }
}
