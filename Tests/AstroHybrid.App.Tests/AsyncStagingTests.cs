using AstroHybrid.App.Models;
using Xunit;

namespace AstroHybrid.App.Tests
{
    public sealed class AsyncStagingTests
    {
        [Theory]
        [InlineData("AMD", true)]
        [InlineData("amd", true)]
        [InlineData("NVIDIA", false)]
        [InlineData("Intel", false)]
        [InlineData(null, false)]
        public void AutomaticUsesSelectedVendor(string? vendor, bool expected)
        {
            Assert.Equal(expected, new AppSettings().EffectiveAsyncVulkanStaging(vendor));
        }

        [Fact]
        public void ExplicitOverridesSurviveAdapterChanges()
        {
            var settings = new AppSettings { AsyncVulkanStaging = false };
            Assert.False(settings.EffectiveAsyncVulkanStaging("AMD"));
            settings.AsyncVulkanStaging = true;
            Assert.True(settings.EffectiveAsyncVulkanStaging("NVIDIA"));
            settings.AsyncVulkanStaging = null;
            Assert.False(settings.EffectiveAsyncVulkanStaging("NVIDIA"));
            Assert.True(settings.EffectiveAsyncVulkanStaging("AMD"));
        }
    }
}
