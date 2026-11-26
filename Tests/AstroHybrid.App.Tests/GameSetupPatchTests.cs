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

        [Fact]
        public void Fixed1080pPatchMatchesAcrossReadBoundaryAndPreservesOtherBytes()
        {
            string exe = Path.Combine(_runHybrid, "AstroBot.exe");
            byte[] original = { 0x44, 0x03, 0x73, 0x08, 0x44, 0x89, 0x73, 0x0C };
            byte[] patched = { 0x45, 0x31, 0xF6, 0x90, 0x44, 0x89, 0x73, 0x0C };
            var data = new byte[128 * 1024];
            int offset = 64 * 1024 + 3;
            Buffer.BlockCopy(original, 0, data, offset, original.Length);
            File.WriteAllBytes(exe, data);

            var setup = new GameSetup(_root);
            Assert.True(setup.SetFixed1080pCap("AstroBot.exe", enable: true));
            var expected = (byte[])data.Clone();
            Buffer.BlockCopy(patched, 0, expected, offset, patched.Length);
            Assert.Equal(expected, File.ReadAllBytes(exe));

            Assert.True(setup.SetFixed1080pCap("AstroBot.exe", enable: false));
            Assert.Equal(data, File.ReadAllBytes(exe));
        }

        [Fact]
        public void Fixed1080pPatchDoesNotModifyDuplicateSignatures()
        {
            string exe = Path.Combine(_runHybrid, "AstroBot.exe");
            byte[] original = { 0x44, 0x03, 0x73, 0x08, 0x44, 0x89, 0x73, 0x0C };
            var data = new byte[128 * 1024];
            Buffer.BlockCopy(original, 0, data, 10, original.Length);
            Buffer.BlockCopy(original, 0, data, data.Length - original.Length, original.Length);
            File.WriteAllBytes(exe, data);

            Assert.False(new GameSetup(_root).SetFixed1080pCap("AstroBot.exe", enable: true));
            Assert.Equal(data, File.ReadAllBytes(exe));
        }

        public void Dispose()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
    }
}
