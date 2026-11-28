using AstroHybrid.App.Models;
using AstroHybrid.App.Services;
using Xunit;

namespace AstroHybrid.App.Tests
{
    public sealed class UpscalerRecommendationTests
    {
        [Theory]
        [InlineData(2, 0)]  // 1920x1080
        [InlineData(1, 1)]  // 1600x900
        [InlineData(3, 1)]  // 2560x1440
        [InlineData(4, 1)]  // 2560x1080 ultrawide
        [InlineData(-1, 0)] // Invalid saved size falls back to 1920x1080
        public void Desktop2080SuperUsesGameUpscalingAt1080p(int size, int expected)
        {
            var gpu = Nvidia("NVIDIA GeForce RTX 2080 SUPER", @"PCI\VEN_10DE&DEV_1E81&SUBSYS_00000000\0");
            var pc = new HardwareInfo { Gpus = { gpu } };

            Assert.Equal(expected, HardwareDetector.RecommendUpscaler(pc, size));
            Assert.True(pc.SupportsDlss);
            Assert.True(pc.SupportsReflex);
        }

        [Theory]
        [InlineData("NVIDIA GeForce RTX 2080 SUPER", "", 0)]
        [InlineData("NVIDIA GeForce RTX 2080 SUPER with Max-Q Design", "", 1)]
        [InlineData("NVIDIA GeForce RTX 2080 SUPER", @"PCI\VEN_10DE&DEV_1E93&SUBSYS_00000000\0", 1)]
        [InlineData("NVIDIA GeForce RTX 2080", "", 1)]
        [InlineData("NVIDIA GeForce RTX 2080 Ti", "", 1)]
        public void NameFallbackMatchesOnlyTheDesktopModelWhenPciIdIsMissing(string name, string pnpDeviceId, int expected)
        {
            var pc = new HardwareInfo { Gpus = { Nvidia(name, pnpDeviceId) } };

            Assert.Equal(expected, HardwareDetector.RecommendUpscaler(pc, 2));
        }

        [Fact]
        public void UsesTheSelectedAdapterOnMultiGpuSystems()
        {
            var rtx = Nvidia("NVIDIA GeForce RTX 2080 SUPER", @"PCI\VEN_10DE&DEV_1E81&SUBSYS_00000000\0");
            var arc = new GpuAdapter { Vendor = "Intel", SupportsXess = true, IsDiscrete = true };
            var pc = new HardwareInfo { Gpus = { arc, rtx } };

            Assert.Equal(0, HardwareDetector.RecommendUpscaler(pc, 2));

            pc.SelectGpu(0);

            Assert.Equal(3, HardwareDetector.RecommendUpscaler(pc, 2));
        }

        private static GpuAdapter Nvidia(string name, string pnpDeviceId) => new GpuAdapter
        {
            Name = name,
            PnpDeviceId = pnpDeviceId,
            Vendor = "NVIDIA",
            IsDiscrete = true,
            RtxSeries = 20,
            SupportsDlss = true,
            SupportsReflex = true
        };
    }
}
