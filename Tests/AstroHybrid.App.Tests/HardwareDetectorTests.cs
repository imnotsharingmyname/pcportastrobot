using AstroHybrid.App.Services;
using Xunit;

namespace AstroHybrid.App.Tests
{
    public sealed class HardwareDetectorTests
    {
        [Theory]
        [InlineData("NVIDIA RTX PRO 4000 Blackwell Laptop GPU", 50)]
        [InlineData("NVIDIA RTX 4000 PRO Blackwell - Laptop", 50)]
        [InlineData("NVIDIA RTX A5000", 30)]
        [InlineData("NVIDIA RTX 4000 Ada Generation", 40)]
        [InlineData("NVIDIA Quadro RTX 6000", 20)]
        [InlineData("NVIDIA GeForce RTX 2080 SUPER", 20)]
        [InlineData("NVIDIA GeForce RTX 4090", 40)]
        [InlineData("NVIDIA GeForce RTX 5060", 50)]
        [InlineData("NVIDIA GeForce GTX 1660", 0)]
        public void DetectsRtxArchitecture(string name, int expected)
        {
            Assert.Equal(expected, HardwareDetector.NvidiaRtxSeries(name));
        }
    }
}
