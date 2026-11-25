using System.IO;
using AstroHybrid.App.Services;
using Xunit;

namespace AstroHybrid.App.Tests
{
    public sealed class GameSourceTests
    {
        [Theory]
        [InlineData("game.ZAR")]
        [InlineData("  \"C:\\Astro Bot\\game.zar\"  ")]
        public void IsArchiveAcceptsStandaloneArchivePaths(string path)
        {
            Assert.True(GameSource.IsArchive(path));
        }

        [Fact]
        public void ExistsDistinguishesArchiveFilesAndDumpFolders()
        {
            var root = Path.Combine(Path.GetTempPath(), "astrohybrid-source-" + System.Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(root);
                var archive = Path.Combine(root, "game.ZAR");
                File.WriteAllBytes(archive, new byte[] { 1 });
                var dump = Path.Combine(root, "dump");
                Directory.CreateDirectory(dump);
                File.WriteAllBytes(Path.Combine(dump, "eboot.bin"), new byte[] { 1 });

                Assert.True(GameSource.Exists(archive));
                Assert.True(GameSource.Exists(dump));
                Assert.True(GameSource.Exists("  \"" + archive + "\"  "));
                Assert.False(GameSource.Exists(root));
                Assert.False(GameSource.Exists(Path.Combine(root, "missing.zar")));
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }
    }
}
