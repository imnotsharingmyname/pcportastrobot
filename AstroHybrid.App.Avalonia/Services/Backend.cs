using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace AstroHybrid.App.Avalonia.Services;

public sealed class AppSettings
{
    public string GamePath { get; set; } = "";
    public int Width { get; set; } = 1920;
    public int Height { get; set; } = 1080;
    public int FpsLimit { get; set; } = 60;
    public bool Fullscreen { get; set; }
    public bool VSync { get; set; }
    public bool GameSpeed { get; set; } = true;
    public int GameSpeedPercent { get; set; } = 100;
    public bool PadMotion { get; set; }
    public bool DirectAudio { get; set; } = true;
    public string AudioSink { get; set; } = "";
    public string ExtraEnv { get; set; } = "";

    // Graphics
    public string OverlayMode { get; set; } = "";
    public bool LowLatency { get; set; }

    // Controller
    public string ControllerPref { get; set; } = "auto";
    public int RumblePercent { get; set; } = 100;
    public bool RumbleSwitchBoost { get; set; }
    public bool DsWirelessSpeaker { get; set; }
    public bool DsWirelessHaptics { get; set; }
    public bool TriggerState { get; set; } = true;

    // Advanced
    public int Lighting { get; set; }
    public int Language { get; set; } = -1;
    public string Priority { get; set; } = "normal";
    public int CpPriority { get; set; }
    public bool HangWatchdog { get; set; }
    public bool ShaderCache { get; set; } = true;
    public bool DrawPrep { get; set; } = true;
    public bool SkipIntro { get; set; }
}

public sealed class AppSettingsStore
{
    private static readonly string ConfigDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "astrohybrid");
    private static readonly string ConfigFile = System.IO.Path.Combine(ConfigDirectory, "settings.json");

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(ConfigFile))
            {
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(ConfigFile)) ?? new AppSettings();
            }
        }
        catch
        {
            // A damaged settings file falls back to defaults; the user can save again.
        }

        return new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(ConfigDirectory);
        File.WriteAllText(ConfigFile, JsonSerializer.Serialize(settings,
            new JsonSerializerOptions { WriteIndented = true }));
    }

    public string ConfigPath => ConfigFile;
}

public sealed class ProcessResult
{
    public int ExitCode { get; init; }
    public string Output { get; init; } = "";
    public bool Success => ExitCode == 0;
}

public sealed record GamePadInfo(string Name, string Type, bool HasHaptics, bool HasMotion, bool HasTriggers);

public sealed class Backend
{
    public string Root { get; }
    public string Script { get; }
    public string Python { get; }
    public string RunDirectory => Path.Combine(Root, "Build", "Run");
    public string HybridDirectory => Path.Combine(Root, "Build", "RunHybrid");
    public string DefaultGamePath => Path.Combine(Root, "Game", "PPSA21567-app");
    public string LogDirectory => Path.Combine(Root, "Logs");

    private Process? _game;
    private bool _stopping;
    private string _statusMessage = "Loading…";
    private bool _isBusy;
    private readonly AppSettingsStore _store = new();

    public event Action<bool>? GameRunningChanged;
    public event Action<string>? GameOutput;
    public event Action? StatusChanged;

    public string StatusMessage => _statusMessage;
    public bool IsBusy => _isBusy;

    public void SetStatus(string message, bool busy = false)
    {
        _statusMessage = message;
        _isBusy = busy;
        StatusChanged?.Invoke();
    }


    public Backend()
    {
        Root = FindRoot();
        Script = Path.Combine(Root, "Tools", "linux", "astrohybrid.py");
        Python = "python3";
    }

    private string _gamePath = "";

    public AppSettings LoadSettings()
    {
        var settings = _store.Load();
        _gamePath = settings.GamePath;
        return settings;
    }

    public void SaveSettings(AppSettings settings)
    {
        _gamePath = settings.GamePath;
        _store.Save(settings);
    }

    public void SetGamePath(string path) => _gamePath = path;

    public bool HybridReady =>
        File.Exists(Path.Combine(HybridDirectory, "AstroBot")) &&
        File.Exists(Path.Combine(HybridDirectory, "libs", "kyty_gpu.so")) &&
        File.Exists(Path.Combine(HybridDirectory, "libs", "libAstroHybridHost.prx"));

    public bool GameRunning => _game is { HasExited: false };

