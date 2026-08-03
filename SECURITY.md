# Security policy

## Security and trust

The Team Fortress 2 module executes C# and starts the Source dedicated server with the current user's Windows permissions. WindowsGSH cannot guarantee arbitrary third-party or modified modules. Review source, manifests, dependencies, and download origins before use.

## Download modules safely

Obtain the module from the official repository or another trusted source and install server files/plugins through legitimate Steam sources. Review MetaMod/SourceMod plugins independently and match their architecture to the selected server executable.

## Protect credentials and server data

Protect GSLTs, RCON passwords, admin lists, logs, player addresses, plugins, configs, and backups. Keep RCON private and redact secrets from command lines and diagnostics.

## Report a vulnerability

Use the [private repository advisory page](https://github.com/WindowsGSH/WindowsGSH.TF2/security/advisories/new). Do not publish exploits, credentials, private server data, or unredacted diagnostics.

## Include in a report

Include module/WindowsGSH/server versions, selected architecture, plugin provenance, reproduction steps, impact, and sanitized diagnostics.

## Supported versions

Security fixes target the latest module release and current WindowsGSH module API unless stated otherwise.
