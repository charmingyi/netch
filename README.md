# `In preparation for 2.0, this repository will be cleared of all 1.0 related releases and code`
<p align="center"><img src="https://github.com/NetchX/Netch/blob/main/Netch/Resources/Netch.png?raw=true" width="128" /></p>

<div align="center">

# Netch
A simple proxy client

[![](https://img.shields.io/badge/telegram-group-green?style=flat-square)](https://t.me/netch_group)
[![](https://img.shields.io/badge/telegram-channel-blue?style=flat-square)](https://t.me/netch_channel)
[![](https://img.shields.io/github/downloads/netchx/netch/total.svg?style=flat-square)](https://github.com/netchx/netch/releases)
[![](https://img.shields.io/github/v/release/netchx/netch?style=flat-square)](https://github.com/netchx/netch/releases)
</div>

## Features
Some features may not be implemented in version 1

### Modes
- `ProcessMode` - Use Netfilter driver to intercept process traffic
- `ShareMode` - Share your network based on WinPcap / Npcap
- `TunMode` - Use WinTUN driver to create virtual adapter
- `WebMode` - Web proxy mode

### Protocols
- [`Socks5`](https://www.wikiwand.com/en/SOCKS)
- SOCKS4 / SOCKS4A
- HTTP proxy
- [`Shadowsocks`](https://shadowsocks.org)
- [`ShadowsocksR`](https://github.com/shadowsocksrr/shadowsocksr-libev)
- [`WireGuard`](https://www.wireguard.com)
- [`Trojan`](https://trojan-gfw.github.io/trojan)
- [`VMess`](https://www.v2fly.org)
- [`VLESS`](https://xtls.github.io)
- Multi-hop proxy chains for supported outbound protocols

### Others
- UDP NAT FullCone (Limited by your server)
- .NET 6.0 x64

Process and route modes can be managed as user presets. Process modes can define
a game server address for live ICMP reachability, latency, and probe-loss display.
This active probe does not measure loss inside encrypted game traffic.

NAT discovery runs through the active proxy and reports mapping and filtering
behavior from the configured STUN service. Results depend on the proxy's UDP
support and the test service.

Proxy chains can contain two to five nodes. Add them in entry-to-exit order; the
current SagerNet core routes the exit connection through the preceding node.
UDP behavior depends on the selected protocols and upstream core support.

## Sponsor
<a href="https://www.jetbrains.com/?from=Netch"><img src="jetbrains.svg" alt="JetBrains" width="200"/></a>

## License
Netch is licensed under the [GPLv3](https://raw.githubusercontent.com/netchx/netch/main/LICENSE) license