    public async Task<ProcessResult> CheckAsync(string gamePath, Action<string>? onLine = null,
        CancellationToken cancellationToken = default) =>
        await RunAsync(["check", "--game", gamePath], null, onLine, cancellationToken);

    public async Task<ProcessResult> SetupAsync(string gamePath, bool hybrid,
        Action<string>? onLine = null, CancellationToken cancellationToken = default)
    {
        var arguments = new List<string> { "setup", "--game", gamePath };
        if (hybrid)
        {
            arguments.Add("--hybrid");
        }

        return await RunAsync(arguments, null, onLine, cancellationToken);
    }

    public async Task<ProcessResult> RunToolAsync(IEnumerable<string> arguments,
        Action<string>? onLine = null, CancellationToken cancellationToken = default) =>
        await RunAsync(arguments, null, onLine, cancellationToken);

    public void StartGame(AppSettings settings)
    {
        if (GameRunning)
        {
            return;
        }

        if (!Directory.Exists(settings.GamePath))
        {
            SetStatus("Game dump not found");
            return;
        }

        if (!HybridReady)
        {
            SetStatus("Setup required");
            return;
        }

        var arguments = new List<string> { Script, "run", "--hybrid", "--game", settings.GamePath };
        if (settings.DirectAudio)
        {
            arguments.Add("--direct-audio");
            if (!string.IsNullOrWhiteSpace(settings.AudioSink))
            {
                arguments.Add("--audio-sink");
                arguments.Add(settings.AudioSink.Trim());
            }
        }
        arguments.Add("--env");
        arguments.Add("APS5_HOST_IMPORT_MIB");
        arguments.Add("11264");
        arguments.Add("--env");
        arguments.Add("KYTY_GPU_WIDTH");
        arguments.Add(settings.Width.ToString());
        arguments.Add("--env");
        arguments.Add("KYTY_GPU_HEIGHT");
        arguments.Add(settings.Height.ToString());
        if (settings.FpsLimit > 0)
        {
            arguments.Add("--env");
            arguments.Add("ASTROHYBRID_FPS_LIMIT");
            arguments.Add(settings.FpsLimit.ToString());
        }
        if (settings.Fullscreen)
        {
            arguments.Add("--env");
            arguments.Add("KYTY_GPU_FULLSCREEN");
            arguments.Add("1");
        }
        if (settings.VSync)
        {
            arguments.Add("--env");
            arguments.Add("KYTY_GPU_PRESENT_MODE");
            arguments.Add("Fifo");
        }
        if (settings.GameSpeed)
        {
            arguments.Add("--env");
            arguments.Add("ASTROHYBRID_GAME_SPEED");
            arguments.Add("1");
            if (settings.GameSpeedPercent is > 0 and < 100)
            {
                arguments.Add("--env");
                arguments.Add("ASTROHYBRID_GAME_SPEED_PERCENT");
                arguments.Add(settings.GameSpeedPercent.ToString());
            }
        }
        if (settings.PadMotion)
        {
            arguments.Add("--env");
            arguments.Add("ASTROHYBRID_PAD_MOTION");
            arguments.Add("1");
        }
        if (!string.IsNullOrWhiteSpace(settings.OverlayMode))
        {
            arguments.Add("--env");
            arguments.Add("ASTROHYBRID_OVERLAY");
            arguments.Add(settings.OverlayMode);
        }
        if (settings.LowLatency)
        {
            arguments.Add("--env");
            arguments.Add("ASTROHYBRID_LOW_LATENCY");
            arguments.Add("1");
        }
        if (!string.IsNullOrWhiteSpace(settings.ControllerPref) && settings.ControllerPref != "auto")
        {
            arguments.Add("--env");
            arguments.Add("ASTROHYBRID_PAD_PREFER");
            arguments.Add(settings.ControllerPref);
        }
        if (settings.RumblePercent != 100)
        {
            arguments.Add("--env");
            arguments.Add("ASTROHYBRID_RUMBLE_SCALE");
            arguments.Add(settings.RumblePercent.ToString());
        }
        arguments.Add("--env");
        arguments.Add("ASTROHYBRID_RUMBLE_SWITCH_FLOOR");
        arguments.Add(settings.RumbleSwitchBoost
            ? (96 * settings.RumblePercent / 100).ToString()
            : "0");
        arguments.Add("--env");
        arguments.Add("ASTROHYBRID_DS_WIRELESS_SPEAKER");
        arguments.Add(settings.DsWirelessSpeaker ? "1" : "0");
        arguments.Add("--env");
        arguments.Add("ASTROHYBRID_DS_WIRELESS_HAPTICS");
        arguments.Add(settings.DsWirelessHaptics ? "1" : "0");
        if (!settings.TriggerState)
        {
            arguments.Add("--env");
            arguments.Add("APS5_PAD_TRIGGER_STATE");
            arguments.Add("0");
        }
        if (settings.Language >= 0)
        {
            arguments.Add("--env");
            arguments.Add("ANYPS5_LANGUAGE");
            arguments.Add(settings.Language.ToString());
        }
        if (settings.CpPriority > 0)
        {
            arguments.Add("--env");
            arguments.Add("KYTY_CP_PRIORITY");
            arguments.Add(settings.CpPriority.ToString());
        }
        if (settings.Priority != "normal")
        {
            arguments.Add("--priority");
            arguments.Add(settings.Priority);
        }
        foreach (var pair in ParseEnv(settings.ExtraEnv))
        {
            arguments.Add("--env");
            arguments.Add(pair.Key);
            arguments.Add(pair.Value);
        }

        var start = new ProcessStartInfo(Python)
        {
            WorkingDirectory = Root,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }
        start.Environment["PYTHONUNBUFFERED"] = "1";

        var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => Emit(e.Data);
        process.ErrorDataReceived += (_, e) => Emit(e.Data);
        process.Exited += (_, _) =>
        {
            var code = 0;
            try
            {
                code = process.ExitCode;
            }
            catch
            {
                // The process may already be disposed.
            }

            var stopped = _stopping;
            _stopping = false;
            if (ReferenceEquals(_game, process))
            {
                _game = null;
            }

            process.Dispose();
            GameRunningChanged?.Invoke(false);
            SetStatus(stopped ? "Game stopped" : code == 0 ? "Game exited" : $"Game exited (code {code})");
        };

        try
        {
            _game = process;
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            SetStatus("Running");
            GameRunningChanged?.Invoke(true);
        }
        catch (Exception exception)
        {
            _game = null;
            process.Dispose();
            SetStatus($"Launch failed: {exception.Message}");
            GameRunningChanged?.Invoke(false);
        }
    }

