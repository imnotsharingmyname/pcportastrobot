using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using AstroHybrid.App.Services;
using Xunit;

namespace AstroHybrid.App.Tests
{
    public sealed class ArchiveSetupTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "astrohybrid-archive-setup-" + Guid.NewGuid().ToString("N"));

        [Fact]
        public void MaterializeExtractsOnlySetupEntries()
        {
            var files = ZArchiveFixture.ValidFiles();
            files["sce_module/generated.guest.prx"] = new byte[] { 99 };
            files["args.txt"] = new byte[] { 1, 2 };
            files["param.sfx"] = new byte[] { 3, 4 };
            var archive = ZArchiveFixture.Create(Path.Combine(_root, "source"), files);
            var before = File.ReadAllBytes(archive);
            var output = Path.Combine(_root, "app", "Build", "ZarSource");
            var progress = new List<ArchiveProgress>();

            var staged = GameSource.MaterializeForSetup(archive, output,
                new ActionProgress<ArchiveProgress>(progress.Add), CancellationToken.None);

            Assert.Equal(output, staged);
            Assert.Equal(files["eboot.bin"], File.ReadAllBytes(Path.Combine(staged, "eboot.bin")));
            Assert.Equal(files["sce_module/libc.prx"], File.ReadAllBytes(Path.Combine(staged, "sce_module", "libc.prx")));
            Assert.Equal(files["sce_sys/param.json"], File.ReadAllBytes(Path.Combine(staged, "sce_sys", "param.json")));
            Assert.Equal(files["args.txt"], File.ReadAllBytes(Path.Combine(staged, "args.txt")));
            Assert.Equal(files["param.sfx"], File.ReadAllBytes(Path.Combine(staged, "param.sfx")));
            Assert.False(File.Exists(Path.Combine(staged, "sce_module", "generated.guest.prx")));
            Assert.False(Directory.Exists(Path.Combine(staged, "data")));
            Assert.Equal(100, progress.Last().Percent);
            Assert.Equal(before, File.ReadAllBytes(archive));
        }

        [Fact]
        public void BuildApp0LinksSelectedArchiveBesideApp0()
        {
            var archive = ZArchiveFixture.Create(Path.Combine(_root, "source"), ZArchiveFixture.ValidFiles());
            var before = File.ReadAllBytes(archive);
            var appRoot = Path.Combine(_root, "app");
            var dump = CreateDump(appRoot);
            InvokeBuildApp0(appRoot, dump, archive);

            var runtime = Path.Combine(appRoot, "Build", "RunHybrid");
            Assert.True(File.Exists(Path.Combine(runtime, "app0.zar")));
            Assert.Equal(before, File.ReadAllBytes(Path.Combine(runtime, "app0.zar")));
            Assert.Equal(before, File.ReadAllBytes(archive));
            Assert.True(File.Exists(Path.Combine(runtime, "app0", "eboot.bin")));
        }

        [Fact]
        public void ExistingRuntimeArchiveIsNotDeletedWhenItIsTheSource()
        {
            var appRoot = Path.Combine(_root, "app");
            var runtime = Path.Combine(appRoot, "Build", "RunHybrid");
            Directory.CreateDirectory(runtime);
            var archive = Path.Combine(runtime, "app0.zar");
            ZArchiveFixture.Write(archive, ZArchiveFixture.ValidFiles(), compressed: true);
            var before = File.ReadAllBytes(archive);

            InvokeBuildApp0(appRoot, CreateDump(appRoot), archive);

            Assert.Equal(before, File.ReadAllBytes(archive));
        }

        [Fact]
        public async Task GeneratedCleanupNeverTouchesSourceArchive()
        {
            var archive = ZArchiveFixture.Create(Path.Combine(_root, "source"), ZArchiveFixture.ValidFiles());
            var before = File.ReadAllBytes(archive);
            var appRoot = Path.Combine(_root, "app");
            var staged = GameSource.MaterializeForSetup(archive,
                Path.Combine(appRoot, "Build", "ZarSource"), null, CancellationToken.None);
            Directory.CreateDirectory(Path.Combine(appRoot, "Build", "RunHybrid"));
            Assert.True(Directory.Exists(staged));

            await new InstallationManager(appRoot).UninstallAsync();

            Assert.False(Directory.Exists(staged));
            Assert.Equal(before, File.ReadAllBytes(archive));
            Assert.Equal(Convert.ToBase64String(SHA256.Create().ComputeHash(before)),
                Convert.ToBase64String(SHA256.Create().ComputeHash(File.ReadAllBytes(archive))));
        }

        [Fact]
        public async Task SameRuntimeArchiveSurvivesUninstall()
        {
            var appRoot = Path.Combine(_root, "app");
            var runtime = Path.Combine(appRoot, "Build", "RunHybrid");
            Directory.CreateDirectory(runtime);
            var archive = Path.Combine(runtime, "app0.zar");
            ZArchiveFixture.Write(archive, ZArchiveFixture.ValidFiles(), compressed: true);
            var before = File.ReadAllBytes(archive);
            var settingsManager = new SettingsManager(appRoot);
            var settings = settingsManager.Load();
            settings.GameDumpPath = archive;
            settingsManager.Save(settings);

            await new InstallationManager(appRoot).UninstallAsync();

            Assert.True(File.Exists(archive));
            Assert.Equal(before, File.ReadAllBytes(archive));
        }

        [Fact]
        public void ArchiveInsideApp0IsRejectedBeforeCleanup()
        {
            var appRoot = Path.Combine(_root, "app");
            var archiveDirectory = Path.Combine(appRoot, "Build", "RunHybrid", "app0", "inside");
            Directory.CreateDirectory(archiveDirectory);
            var archive = Path.Combine(archiveDirectory, "selected.zar");
            ZArchiveFixture.Write(archive, ZArchiveFixture.ValidFiles(), compressed: true);
            var before = File.ReadAllBytes(archive);

            var error = Assert.Throws<TargetInvocationException>(() => InvokeBuildApp0(appRoot, CreateDump(appRoot), archive));

            Assert.IsType<InvalidOperationException>(error.InnerException);
            Assert.True(File.Exists(archive));
            Assert.Equal(before, File.ReadAllBytes(archive));
        }

        private string CreateDump(string appRoot)
        {
            var dump = Path.Combine(appRoot, "Build", "ZarSource");
            Directory.CreateDirectory(Path.Combine(dump, "sce_sys"));
            Directory.CreateDirectory(Path.Combine(dump, "sce_module"));
            Directory.CreateDirectory(Path.Combine(dump, "data"));
            File.WriteAllBytes(Path.Combine(dump, "eboot.bin"), new byte[] { 1, 2, 3, 4 });
            File.WriteAllBytes(Path.Combine(dump, "sce_module", "libc.prx"), new byte[] { 1 });
            File.WriteAllText(Path.Combine(dump, "sce_sys", "param.json"), "{}");
            return dump;
        }

        private static void InvokeBuildApp0(string appRoot, string dump, string archive)
        {
            var setup = new GameSetup(appRoot);
            var method = typeof(GameSetup).GetMethod("BuildApp0", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);
            var app0 = Path.Combine(appRoot, "Build", "RunHybrid", "app0");
            method!.Invoke(setup, new object[] { dump, app0, new Action<string>(_ => { }), archive });
        }

        public void Dispose()
        {
            GameSetup.DeleteTree(_root);
        }
    }
}
