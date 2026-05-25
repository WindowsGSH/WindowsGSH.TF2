using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using WindowsGSH.Core.Modules;
using WindowsGSH.Core.Query;
using WindowsGSH.Core.Rcon;
using WindowsGSH.Core.Servers;

namespace WindowsGSH.Modules.TF2;

public sealed class TF2Module : IGameServerModule, IManifestBackedModule
{
    private ModuleManifest? _manifest;
    private string _moduleDirectory = AppContext.BaseDirectory;

    private ModuleManifest Manifest => _manifest ??= ModuleManifest.Load(Path.Combine(_moduleDirectory, "module.json"));

    public string Id => Manifest.Id;

    public string Name => Manifest.Name;

    public string Version => Manifest.Version;

    public ModuleCapabilities Capabilities => Manifest.ToCapabilities(supportsQuery: true, supportsRcon: true);

    public SteamInstallDefinition? SteamInstall => Manifest.ToSteamInstall();

    public ModuleRuntimeDefinition Runtime => Manifest.ToRuntime();

    public void Configure(ModuleManifest manifest, string moduleDirectory)
    {
        _manifest = manifest;
        _moduleDirectory = moduleDirectory;
    }

    public IReadOnlyList<ConfigFieldDefinition> GetConfigFields()
    {
        return Manifest.ToConfigFields();
    }

    public IReadOnlyList<ServerAddonDefinition> GetAddonDefinitions()
    {
        return [];
    }

    public IReadOnlyList<ServerBackupTargetDefinition> GetBackupTargets()
    {
        return Manifest.ToBackupTargets();
    }

    public ServerAddonStatus GetAddonStatus(ServerInstance instance, string addonId)
    {
        return new ServerAddonStatus(addonId, IsInstalled: false, IsEnabled: false, StatusText: "Not available");
    }

    public Task InstallAddonAsync(ServerInstance instance, string addonId, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("This module does not expose addons.");
    }

    public Task RemoveAddonAsync(ServerInstance instance, string addonId, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("This module does not expose addons.");
    }

    public string GetServerName(IReadOnlyDictionary<string, object?> settings)
    {
        return GetSetting(settings, "server.name", "WindowsGSH TF2 Server");
    }

    public ServerDisplayInfo GetDisplayInfo(ServerInstance instance)
    {
        return new ServerDisplayInfo(
            IpAddress: GetSetting(instance, "network.ip", "0.0.0.0"),
            Port: GetSetting(instance, "network.directConnectionPort", "27015"),
            MaxPlayers: GetSetting(instance, "server.maxPlayers", "24"));
    }

