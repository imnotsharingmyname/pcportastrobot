using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AstroHybrid.App.Services;
using Xunit;

namespace AstroHybrid.App.Tests
{
    public sealed class GameValidatorArchiveTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "astrohybrid-validator-zar-" + Guid.NewGuid().ToString("N"));

        [Fact]
        public async Task ValidStandaloneArchiveReportsMetadataSizeAndProgress()
        {
            var path = ZArchiveFixture.Create(_root, ZArchiveFixture.ValidFiles());
            var progress = new List<ArchiveProgress>();

            var result = await new GameValidator(minEbootSize: 4).ValidateGameDumpAsync(
                path, new ActionProgress<ArchiveProgress>(progress.Add));

            Assert.True(result.IsValid, result.ValidationError);
            Assert.Equal("PPSA21567", result.TitleId);
            Assert.StartsWith("01.018.000", result.Version);
            Assert.Equal(4, result.EbootSize);
            Assert.NotEmpty(progress);
            Assert.Equal(100, progress.Last().Percent);
        }

        [Theory]
        [InlineData("eboot.bin", "eboot.bin")]
        [InlineData("sce_module/libc.prx", "sce_module")]
        [InlineData("sce_sys/param.json", "sce_sys")]
        [InlineData("data/prein/payload.bin", "data")]
        public async Task MissingRequiredContentIsRejected(string entry, string expectedError)
        {
            var files = ZArchiveFixture.ValidFiles();
            files.Remove(entry);
            var path = ZArchiveFixture.Create(_root, files);

            var result = await new GameValidator(minEbootSize: 4).ValidateGameDumpAsync(path);

            Assert.False(result.IsValid);
            Assert.Contains(expectedError, result.ValidationError, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task TinyEbootIsRejected()
        {
            var path = ZArchiveFixture.Create(_root, ZArchiveFixture.ValidFiles(ebootSize: 4));
            var result = await new GameValidator(minEbootSize: 5).ValidateGameDumpAsync(path);
            Assert.False(result.IsValid);
            Assert.Contains("too small", result.ValidationError, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task WrappedApp0RootIsRejectedClearly()
        {
            var files = ZArchiveFixture.ValidFiles().ToDictionary(p => "app0/" + p.Key, p => p.Value);
            var path = ZArchiveFixture.Create(_root, files);
            var result = await new GameValidator(minEbootSize: 4).ValidateGameDumpAsync(path);
            Assert.False(result.IsValid);
            Assert.Contains("wrapped app0/", result.ValidationError);
        }

        [Fact]
        public async Task CorruptRequiredEntryFailsButOptionalDataIsNotRead()
        {
            var requiredPath = ZArchiveFixture.Create(_root, ZArchiveFixture.ValidFiles(128));
            ZArchiveFixture.CorruptCompressedBlock(requiredPath, 0);
            var required = await new GameValidator(minEbootSize: 4).ValidateGameDumpAsync(requiredPath);
            Assert.False(required.IsValid);

            var files = ZArchiveFixture.ValidFiles();
            files["eboot.bin"] = Enumerable.Range(0, ZArchiveFixture.BlockSize).Select(i => (byte)(i * 73)).ToArray();
            files["sce_module/libc.prx"] = Enumerable.Range(0, ZArchiveFixture.BlockSize).Select(i => (byte)(i * 31)).ToArray();
            files["sce_sys/param.json"] = Enumerable.Range(0, ZArchiveFixture.BlockSize).Select(i => (byte)(i * 19)).ToArray();
            // The three required files fill raw blocks 0-2; optional data alone occupies compressed block 3.
            var optionalPath = ZArchiveFixture.Create(_root, files, compressed: true);
            ZArchiveFixture.CorruptCompressedBlock(optionalPath, 3);
            var optional = await new GameValidator(minEbootSize: 4).ValidateGameDumpAsync(optionalPath);
            Assert.True(optional.IsValid, optional.ValidationError);
        }

        public void Dispose()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }
    }
}
