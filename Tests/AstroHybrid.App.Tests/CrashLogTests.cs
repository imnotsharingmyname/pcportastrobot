using System;
using System.IO;
using System.Text;
using AstroHybrid.App.ViewModels;
using Xunit;

namespace AstroHybrid.App.Tests
{
    public sealed class CrashLogTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "AstroHybrid-crashlog-" + Guid.NewGuid().ToString("N"));

        public CrashLogTests() => Directory.CreateDirectory(_root);

        [Fact]
        public void SmallLogIsReadInFull()
        {
            var path = Path.Combine(_root, "small.log");
            const string message = "startup\nErrorDeviceLost\n";
            File.WriteAllText(path, message);
            Assert.Equal(message, MainWindowViewModel.ReadCrashLogSample(path));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void LargeLogKeepsStartupAndExitDiagnostics(bool utf16)
        {
            var path = Path.Combine(_root, "large.log");
            Encoding encoding = utf16 ? Encoding.Unicode : new UTF8Encoding(false);
            File.WriteAllText(path, "startup marker\n" + new string('x', 400000) +
                "\nMIDDLE_SENTINEL\n" + new string('y', 400000) + "\nErrorDeviceLost\n", encoding);
            var sample = MainWindowViewModel.ReadCrashLogSample(path);
            Assert.Contains("startup marker", sample);
            Assert.Contains("ErrorDeviceLost", sample);
            Assert.DoesNotContain("MIDDLE_SENTINEL", sample);
            Assert.True(sample.Length < 300000);
        }

        public void Dispose() => Directory.Delete(_root, true);
    }
}
