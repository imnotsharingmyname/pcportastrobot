using System.IO;
using System.Reflection;
using AstroHybrid.App.Services;
using Xunit;

namespace AstroHybrid.App.Tests
{
    public sealed class BuildManagerTests
    {
        [Fact]
        public void ConstructorDoesNotProbePython()
        {
            string root = Path.Combine(Path.GetTempPath(), "AstroHybrid-BuildManager-" + Path.GetRandomFileName());
            try
            {
                var manager = new BuildManager(root);
                var pythonPath = typeof(BuildManager)
                    .GetField("_pythonPath", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(manager);

                Assert.Null(pythonPath);
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            }
        }
    }
}
