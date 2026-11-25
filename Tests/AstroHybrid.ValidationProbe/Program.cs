using System;
using System.Threading;
using AstroHybrid.App.Models;
using AstroHybrid.App.Services;

namespace AstroHybrid.ValidationProbe
{
    internal sealed class ProgressWriter : IProgress<ArchiveProgress>
    {
        private int _last = -1;
        public void Report(ArchiveProgress value)
        {
            if (value.Percent != 100 && value.Percent < _last + 5 && value.Percent >= _last) return;
            _last = value.Percent;
            Console.WriteLine($"ZAR {value.Percent}% {value.Message} ({value.ProcessedBytes}/{value.TotalBytes})");
        }
    }

    internal static class Program
    {
        private static int Main(string[] args)
        {
            if (args.Length != 1)
            {
                Console.Error.WriteLine("Usage: AstroHybrid.ValidationProbe <archive.zar>");
                return 64;
            }
            try
            {
                var info = new GameValidator().ValidateGameDumpAsync(args[0], new ProgressWriter(), CancellationToken.None)
                    .GetAwaiter().GetResult();
                Print(info);
                return info.IsValid ? 0 : 2;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                return 3;
            }
        }

        private static void Print(GameInfo info)
        {
            Console.WriteLine($"IsValid={info.IsValid}");
            Console.WriteLine($"TitleId={info.TitleId}");
            Console.WriteLine($"Version={info.Version}");
            Console.WriteLine($"EbootSize={info.EbootSize}");
            Console.WriteLine($"ValidationError={info.ValidationError}");
            Console.WriteLine($"Warning={info.Warning}");
        }
    }
}
