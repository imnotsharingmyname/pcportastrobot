using System;
using System.Reflection;
using System.Runtime.InteropServices;
using AstroHybrid.App.Services;
using Xunit;

namespace AstroHybrid.App.Tests
{
    public sealed class ControllerDetectorTests
    {
        [Fact]
        public void WinMmCapabilitiesUseNativeLayoutAndPointerSizedId()
        {
            var detector = typeof(ControllerDetector);
            var caps = detector.GetNestedType("JOYCAPSW", BindingFlags.NonPublic)!;
            Assert.Equal(728, Marshal.SizeOf(caps));
            Assert.Equal(4, Marshal.OffsetOf(caps, "szPname").ToInt32());
            Assert.Equal(144, Marshal.OffsetOf(caps, "szRegKey").ToInt32());
            Assert.Equal(208, Marshal.OffsetOf(caps, "szOEMVxD").ToInt32());
            var method = detector.GetMethod("joyGetDevCapsW", BindingFlags.NonPublic | BindingFlags.Static)!;
            Assert.Equal(typeof(UIntPtr), method.GetParameters()[0].ParameterType);
            Assert.Equal(typeof(IntPtr), method.GetParameters()[1].ParameterType);
        }
    }
}
