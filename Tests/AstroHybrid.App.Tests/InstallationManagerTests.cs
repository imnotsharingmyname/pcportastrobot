using System;
using System.IO;
using System.Text;
using AstroHybrid.App.Services;
using Xunit;

namespace AstroHybrid.App.Tests
{
    public sealed class InstallationManagerTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "AstroHybrid-InstallManager-" + Path.GetRandomFileName());

        public InstallationManagerTests()
        {
            Directory.CreateDirectory(Path.Combine(_root, "Build", "RunHybrid"));
        }

        [Fact]
        public void KytyVersionMarkerIsFoundAcrossReadChunkBoundaries()
        {
            const string marker = "u59-chunk-boundary-test";
            var data = new byte[192 * 1024];
            byte[] markerBytes = Encoding.ASCII.GetBytes(marker);
            const int markerOffset = 64 * 1024 - 16;
            Buffer.BlockCopy(markerBytes, 0, data, markerOffset, markerBytes.Length);
            string dll = Path.Combine(_root, "Build", "RunHybrid", "kyty_gpu.dll");
            File.WriteAllBytes(dll, data);

            Assert.Equal(marker, InstallationManager.ReadKytyVersion(_root));
        }

        [Fact]
        public void MaximumLengthKytyVersionMarkerSurvivesChunkBoundary()
        {
            string marker = "u59-" + new string('x', 80);
            var data = new byte[128 * 1024];
            byte[] markerBytes = Encoding.ASCII.GetBytes(marker);
            const int markerOffset = 64 * 1024 - 83;
            Buffer.BlockCopy(markerBytes, 0, data, markerOffset, markerBytes.Length);
            string dll = Path.Combine(_root, "Build", "RunHybrid", "kyty_gpu.dll");
            File.WriteAllBytes(dll, data);

            Assert.Equal(marker, InstallationManager.ReadKytyVersion(_root));
        }

        [Fact]
        public void MaximumLengthMarkerEndingAtNonfinalChunkBoundaryIsNotTruncated()
        {
            string marker = "u59-" + new string('y', 80);
            var data = new byte[128 * 1024];
            byte[] markerBytes = Encoding.ASCII.GetBytes(marker);
            int markerOffset = 64 * 1024 - markerBytes.Length;
            Buffer.BlockCopy(markerBytes, 0, data, markerOffset, markerBytes.Length);
            string dll = Path.Combine(_root, "Build", "RunHybrid", "kyty_gpu.dll");
            File.WriteAllBytes(dll, data);

            Assert.Equal(marker, InstallationManager.ReadKytyVersion(_root));
        }

        public void Dispose()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
    }
}
