# Team Fortress 2 Dedicated Server

[![WindowsGSH](.github/assets/windowsgsh-badge.svg)](https://windowsgsh.com)
[![Status](https://img.shields.io/badge/status-needs_live_test-f59e0b)](#status)
[![Module version](https://img.shields.io/badge/dynamic/json?url=https%3A%2F%2Fraw.githubusercontent.com%2FWindowsGSH%2FWindowsGSH.TF2%2Fmain%2FTF2.mod%2Fmodule.json&query=%24.version&prefix=v&label=module&color=1E8449)](TF2.mod/module.json)
[![Requires WindowsGSH](https://img.shields.io/badge/dynamic/json?url=https%3A%2F%2Fraw.githubusercontent.com%2FWindowsGSH%2FWindowsGSH.TF2%2Fmain%2FTF2.mod%2Fmodule.json%3Fbadge%3Dminimum&query=%24.minimumWindowsGshVersion&prefix=v&label=requires%20WindowsGSH&color=2563EB)](TF2.mod/module.json)
[![Licence](https://img.shields.io/badge/licence-MIT-64748B)](LICENSE.md)

This is the single supported WindowsGSH module for Team Fortress 2. It installs, configures, starts, stops, queries, administers, imports, and backs up a TF2 dedicated server.

## Status

**NEEDS LIVE TEST.** Native Source A2S, Source RCON, config preservation, SourceTV configuration, import, and both current 64-bit and compatibility 32-bit executables are implemented. Readiness Check validates the selected executable and config and warns when a public-server GSLT is absent. A live public server, SourceTV, graceful shutdown, reattachment, and plugin compatibility still require verification.

## Installation

WindowsGSH anonymously installs Steam app `232250`. The module uses `srcds_win64.exe` by default following Valve's 2024 64-bit server release and falls back to `srcds.exe` when necessary. Import `TF2.mod`, add a server, install, configure, and start it.

Create a unique Steam Game Server Login Token for game app `440` at <https://steamcommunity.com/dev/managegameservers> and enter it for public server-browser registration.

### Import an existing server

Import accepts a direct TF2 installation or a WindowsGSM root containing `serverfiles`, with either `srcds_win64.exe` or `srcds.exe`. Preview reads supported values from `tf\cfg\server.cfg`, preserves source files, and selects 64-bit when that executable is available. Copy and Adopt remain explicit host choices.

## Configuration

WindowsGSH manages hostname, visible player count, game password, RCON password, LAN mode, SourceTV, launch map, bind address, port, executable architecture, and optional GSLT. It updates managed `server.cfg` commands atomically while preserving comments, unrelated plugin/gameplay configuration, and existing baseline settings. Additional arguments are trusted raw command-line text.

Use 64-bit unless a specific MetaMod/SourceMod binary only supports 32-bit. Confirm every native extension matches the chosen architecture.

## Networking

| Purpose | Default | Protocol | Exposure |
| --- | ---: | --- | --- |
| Game and A2S query | `27015` | UDP | Required; eligible for opt-in UPnP. |
| Source RCON | `27015` | TCP | Administrative; private and excluded from automatic forwarding. |
| SourceTV | `27020` | UDP | Optional and private; forward manually only for public spectators. |

The shared numeric game/RCON endpoint is represented as separate UDP and TCP declarations, avoiding a false self-overlap. Do not expose Source RCON directly unless required; prefer a firewall allow-list or private management path.

## Query, console, and administration

WindowsGSH uses Source A2S for online status, map, version, and player counts, and Source RCON for commands. Configure a strong unique RCON password before using the RCON command box. The process window is hidden and direct embedded stdin is not claimed; RCON is the supported administration path. SourceTV is controlled through `sourcetv.enabled` and `sourcetv.port`.

## Files and backups

| Purpose | Path |
| --- | --- |
| 64-bit executable | `srcds_win64.exe` |
| 32-bit compatibility executable | `srcds.exe` |
| Server configuration | `tf\cfg\server.cfg` |
| Custom content | `tf\custom` |
| MetaMod/SourceMod | `tf\addons` |
| Logs | `tf\logs` |

Backup targets include `tf\cfg`, `tf\custom`, and `tf\addons`.

## Known limitations

- Live graceful stop behavior and RCON `quit` handling need validation.
- Third-party server plugins must match the selected process architecture.
- SourceTV behavior and manual forwarding need live validation.
- GSLT ownership and Valve server-operation requirements remain the operator's responsibility.

## Beta verification checklist

- [ ] Fresh-install app `232250`; confirm `srcds_win64.exe`, public listing with a unique app-440 GSLT, UDP joining/A2S, and opt-in UPnP.
- [ ] Test Source RCON locally, Stop, crash detection, reattachment, update, Verify Files, backup, and restore.
- [ ] Test direct and WindowsGSM Copy/Adopt imports with both executable layouts and existing cfg/custom/addons content.
- [ ] Verify SourceTV locally and through any intentionally configured public forwarding.
- [ ] Verify 64-bit MetaMod/SourceMod; separately test the 32-bit compatibility switch with matching plugins.

## Support

Report issues through the [WindowsGSH.TF2 tracker](https://github.com/WindowsGSH/WindowsGSH.TF2/issues) with versions and sanitized diagnostics. Never publish RCON passwords, GSLTs, player addresses, or unredacted archives.

## Support development

If you like the work I do and would like to support continued WindowsGSH and module development, you can contribute here:

- [Ko-fi](https://ko-fi.com/shenniko)
- [PayPal](https://paypal.me/shenniko)

## Trust and source

Modules run with WindowsGSH's permissions. Review the manifest, C# source, [SECURITY.md](SECURITY.md), and [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md). Installation and configuration follow the [Official Team Fortress Wiki](https://wiki.teamfortress.com/wiki/Windows_dedicated_server).
