using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using WindowsGSH.Core.Modules;
using WindowsGSH.Core.Query;
using WindowsGSH.Core.Rcon;
using WindowsGSH.Core.Readiness;
using WindowsGSH.Core.Servers;

namespace WindowsGSH.Modules.TF2;

public sealed class TF2Module : IGameServerModule, IManifestBackedModule, IModuleExistingServerImportCapability, IModuleGracefulStopCapability, IModulePortCapability, IModuleReadinessCapability
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

    public bool CanImport(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var installPath = ResolveExistingInstallPath(path);
        return HasServerExecutable(installPath);
    }

    public async Task<ModuleExistingServerImportProbe> PreviewImportAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var sourcePath = Path.GetFullPath(path);
        var installPath = ResolveExistingInstallPath(sourcePath);
        var probeInstance = new ServerInstance(
            Id: Path.GetFileName(sourcePath),
            Name: Name,
            ModuleId: Id,
            ServerFolder: sourcePath,
            InstallPath: installPath,
            ConfigPath: Path.Combine(sourcePath, "ServerConfig.json"),
            Settings: new Dictionary<string, object?>());

        var settings = new Dictionary<string, object?>(
            await ReadConfigFileSettingsAsync(probeInstance, cancellationToken),
            StringComparer.OrdinalIgnoreCase);
        settings["server.use64Bit"] = File.Exists(Path.Combine(installPath, "srcds_win64.exe"));
        var warnings = new List<string>();
        if (!File.Exists(GetServerCfgPath(probeInstance)))
        {
            warnings.Add("tf/cfg/server.cfg was not found. WindowsGSH will use module defaults for missing values.");
        }

        return new ModuleExistingServerImportProbe(
            SourceName: GetSetting(settings, "server.name", Path.GetFileName(sourcePath)),
            InstallPath: installPath,
            Settings: settings,
            Warnings: warnings);
    }

    public IReadOnlyList<ConfigFieldDefinition> GetConfigFields()
    {
        return Manifest.ToConfigFields();
    }

    public IReadOnlyList<ServerPortDefinition> GetPorts()
    {
        return Manifest.ToPorts();
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
            Port: GetServerPort(instance),
            MaxPlayers: GetSetting(instance, "server.maxPlayers", "24"));
    }

    public Task<IReadOnlyDictionary<string, object?>> ReadConfigFileSettingsAsync(ServerInstance instance, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var settings = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var path = GetServerCfgPath(instance);
        if (!File.Exists(path))
        {
            return Task.FromResult<IReadOnlyDictionary<string, object?>>(settings);
        }

        foreach (var rawLine in File.ReadLines(path))
        {
            var line = StripCfgComment(rawLine).Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var parts = line.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                continue;
            }

            var key = parts[0].Trim();
            var value = UnquoteCfg(parts[1].Trim());
            if (key.Equals("hostname", StringComparison.OrdinalIgnoreCase))
            {
                settings["server.name"] = value;
            }
            else if (key.Equals("sv_visiblemaxplayers", StringComparison.OrdinalIgnoreCase) &&
                     int.TryParse(value, out var maxPlayers))
            {
                settings["server.maxPlayers"] = maxPlayers;
            }
            else if (key.Equals("rcon_password", StringComparison.OrdinalIgnoreCase))
            {
                settings["rcon.password"] = value;
            }
            else if (key.Equals("sv_password", StringComparison.OrdinalIgnoreCase))
            {
                settings["server.password"] = value;
            }
            else if (key.Equals("sv_lan", StringComparison.OrdinalIgnoreCase))
            {
                settings["server.lan"] = value == "1" || bool.TryParse(value, out var lan) && lan;
            }
            else if (key.Equals("tv_enable", StringComparison.OrdinalIgnoreCase))
            {
                settings["sourcetv.enabled"] = value == "1" || bool.TryParse(value, out var enabled) && enabled;
            }
            else if (key.Equals("tv_port", StringComparison.OrdinalIgnoreCase) && int.TryParse(value, out var tvPort))
            {
                settings["sourcetv.port"] = tvPort;
            }
        }

        return Task.FromResult<IReadOnlyDictionary<string, object?>>(settings);
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
            FileName = GetExecutablePath(instance),
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
            throw new FileNotFoundException("Neither the selected TF2 server executable nor a compatible fallback was found.", GetExecutablePath(instance));
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

    public Task StopAsync(ServerInstance instance, CancellationToken cancellationToken)
    {
        return StopProcessesAsync(instance, cancellationToken, allowKill: true);
    }

    public Task StopGracefullyAsync(ServerInstance instance, CancellationToken cancellationToken)
    {
        return StopProcessesAsync(instance, cancellationToken, allowKill: false);
    }

    private async Task StopProcessesAsync(ServerInstance instance, CancellationToken cancellationToken, bool allowKill)
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

                if (allowKill && !process.HasExited)
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
        return HasServerExecutable(instance.InstallPath);
    }

    public Task<IReadOnlyList<ReadinessCheckResult>> CheckReadinessAsync(ServerInstance instance, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var checks = new List<ReadinessCheckResult>();
        var executablePath = GetExecutablePath(instance);
        checks.Add(File.Exists(executablePath)
            ? ReadinessCheckResult.Pass("TF2 executable", $"Found: {executablePath}")
            : ReadinessCheckResult.Fail("TF2 executable", "Neither srcds_win64.exe nor srcds.exe was found. Run install/update with SteamCMD app 232250."));

        var configPath = GetServerCfgPath(instance);
        checks.Add(File.Exists(configPath)
            ? ReadinessCheckResult.Pass("TF2 server configuration", $"Found: {configPath}")
            : ReadinessCheckResult.Info("TF2 server configuration", "tf/cfg/server.cfg will be created before the first start."));

        checks.Add(string.IsNullOrWhiteSpace(GetSetting(instance, "steam.gslt", ""))
            ? ReadinessCheckResult.Warning("Game Server Login Token", "A unique GSLT is required for a public Internet-listed TF2 server.")
            : ReadinessCheckResult.Pass("Game Server Login Token", "A GSLT is configured."));

        return Task.FromResult<IReadOnlyList<ReadinessCheckResult>>(checks);
    }

    public string? GetConsoleLogPath(ServerInstance instance)
    {
        return Path.Combine(instance.InstallPath, "tf", "logs");
    }

    public async Task<string> ExecuteRconCommandAsync(ServerInstance instance, string command, CancellationToken cancellationToken)
    {
        var host = GetConnectableHost(GetSetting(instance, "network.ip", "127.0.0.1"));
        var port = ParseInt(GetServerPort(instance), 27015);
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
        var port = ParseInt(GetSetting(instance, "network.queryPort", GetServerPort(instance)), 27015);

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
        var arguments = $"-console -game tf -ip {QuoteArgument(GetSetting(instance, "network.ip", "0.0.0.0"))} " +
                        $"-port {GetServerPort(instance)} +map {QuoteArgument(GetSetting(instance, "server.map", "ctf_2fort"))} " +
                        $"+maxplayers {ParseInt(GetSetting(instance, "server.maxPlayers", "24"), 24)} +exec server.cfg";
        var gslt = GetSetting(instance, "steam.gslt", "");
        if (!string.IsNullOrWhiteSpace(gslt))
        {
            arguments += $" +sv_setsteamaccount {QuoteArgument(gslt)}";
        }
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
        var path = Path.Combine(cfgFolder, "server.cfg");
        var lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : [];
        SetCfg(lines, "hostname", $"\"{EscapeCfg(GetSetting(instance, "server.name", "WindowsGSH TF2 Server"))}\"");
        SetCfg(lines, "sv_visiblemaxplayers", ParseInt(GetSetting(instance, "server.maxPlayers", "24"), 24).ToString());
        SetCfg(lines, "rcon_password", $"\"{EscapeCfg(GetSetting(instance, "rcon.password", ""))}\"");
        SetCfg(lines, "sv_password", $"\"{EscapeCfg(GetSetting(instance, "server.password", ""))}\"");
        SetCfg(lines, "sv_lan", GetBool(instance, "server.lan") ? "1" : "0");
        SetCfg(lines, "tv_enable", GetBool(instance, "sourcetv.enabled") ? "1" : "0");
        SetCfg(lines, "tv_port", ParseInt(GetSetting(instance, "sourcetv.port", "27020"), 27020).ToString());
        EnsureCfg(lines, "sv_pure", "0");
        EnsureCfg(lines, "sv_pausable", "0");
        EnsureCfg(lines, "log", "on");

        var temporaryPath = path + ".windowsgsh.tmp";
        try
        {
            File.WriteAllLines(temporaryPath, lines, new UTF8Encoding(false));
            File.Move(temporaryPath, path, true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
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

    private static string GetServerPort(ServerInstance instance)
    {
        return GetSetting(instance, "network.port", GetSetting(instance, "network.directConnectionPort", "27015"));
    }

    private static string GetServerCfgPath(ServerInstance instance)
    {
        return Path.Combine(instance.InstallPath, "tf", "cfg", "server.cfg");
    }

    private string ResolveExistingInstallPath(string path)
    {
        var sourcePath = Path.GetFullPath(path);
        if (HasServerExecutable(sourcePath))
        {
            return sourcePath;
        }

        var serverFilesPath = Path.Combine(sourcePath, "serverfiles");
        return HasServerExecutable(serverFilesPath)
            ? serverFilesPath
            : sourcePath;
    }

    private static bool HasServerExecutable(string installPath) =>
        File.Exists(Path.Combine(installPath, "srcds_win64.exe")) || File.Exists(Path.Combine(installPath, "srcds.exe"));

    private static string GetExecutablePath(ServerInstance instance)
    {
        var prefer64Bit = !instance.Settings.TryGetValue("server.use64Bit", out var value) ||
                          !bool.TryParse(value?.ToString(), out var parsed) || parsed;
        var preferred = Path.Combine(instance.InstallPath, prefer64Bit ? "srcds_win64.exe" : "srcds.exe");
        if (File.Exists(preferred)) return preferred;
        return Path.Combine(instance.InstallPath, prefer64Bit ? "srcds.exe" : "srcds_win64.exe");
    }

    private static void SetCfg(List<string> lines, string key, string value)
    {
        for (var index = 0; index < lines.Count; index++)
        {
            var content = StripCfgComment(lines[index]).Trim();
            var parts = content.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || !parts[0].Equals(key, StringComparison.OrdinalIgnoreCase)) continue;
            var commentIndex = lines[index].IndexOf("//", StringComparison.Ordinal);
            var comment = commentIndex >= 0 ? " " + lines[index][commentIndex..] : "";
            lines[index] = $"{key} {value}{comment}";
            return;
        }
        lines.Add($"{key} {value}");
    }

    private static void EnsureCfg(List<string> lines, string key, string value)
    {
        if (!lines.Any(line =>
                StripCfgComment(line).Trim()
                    .Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault()
                    ?.Equals(key, StringComparison.OrdinalIgnoreCase) == true))
        {
            lines.Add($"{key} {value}");
        }
    }

    private static string StripCfgComment(string line)
    {
        var index = line.IndexOf("//", StringComparison.Ordinal);
        return index >= 0 ? line[..index] : line;
    }

    private static string UnquoteCfg(string value)
    {
        var text = value.Trim();
        return text.Length >= 2 && text[0] == '"' && text[^1] == '"'
            ? text[1..^1].Replace("\\\"", "\"").Replace("\\\\", "\\")
            : text;
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

    private static string QuoteArgument(string value)
    {
        return $"\"{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
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