    public void StopGame()
    {
        var process = _game;
        if (process is null)
        {
            return;
        }

        _stopping = true;
        SetStatus("Stopping…");

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // The game may have exited between the check and the kill.
        }
    }

    public IReadOnlyList<GamePadInfo> DetectGamePads()
    {
        var result = new List<GamePadInfo>();
        try
        {
            var devices = File.ReadAllText("/proc/bus/input/devices");
            foreach (var block in devices.Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
            {
                if (!block.Contains("js", StringComparison.Ordinal) ||
                    !(block.Contains("Steam", StringComparison.OrdinalIgnoreCase) ||
                      block.Contains("Controller", StringComparison.OrdinalIgnoreCase) ||
                      block.Contains("Pad", StringComparison.OrdinalIgnoreCase) ||
                      block.Contains("DualSense", StringComparison.OrdinalIgnoreCase) ||
                      block.Contains("DualShock", StringComparison.OrdinalIgnoreCase) ||
                      block.Contains("Xbox", StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var name = block.Split('\n')
                    .FirstOrDefault(line => line.StartsWith("N: Name=", StringComparison.Ordinal));
                if (name is null)
                {
                    continue;
                }

                var label = name.Replace("N: Name=", "").Trim('"');
                var family = PadFamily(label);
                result.Add(new GamePadInfo(
                    label,
                    family,
                    family != "Generic",
                    family is "PlayStation" or "Switch Pro",
                    family == "PlayStation"));
            }
        }
        catch
        {
            // /proc is optional outside Linux; the page simply shows no pads.
        }

        return result
            .GroupBy(pad => pad.Name, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();
    }

    private static string PadFamily(string name)
    {
        if (name.Contains("DualSense", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("DualShock", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("PlayStation", StringComparison.OrdinalIgnoreCase))
        {
            return "PlayStation";
        }

        if (name.Contains("Switch", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Nintendo", StringComparison.OrdinalIgnoreCase))
        {
            return "Switch Pro";
        }

        if (name.Contains("Xbox", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("X-Box", StringComparison.OrdinalIgnoreCase))
        {
            return "Xbox";
        }

        return "Generic";
    }

    public string CpuName
    {
        get
        {
            try
            {
                foreach (var line in File.ReadLines("/proc/cpuinfo"))
                {
                    if (line.StartsWith("model name", StringComparison.Ordinal))
                    {
                        return line.Split(':', 2)[1].Trim();
                    }
                }
            }
            catch
            {
            }

            return "Unknown CPU";
        }
    }

    public string MemoryLabel
    {
        get
        {
            try
            {
                foreach (var line in File.ReadLines("/proc/meminfo"))
                {
                    if (line.StartsWith("MemTotal:", StringComparison.Ordinal))
                    {
                        var kb = double.Parse(line.Split(':')[1].Trim().Split(' ')[0]);
                        return $"{kb / 1024 / 1024:0} GiB";
                    }
                }
            }
            catch
            {
            }

            return "";
        }
    }

    public string GpuName
    {
        get
        {
            try
            {
                var start = new ProcessStartInfo("lspci")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                };
                start.ArgumentList.Add("-nn");
                using var lspci = Process.Start(start);
                if (lspci is null)
                {
                    return "";
                }

                var output = lspci.StandardOutput.ReadToEnd();
                lspci.WaitForExit(1000);
                var gpuLine = output.Split('\n')
                    .FirstOrDefault(line => line.Contains("VGA", StringComparison.Ordinal) ||
                                            line.Contains("3D controller", StringComparison.Ordinal));
                if (gpuLine is null)
                {
                    return "";
                }

                var classEnd = gpuLine.IndexOf("]: ", StringComparison.Ordinal);
                var rest = classEnd >= 0 ? gpuLine[(classEnd + 3)..] : gpuLine;
                rest = Regex.Replace(rest, @"\s*\[[0-9a-fA-F]{4}:[0-9a-fA-F]{4}\].*$", "");
                var brackets = Regex.Matches(rest, @"\[([^\]]+)\]");
                return brackets.Count > 0 ? brackets[^1].Groups[1].Value.Trim() : rest.Trim();
            }
            catch
            {
                return "";
            }
        }
    }

    /// <summary>Game version from the dump's sce_sys/param.json, or empty.</summary>
    public string GameVersion => GameInfo.Version;

    /// <summary>Title id from the dump's sce_sys/param.json, or the tested id.</summary>
    public string GameTitleId => GameInfo.TitleId.Length > 0 ? GameInfo.TitleId : "PPSA21567";

    public bool GameInfoAvailable => GameInfo.Version.Length > 0 || GameInfo.TitleId.Length > 0;

    /// <summary>Short commit the launcher was built from.</summary>
    public static string BuildSha
    {
        get
        {
            try
            {
                var file = Path.Combine(AppContext.BaseDirectory, "build-info.txt");
                if (File.Exists(file))
                {
                    var text = File.ReadAllText(file).Trim();
                    if (text.Length > 0)
                    {
                        return text;
                    }
                }
            }
            catch
            {
            }

            // Development runs from a checkout: ask git directly.
            try
            {
                var start = new ProcessStartInfo("git")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                start.ArgumentList.Add("rev-parse");
                start.ArgumentList.Add("--short");
                start.ArgumentList.Add("HEAD");
                using var git = Process.Start(start);
                if (git is not null)
                {
                    var text = git.StandardOutput.ReadToEnd().Trim();
                    git.WaitForExit(1000);
                    if (text.Length > 0)
                    {
                        return text;
                    }
                }
            }
            catch
            {
            }

            return "unknown";
        }
    }

    public (string TitleId, string Version) GameInfo => ReadGameInfo();

    private (string TitleId, string Version) ReadGameInfo()
    {
        var path = Path.Combine(GamePathOrDefault(), "sce_sys", "param.json");
        try
        {
            if (!File.Exists(path))
            {
                return ("", "");
            }

            using var document = JsonDocument.Parse(File.ReadAllText(path));
            string Get(string name) =>
                document.RootElement.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                    ? value.GetString() ?? ""
                    : "";
            return (Get("titleId"), Get("contentVersion"));
        }
        catch
        {
            return ("", "");
        }
    }

    private string GamePathOrDefault() =>
        _gamePath.Length > 0 ? _gamePath : DefaultGamePath;

    private string _installSize = "";
    private DateTime _installSizeAt;

    public string InstalledAt
    {
        get
        {
            try
            {
                var binary = Path.Combine(HybridDirectory, "AstroBot");
                return File.Exists(binary)
                    ? File.GetLastWriteTime(binary).ToString("yyyy-MM-dd HH:mm")
                    : "—";
            }
            catch
            {
                return "—";
            }
        }
    }

    /// <summary>Size of Build/RunHybrid, cached for half a minute.</summary>
    public async Task<string> InstallSizeAsync()
    {
        if (_installSize.Length > 0 && (DateTime.UtcNow - _installSizeAt).TotalSeconds < 30)
        {
            return _installSize;
        }

        if (!Directory.Exists(HybridDirectory))
        {
            _installSize = "";
            return _installSize;
        }

        try
        {
            var start = new ProcessStartInfo("du")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            start.ArgumentList.Add("-sh");
            start.ArgumentList.Add(HybridDirectory);
            using var du = Process.Start(start);
            if (du is null)
            {
                return "";
            }

            var output = await du.StandardOutput.ReadToEndAsync();
            await du.WaitForExitAsync();
            _installSize = output.Split('\t')[0].Trim();
            _installSizeAt = DateTime.UtcNow;
        }
        catch
        {
            _installSize = "";
        }

        return _installSize;
    }

    /// <summary>Pinned AnyPS5 and Kyty commits the runtime was built from.</summary>
    public (string AnyPs5, string Kyty) ToolVersions()
    {
        string Read(string script)
        {
            try
            {
                var path = Path.Combine(Root, "Tools", "linux", script);
                if (!File.Exists(path))
                {
                    return "—";
                }

                foreach (var line in File.ReadLines(path))
                {
                    if (line.StartsWith("PIN=", StringComparison.Ordinal))
                    {
                        var pin = line[4..].Trim();
                        return pin.Length >= 7 ? pin[..7] + " (pinned)" : pin;
                    }
                }
            }
            catch
            {
            }

            return "—";
        }

        return (Read("prepare-anyps5.sh"), Read("prepare-kyty.sh"));
    }

    /// <summary>Removes Build/RunHybrid. The game dump is never touched.</summary>
    public bool RemoveRuntime()
    {
        try
        {
            if (GameRunning)
            {
                SetStatus("Stop the game before removing the runtime");
                return false;
            }

            if (Directory.Exists(HybridDirectory))
            {
                Directory.Delete(HybridDirectory, true);
            }

            _installSize = "";
            SetStatus("Runtime removed");
            return true;
        }
        catch (Exception exception)
        {
            SetStatus($"Could not remove the runtime: {exception.Message}");
            return false;
        }
    }

    public string HardwareSummary()
    {
        var cpu = CpuName;
        var memory = MemoryLabel;
        var gpu = GpuName;
        var session = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE") ?? "unknown session";
        return $"{cpu}{(memory.Length > 0 ? $" · {memory} RAM" : "")}" +
               $"{(gpu.Length > 0 ? $"\nGPU: {gpu}" : "")}\nDisplay: {session}";
    }

    private async Task<ProcessResult> RunAsync(IEnumerable<string> arguments,
        IDictionary<string, string>? environment, Action<string>? onLine, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(Python)
        {
            WorkingDirectory = Root,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add(Script);
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }
        if (environment is not null)
        {
            foreach (var pair in environment)
            {
                start.Environment[pair.Key] = pair.Value;
            }
        }
        start.Environment["PYTHONUNBUFFERED"] = "1";

        var output = new StringBuilder();
        using var process = new Process { StartInfo = start };
        process.OutputDataReceived += (_, e) => Append(e.Data, output, onLine);
        process.ErrorDataReceived += (_, e) => Append(e.Data, output, onLine);
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
            }
            throw;
        }

        return new ProcessResult { ExitCode = process.ExitCode, Output = output.ToString() };
    }

    private void Emit(string? line)
    {
        if (line is not null)
        {
            GameOutput?.Invoke(line);
        }
    }

    private static void Append(string? line, StringBuilder output, Action<string>? onLine)
    {
        if (line is null)
        {
            return;
        }
        output.AppendLine(line);
        onLine?.Invoke(line);
    }

    private static IEnumerable<KeyValuePair<string, string>> ParseEnv(string text)
    {
        foreach (var raw in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var index = raw.IndexOf('=');
            if (index > 0)
            {
                yield return new KeyValuePair<string, string>(raw[..index].Trim(), raw[(index + 1)..].Trim());
            }
        }
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Tools", "linux", "astrohybrid.py")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }

        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Tools", "linux", "astrohybrid.py")))
            {
                return current.FullName;
            }
            current = current.Parent;
        }

        throw new InvalidOperationException("Cannot find the AstroHybrid project root (Tools/linux/astrohybrid.py).");
    }
}
