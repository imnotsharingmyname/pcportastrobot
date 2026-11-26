using System;
using System.Collections.Generic;
using System.IO;
using AstroHybrid.App.Services;
using Xunit;

namespace AstroHybrid.App.Tests
{
    public sealed class ShaderPrepTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "astrohybrid-shader-test-" + Guid.NewGuid().ToString("N"));

        public ShaderPrepTests()
        {
            Directory.CreateDirectory(Path.Combine(_root, "PipelineLists"));
            File.WriteAllText(ShaderPrep.ListPath(_root), "list");
            File.WriteAllText(Path.Combine(_root, "AstroBot.exe"), "standard");
            File.WriteAllText(Path.Combine(_root, "AstroBot-NonRT-SoftAssert.exe"), "nonrt");
        }

        private static Dictionary<string, string> Variables(string gpu = "GPU-A", string exe = "AstroBot.exe") =>
            new Dictionary<string, string>
            {
                ["ANYPS5_GPU"] = gpu,
                ["ASTROHYBRID_EXE"] = exe,
                ["ASTROHYBRID_SHADER_PREP"] = "1",
                ["ASTROHYBRID_SHADER_LIST"] = "1",
                ["ASTROHYBRID_SHADER_CACHE"] = "1"
            };

        [Fact]
        public void SelectedExecutableAndGpuInvalidateMarker()
        {
            var settings = Variables();
            Assert.Equal(Path.Combine(_root, "AstroBot.exe"), ShaderPrep.ExecutablePath(_root, settings));
            Assert.True(ShaderPrep.Needed(_root, settings));
            ShaderPrep.MarkDone(_root, settings);
            Assert.False(ShaderPrep.Needed(_root, settings));
            Assert.True(ShaderPrep.Needed(_root, Variables("GPU-B")));
            Assert.True(ShaderPrep.Needed(_root, Variables(exe: "AstroBot-NonRT-SoftAssert.exe")));
            File.AppendAllText(Path.Combine(_root, "AstroBot.exe"), "changed");
            Assert.True(ShaderPrep.Needed(_root, settings));
        }

        [Fact]
        public void DisabledCacheAndMissingSelectedExecutableSkipPreparation()
        {
            var settings = Variables();
            settings["ASTROHYBRID_SHADER_CACHE"] = "0";
            Assert.False(ShaderPrep.Needed(_root, settings));
            settings["ASTROHYBRID_SHADER_CACHE"] = "1";
            settings["ASTROHYBRID_EXE"] = "missing.exe";
            Assert.Null(ShaderPrep.ExecutablePath(_root, settings));
            Assert.False(ShaderPrep.Needed(_root, settings));
        }

        public void Dispose() => Directory.Delete(_root, recursive: true);
    }
}
