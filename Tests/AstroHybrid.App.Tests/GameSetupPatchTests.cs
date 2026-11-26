using System;
using System.IO;
using AstroHybrid.App.Services;
using Xunit;

namespace AstroHybrid.App.Tests
{
    public sealed class GameSetupPatchTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "AstroHybrid-GameSetup-" + Path.GetRandomFileName());
        private readonly string _runHybrid;

        public GameSetupPatchTests()
        {
            _runHybrid = Path.Combine(_root, "Build", "RunHybrid");
            Directory.CreateDirectory(_runHybrid);
        }

        [Fact]
        public void RepeatedFixed1080pStateDoesNotReadTheExecutableAgain()
        {
            string exe = Path.Combine(_runHybrid, "AstroBot.exe");
            byte[] original = { 0x44, 0x03, 0x73, 0x08, 0x44, 0x89, 0x73, 0x0C };
            File.WriteAllBytes(exe, original);
            var setup = new GameSetup(_root);
            Assert.True(setup.SetFixed1080pCap("AstroBot.exe", enable: true));

            using (File.Open(exe, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                Assert.False(setup.SetFixed1080pCap("AstroBot.exe", enable: true));
            }
        }

        [Fact]
        public void ChangedExecutableInvalidatesFixed1080pCache()
        {
            string exe = Path.Combine(_runHybrid, "AstroBot.exe");
            byte[] original = { 0x44, 0x03, 0x73, 0x08, 0x44, 0x89, 0x73, 0x0C };
            var setup = new GameSetup(_root);
            File.WriteAllBytes(exe, original);
            Assert.True(setup.SetFixed1080pCap("AstroBot.exe", enable: true));

            File.WriteAllBytes(exe, original);
            File.SetLastWriteTimeUtc(exe, DateTime.UtcNow.AddSeconds(1));
            Assert.True(setup.SetFixed1080pCap("AstroBot.exe", enable: true));
        }

        public void Dispose()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
    }
}
