# Team Fortress 2 Dedicated Server

WindowsGSH module for Team Fortress 2 dedicated servers.

## Support

If this module helps you host your servers, you can support development here:

- [Ko-fi](https://ko-fi.com/shenniko)
- [PayPal](https://paypal.me/shenniko)

## Module Layout

```text
WindowsGSH.TF2/
  README.md
  LICENSE.md
  TF2.mod/
    module.json
    TF2Module.cs
    author.png
```

Import `TF2.mod` directly, or import the repository root and let WindowsGSH discover the nested module folder.

## Current Status

- Declares WindowsGSH module API `1.0` compatibility.
- Installs through SteamCMD app `232250`.
- Starts `srcds.exe` with `-console -game tf`.
- Writes `tf/cfg/server.cfg`.
- Supports Source query status.
- Supports Source RCON through the configured RCON password.
- Backs up server config, custom content, and SourceMod/Metamod addons.
- Supports WindowsGSH existing-server import and WindowsGSM-style `serverfiles` imports.

## Quick Start

1. Import the module in WindowsGSH Module Management.
2. Create a new Team Fortress 2 server.
3. Set the server name, starting map, max players, IP, port, and RCON password.
4. Install the server through WindowsGSH.
5. Start the server.

## Important Settings

- `server.name`: hostname written to `server.cfg`.
- `server.map`: first map passed to `srcds.exe`.
- `server.maxPlayers`: maximum players passed to launch arguments.
- `network.ip`: bind address. Use `0.0.0.0` for all interfaces.
- `network.port`: game, query, and RCON port for the default TF2 setup.
- `rcon.password`: required for RCON commands.
- `server.additionalArguments`: optional extra launch arguments.

## Existing Server Import

Choose **Import Existing** and select either:

- a TF2 dedicated-server install containing `srcds.exe`; or
- a WindowsGSM server folder containing `serverfiles/srcds.exe`.

Existing values are previewed from `tf/cfg/server.cfg` when present.

## Backups

The default backup targets are:

- `tf/cfg`
- `tf/custom`
- `tf/addons`

## Trust Note

C# modules run code on the user's machine. WindowsGSH does not create, own, review, sign, or guarantee third-party modules.
