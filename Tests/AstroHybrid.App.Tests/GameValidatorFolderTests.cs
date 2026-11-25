using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using AstroHybrid.App.Services;
using Xunit;

namespace AstroHybrid.App.Tests
{
    public sealed class GameValidatorFolderTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "astrohybrid-validator-folder-" + Guid.NewGuid().ToString("N"));

        [Fact]
        public async Task FolderDumpStillValidates()
        {
            var dump = CreateDump("PPSA21567-folder", withData: true);
            var result = await new GameValidator(minEbootSize: 4).ValidateGameDumpAsync(dump);
            Assert.True(result.IsValid, result.ValidationError);
            Assert.Equal("PPSA21567", result.TitleId);
            Assert.StartsWith("01.018.000", result.Version);
        }

        [Fact]
        public async Task FolderDumpWhoseNameEndsInZarStillValidates()
        {
            var dump = CreateDump("PPSA21567-folder.zar", withData: true);
            Assert.True(GameSource.Exists(dump));
            var result = await new GameValidator(minEbootSize: 4).ValidateGameDumpAsync(dump);
            Assert.True(result.IsValid, result.ValidationError);
        }

        [Fact]
        public async Task FolderDumpKeepsMissingDataError()
        {
            var dump = CreateDump("PPSA21567-missing", withData: false);
            var result = await new GameValidator(minEbootSize: 4).ValidateGameDumpAsync(dump);
            Assert.False(result.IsValid);
            Assert.Equal("Neither a 'data' directory nor 'app0.zar' was found in the game dump.", result.ValidationError);
        }

        private string CreateDump(string name, bool withData)
        {
            var dump = Path.Combine(_root, name);
            Directory.CreateDirectory(Path.Combine(dump, "sce_module"));
            Directory.CreateDirectory(Path.Combine(dump, "sce_sys"));
            if (withData) Directory.CreateDirectory(Path.Combine(dump, "data"));
            File.WriteAllBytes(Path.Combine(dump, "eboot.bin"), new byte[] { 1, 2, 3, 4 });
            File.WriteAllText(Path.Combine(dump, "sce_module", "libc.prx"), "module");
            File.WriteAllText(Path.Combine(dump, "sce_sys", "param.json"),
                "{\"titleId\":\"PPSA21567\",\"contentVersion\":\"01.018.000\"}", Encoding.UTF8);
            return dump;
        }

        public void Dispose()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }
    }
}
