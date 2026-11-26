using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AstroHybrid.App.Avalonia.Services;

if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException();
var root = Directory.CreateTempSubdirectory("astrohybrid-backend-test-").FullName;
try
{
    var scripts = Path.Combine(root, "Tools", "linux");
    Directory.CreateDirectory(scripts);
    var backend = new Backend(root);
    Directory.CreateDirectory(Path.Combine(backend.HybridDirectory, "libs"));
    foreach (var name in new[] { "AstroBot", "libs/kyty_gpu.so", "libs/libAstroHybridHost.prx" })
        File.WriteAllText(Path.Combine(backend.HybridDirectory, name), "");
    File.WriteAllText(backend.Script, """
import sys
import threading

def emit(stream, prefix):
    for n in range(2000):
        print(f"{prefix}{n}", file=stream, flush=True)

if sys.argv[1] == "run":
    print("final stdout", flush=True)
    print("final stderr", file=sys.stderr, flush=True)
    sys.exit(17)
a = threading.Thread(target=emit, args=(sys.stdout, "out-"))
b = threading.Thread(target=emit, args=(sys.stderr, "err-"))
a.start()
b.start()
a.join()
b.join()
""");

    var output = new ConcurrentQueue<string>();
    var states = new ConcurrentQueue<bool>();
    backend.GameOutput += output.Enqueue;
    var exited = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    backend.GameRunningChanged += running =>
    {
        states.Enqueue(running);
        if (!running) exited.TrySetResult(true);
    };
    backend.StartGame(new AppSettings { GamePath = root, DirectAudio = false });
    await exited.Task.WaitAsync(TimeSpan.FromSeconds(10));
    if (backend.GameRunning || backend.StatusMessage != "Game exited (code 17)")
        throw new Exception("Incorrect game exit state: " + backend.StatusMessage);
    if (!states.SequenceEqual(new[] { true, false }))
        throw new Exception("Invalid game state transition");
    if (!output.Contains("final stdout") || !output.Contains("final stderr"))
        throw new Exception("Final process output was lost");

    var result = await backend.CheckAsync(root).WaitAsync(TimeSpan.FromSeconds(10));
    var lines = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
    if (!result.Success || lines.Length != 4000 || lines.Distinct().Count() != 4000)
        throw new Exception("Concurrent setup output was lost or corrupted");
    Console.WriteLine("Linux backend regression checks passed.");
}
finally { Directory.Delete(root, true); }
