using System;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using AstroHybrid.App.Models;

namespace AstroHybrid.App.Services
{
    public class HardwareDetector
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct DisplayDevice
        {
            public int cb;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
            public int StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct DevMode
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
            public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
            public int dmFields, dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
            public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
            public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct MemoryStatus
        {
            public uint dwLength, dwMemoryLoad;
            public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern bool EnumDisplayDevices(string? device, int index, ref DisplayDevice info, int flags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern bool EnumDisplaySettings(string? device, int mode, ref DevMode info);

        [DllImport("kernel32.dll")]
        static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);

        public HardwareInfo DetectHardware()
        {
            var info = new HardwareInfo();

            // Detect GPU
            try
            {
                for (int i = 0; ; i++)
                {
                    var dd = new DisplayDevice { cb = Marshal.SizeOf(typeof(DisplayDevice)) };
                    if (!EnumDisplayDevices(null, i, ref dd, 0)) break;
                    if (i == 0 || (dd.StateFlags & 4) != 0)
                    {
                        info.GpuName = dd.DeviceString.Trim();
                    }
                    if ((dd.StateFlags & 4) != 0) break;
                }

                // Detect RTX series
                var match = Regex.Match(info.GpuName, @"RTX\s*(\d{1,2})\d{2}", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    info.RtxSeries = int.Parse(match.Groups[1].Value);
                }
                else if (info.GpuName.IndexOf("GTX", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    // GTX cards - no RTX features
                    info.RtxSeries = 0;
                }
                else if (info.GpuName.IndexOf("RX ", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         info.GpuName.IndexOf("Radeon", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    // AMD cards
                    info.RtxSeries = 0;
                }
                else if (info.GpuName.IndexOf("Intel", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         info.GpuName.IndexOf("Arc", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    // Intel cards
                    info.RtxSeries = 0;
                }
            }
            catch { }

            // Detect display settings
            try
            {
                var dm = new DevMode { dmSize = (short)Marshal.SizeOf(typeof(DevMode)) };
                if (EnumDisplaySettings(null, -1, ref dm) && dm.dmDisplayFrequency > 1)
                {
                    info.RefreshRate = dm.dmDisplayFrequency;
                    info.ScreenWidth = dm.dmPelsWidth;
                    info.ScreenHeight = dm.dmPelsHeight;
                }
                else
                {
                    // Fallback to screen bounds
                    info.ScreenWidth = (int)System.Windows.SystemParameters.PrimaryScreenWidth;
                    info.ScreenHeight = (int)System.Windows.SystemParameters.PrimaryScreenHeight;
                }
            }
            catch
            {
                info.ScreenWidth = 1920;
                info.ScreenHeight = 1080;
                info.RefreshRate = 60;
            }

            // Detect RAM
            try
            {
                var ms = new MemoryStatus { dwLength = (uint)Marshal.SizeOf(typeof(MemoryStatus)) };
                if (GlobalMemoryStatusEx(ref ms))
                {
                    info.RamGb = (int)Math.Round(ms.ullTotalPhys / 1073741824.0);
                }
            }
            catch { }

            // Detect CPU
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Name, NumberOfLogicalProcessors FROM Win32_Processor");
                foreach (var obj in searcher.Get())
                {
                    info.CpuName = obj["Name"]?.ToString()?.Trim() ?? "Unknown CPU";
                    if (obj["NumberOfLogicalProcessors"] != null)
                        info.CpuThreads = Convert.ToInt32(obj["NumberOfLogicalProcessors"]);
                    break;
                }
            }
            catch
            {
                info.CpuName = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "Unknown CPU";
                info.CpuThreads = Environment.ProcessorCount;
            }

            // Determine feature support based on GPU
            DetermineFeatureSupport(info);

            return info;
        }

        private void DetermineFeatureSupport(HardwareInfo info)
        {
            // NVIDIA RTX 20 series and newer: DLSS and Reflex
            if (info.RtxSeries >= 20)
            {
                info.SupportsDlss = true;
                info.SupportsReflex = true;
            }

            // NVIDIA RTX 40 series and newer: Frame Generation
            if (info.RtxSeries >= 40)
            {
                info.SupportsFrameGeneration = true;
                info.MaxFrameGenMultiplier = info.RtxSeries >= 50 ? 4 : 2; // RTX 50 supports up to 4x
            }

            // AMD FSR support would go here if implemented
            // Intel XeSS support would go here if implemented
        }

        public string GetBestSettingsSummary(HardwareInfo info)
        {
            var parts = new System.Collections.Generic.List<string>();

            parts.Add($"{info.GpuName}");
            parts.Add($"{info.CpuName} ({info.CpuThreads} threads)");
            parts.Add($"{info.RamGb} GB RAM");
            parts.Add($"{info.ScreenWidth}x{info.ScreenHeight} @ {info.RefreshRate} Hz");

            if (info.RtxSeries >= 50)
                parts.Add("DLSS + Multi Frame Gen (up to 4x) + Reflex");
            else if (info.RtxSeries >= 40)
                parts.Add("DLSS + Frame Gen (2x) + Reflex");
            else if (info.RtxSeries >= 20)
                parts.Add("DLSS + Reflex (no Frame Gen)");
            else
                parts.Add("Game's own upscaling (no NVIDIA features)");

            return string.Join(", ", parts);
        }
    }
}