    public Task<IReadOnlyDictionary<string, object?>> ReadConfigFileSettingsAsync(ServerInstance instance, CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyDictionary<string, object?>>(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase));
    }

    public Task WriteConfigFileSettingsAsync(ServerInstance instance, CancellationToken cancellationToken)
    {
        WriteServerCfg(instance);
        return Task.CompletedTask;
    }

    public Task<InstallPlan> CreateInstallPlanAsync(ServerInstance instance, CancellationToken cancellationToken)
    {
        return Task.FromResult(new InstallPlan(
            Tool: "steamcmd",
            Arguments: $"+force_install_dir \"{instance.InstallPath}\" +login anonymous +app_update {SteamInstall?.AppId} validate +quit",
            WorkingDirectory: instance.InstallPath,
            Notes:
            [
                "TF2 Dedicated Server supports Source console, Source RCON, and A2S querying.",
                "RCON listens on the same TCP port as the game server unless changed by Source."
            ]));
    }

    public Task<ProcessStartInfo> CreateStartInfoAsync(ServerInstance instance, CancellationToken cancellationToken)
    {
        WriteServerCfg(instance);

        return Task.FromResult(new ProcessStartInfo
        {
            FileName = Path.Combine(instance.InstallPath, Runtime.StartPath),
            WorkingDirectory = instance.InstallPath,
            Arguments = BuildLaunchArguments(instance),
            UseShellExecute = true,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        });
    }

    public async Task<Process?> StartAsync(ServerInstance instance, CancellationToken cancellationToken)
    {
        if (!IsInstallValid(instance))
        {
            throw new FileNotFoundException("TF2 server executable was not found.", Path.Combine(instance.InstallPath, Runtime.StartPath));
        }

        var startInfo = await CreateStartInfoAsync(instance, cancellationToken);
        var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };

        process.Start();
        _ = HideMainWindowWhenReadyAsync(process, cancellationToken);
        return process;
    }

    public async Task StopAsync(ServerInstance instance, CancellationToken cancellationToken)
    {
        var processes = ServerProcessLocator.FindProcesses(this, instance.InstallPath);
        foreach (var process in processes)
        {
            using (process)
            {
                if (process.HasExited)
                {
                    continue;
                }

                try
                {
                    process.CloseMainWindow();
                    await Task.Delay(1500, cancellationToken);
                }
                catch
                {
                }

                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync(cancellationToken);
                }
            }
        }
    }

    public Task<IReadOnlyList<Process>> StartAddonProcessesAsync(ServerInstance instance, CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyList<Process>>([]);
    }

    public bool IsInstallValid(ServerInstance instance)
    {
        return File.Exists(Path.Combine(instance.InstallPath, Runtime.StartPath));
    }

    public string? GetConsoleLogPath(ServerInstance instance)
    {
        return Path.Combine(instance.InstallPath, "tf", "logs");
    }

    public async Task<string> ExecuteRconCommandAsync(ServerInstance instance, string command, CancellationToken cancellationToken)
    {
        var host = GetConnectableHost(GetSetting(instance, "network.ip", "127.0.0.1"));
        var port = ParseInt(GetSetting(instance, "network.directConnectionPort", "27015"), 27015);
        var password = GetSetting(instance, "rcon.password", "");

        if (string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException("RCON password is not configured.");
        }

        return await new SourceRconClient().ExecuteAsync(host, port, password, command, cancellationToken);
    }

    public async Task<QueryResult> QueryAsync(ServerInstance instance, CancellationToken cancellationToken)
    {
        if (!string.Equals(Runtime.QueryProtocol, "A2S", StringComparison.OrdinalIgnoreCase))
        {
            return new QueryResult(
                ModuleServerStatus.Unknown,
                MaxPlayers: ParseInt(GetSetting(instance, "server.maxPlayers", "24"), 24),
                Message: $"Unsupported query protocol: {Runtime.QueryProtocol ?? "none"}.");
        }

        var host = GetQueryHost(GetSetting(instance, "network.ip", "127.0.0.1"));
        var port = ParseInt(GetSetting(instance, "network.queryPort", GetSetting(instance, "network.directConnectionPort", "27015")), 27015);

        try
        {
            var info = await new SourceA2sClient().QueryInfoAsync(host, port, TimeSpan.FromSeconds(2), cancellationToken);
            return new QueryResult(
                ModuleServerStatus.Online,
                OnlinePlayers: info.Players,
                MaxPlayers: info.MaxPlayers,
                Version: info.Version,
                Message: string.IsNullOrWhiteSpace(info.Map)
                    ? $"A2S responded from {host}:{port}."
                    : $"A2S responded from {host}:{port}. Map: {info.Map}");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new QueryResult(
                ModuleServerStatus.Offline,
                MaxPlayers: ParseInt(GetSetting(instance, "server.maxPlayers", "24"), 24),
                Message: $"A2S query to {host}:{port} timed out.");
        }
        catch (Exception ex) when (ex is SocketException or IOException or InvalidDataException)
        {
            return new QueryResult(
                ModuleServerStatus.Offline,
                MaxPlayers: ParseInt(GetSetting(instance, "server.maxPlayers", "24"), 24),
                Message: $"A2S query to {host}:{port} failed: {ex.Message}");
        }
    }

    private string BuildLaunchArguments(ServerInstance instance)
    {
        var arguments = Regex.Replace(Manifest.GetDefaultArguments(), "\\{(?<key>[^}]+)\\}", match =>
        {
            var key = match.Groups["key"].Value;
            return instance.Settings.TryGetValue(key, out var value) ? value?.ToString() ?? string.Empty : string.Empty;
        });

        var additional = GetSetting(instance, "server.additionalArguments", "");
        if (!string.IsNullOrWhiteSpace(additional))
        {
            arguments = string.IsNullOrWhiteSpace(arguments)
                ? additional.Trim()
                : $"{arguments} {additional.Trim()}";
        }

        return arguments;
    }

    private static void WriteServerCfg(ServerInstance instance)
    {
        var cfgFolder = Path.Combine(instance.InstallPath, "tf", "cfg");
        Directory.CreateDirectory(cfgFolder);

        var lines = new StringBuilder();
        lines.AppendLine($"hostname \"{EscapeCfg(GetSetting(instance, "server.name", "WindowsGSH TF2 Server"))}\"");
        lines.AppendLine($"sv_visiblemaxplayers {ParseInt(GetSetting(instance, "server.maxPlayers", "24"), 24)}");
        lines.AppendLine($"rcon_password \"{EscapeCfg(GetSetting(instance, "rcon.password", ""))}\"");
        lines.AppendLine($"sv_password \"{EscapeCfg(GetSetting(instance, "server.password", ""))}\"");
        lines.AppendLine($"sv_lan {(GetBool(instance, "server.lan") ? 1 : 0)}");
        lines.AppendLine("sv_pure 0");
        lines.AppendLine("sv_pausable 0");
        lines.AppendLine("log on");

        File.WriteAllText(Path.Combine(cfgFolder, "server.cfg"), lines.ToString());
    }

    private static string GetSetting(ServerInstance instance, string key, string fallback)
    {
        return GetSetting(instance.Settings, key, fallback);
    }

    private static string GetSetting(IReadOnlyDictionary<string, object?> settings, string key, string fallback)
    {
        return settings.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value?.ToString())
            ? value.ToString()!.Trim()
            : fallback;
    }

    private static bool GetBool(ServerInstance instance, string key)
    {
        return instance.Settings.TryGetValue(key, out var value) && bool.TryParse(value?.ToString(), out var parsed) && parsed;
    }

    private static string GetConnectableHost(string host)
    {
        return string.Equals(host, "0.0.0.0", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(host, "::", StringComparison.OrdinalIgnoreCase)
            ? "127.0.0.1"
            : host;
    }

    private static string GetQueryHost(string host)
    {
        if (!string.Equals(host, "0.0.0.0", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(host, "::", StringComparison.OrdinalIgnoreCase))
        {
            return host;
        }

        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(network => network.OperationalStatus == OperationalStatus.Up)
                .Select(network => network.GetIPProperties())
                .Where(properties => properties.GatewayAddresses.Any(gateway => gateway.Address.AddressFamily == AddressFamily.InterNetwork))
                .SelectMany(properties => properties.UnicastAddresses)
                .Select(address => address.Address)
                .FirstOrDefault(address => address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address))
                ?.ToString()
                ?? Dns.GetHostEntry(Dns.GetHostName())
                .AddressList
                .FirstOrDefault(address => address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address))
                ?.ToString() ?? "127.0.0.1";
        }
        catch
        {
            return "127.0.0.1";
        }
    }

    private static int ParseInt(string value, int fallback)
    {
        return int.TryParse(value, out var parsed) ? parsed : fallback;
    }

    private static string EscapeCfg(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    private static async Task HideMainWindowWhenReadyAsync(Process process, CancellationToken cancellationToken)
    {
        try
        {
            for (var attempt = 0; attempt < 20 && !cancellationToken.IsCancellationRequested; attempt++)
            {
                if (process.HasExited)
                {
                    return;
                }

                process.Refresh();
                if (process.MainWindowHandle != IntPtr.Zero)
                {
                    ShowWindow(process.MainWindowHandle, ShowWindowCommand.Hide);
                    return;
                }

                await Task.Delay(250, cancellationToken);
            }
        }
        catch
        {
        }
    }

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, ShowWindowCommand nCmdShow);

    private enum ShowWindowCommand
    {
        Hide = 0
    }
}
