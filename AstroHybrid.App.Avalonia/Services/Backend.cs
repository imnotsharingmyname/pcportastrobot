using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
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
    public bool GameSpeed { get; set; } = true;
    public bool PadMotion { get; set; }
    public bool DirectAudio { get; set; } = true;
    public string AudioSink { get; set; } = "";
    public string ExtraEnv { get; set; } = "";
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
    private readonly AppSettingsStore _store = new();

    public event Action<bool>? GameRunningChanged;
    public event Action<string>? GameOutput;

    public Backend()
    {
        Root = FindRoot();
        Script = Path.Combine(Root, "Tools", "linux", "astrohybrid.py");
        Python = "python3";
    }

    public AppSettings LoadSettings() => _store.Load();

    public void SaveSettings(AppSettings settings) => _store.Save(settings);

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
        if (settings.GameSpeed)
        {
            arguments.Add("--env");
            arguments.Add("ASTROHYBRID_GAME_SPEED");
            arguments.Add("1");
        }
        if (settings.PadMotion)
        {
            arguments.Add("--env");
            arguments.Add("ASTROHYBRID_PAD_MOTION");
            arguments.Add("1");
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
            GameRunningChanged?.Invoke(false);
            process.Dispose();
            if (ReferenceEquals(_game, process))
            {
                _game = null;
            }
        };
        _game = process;
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        GameRunningChanged?.Invoke(true);
    }

    public void StopGame()
    {
        var process = _game;
        if (process is null)
        {
            return;
        }

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

    public IReadOnlyList<string> DetectGamePads()
    {
        var result = new List<string>();
        try
        {
            var devices = File.ReadAllText("/proc/bus/input/devices");
            foreach (var block in devices.Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
            {
                if (block.Contains("js", StringComparison.Ordinal) &&
                    (block.Contains("Steam", StringComparison.OrdinalIgnoreCase) ||
                     block.Contains("Controller", StringComparison.OrdinalIgnoreCase) ||
                     block.Contains("Pad", StringComparison.OrdinalIgnoreCase)))
                {
                    var name = block.Split('\n').FirstOrDefault(line => line.StartsWith("N: Name=", StringComparison.Ordinal));
                    if (name is not null)
                    {
                        result.Add(name.Replace("N: Name=", "").Trim('"'));
                    }
                }
            }
        }
        catch
        {
            // /proc is optional outside Linux; the page simply shows no pads.
        }

        return result.Distinct().ToList();
    }

    public string HardwareSummary()
    {
        var cpu = "Unknown CPU";
        var memory = "";
        try
        {
            foreach (var line in File.ReadLines("/proc/cpuinfo"))
            {
                if (line.StartsWith("model name", StringComparison.Ordinal))
                {
                    cpu = line.Split(':', 2)[1].Trim();
                    break;
                }
            }
        }
        catch
        {
        }

        try
        {
            foreach (var line in File.ReadLines("/proc/meminfo"))
            {
                if (line.StartsWith("MemTotal:", StringComparison.Ordinal))
                {
                    var kb = double.Parse(line.Split(':')[1].Trim().Split(' ')[0]);
                    memory = $"{kb / 1024 / 1024:0.0} GiB";
                    break;
                }
            }
        }
        catch
        {
        }

        var gpu = "";
        try
        {
            var process = new ProcessStartInfo("lspci")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
            };
            process.ArgumentList.Add("-nn");
            using var lspci = Process.Start(process);
            if (lspci is not null)
            {
                var output = lspci.StandardOutput.ReadToEnd();
                lspci.WaitForExit(1000);
                gpu = output.Split('\n')
                    .FirstOrDefault(line => line.Contains("VGA", StringComparison.Ordinal) ||
                                            line.Contains("3D controller", StringComparison.Ordinal))
                    ?.Split(':').Last().Trim() ?? "";
            }
        }
        catch
        {
        }

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
