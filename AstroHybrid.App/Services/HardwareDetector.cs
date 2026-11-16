using System;
using System.Collections.Generic;
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

            // Detect GPU: every adapter, not only the one that drives the desktop. On a laptop the
            // desktop adapter is usually the processor's integrated graphics, while the discrete
            // card (RTX 4050) is a second adapter that never shows up in EnumDisplayDevices (#24).
            try
            {
                info.Gpus = DetectGpus();
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

            return info;
        }

        // Every adapter Windows knows about, with integrated/discrete classified and the NVIDIA
        // features resolved per card. Win32_VideoController lists adapters that are not connected to
        // a display, which is exactly the discrete card of a hybrid-graphics laptop.
        private static List<GpuAdapter> DetectGpus()
        {
            var adapters = new List<GpuAdapter>();
            string primaryName = PrimaryDisplayAdapterName();

            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT Name, AdapterCompatibility, DriverVersion, PNPDeviceID FROM Win32_VideoController");
                foreach (var obj in searcher.Get())
                {
                    var name = obj["Name"]?.ToString()?.Trim() ?? "";
                    var pnp = obj["PNPDeviceID"]?.ToString()?.Trim() ?? "";
                    if (name.Length == 0 || IsVirtualAdapter(name, pnp)) continue;

                    var gpu = new GpuAdapter
                    {
                        Name = name,
                        PnpDeviceId = pnp,
                        DriverVersion = obj["DriverVersion"]?.ToString()?.Trim() ?? "",
                        Vendor = VendorOf(obj["AdapterCompatibility"]?.ToString(), pnp, name)
                    };
                    ClassifyGpu(gpu);
                    ApplyFeatureSupport(gpu);

                    // Windows can list one card twice (two drivers bound to it); keep the first.
                    if (adapters.Exists(a => SameAdapter(a.Name, gpu.Name))) continue;
                    adapters.Add(gpu);
                }
            }
            catch { }

            // No WMI (service disabled, lockdown): fall back to the display adapter, the old result.
            if (adapters.Count == 0 && primaryName.Length > 0)
            {
                var gpu = new GpuAdapter { Name = primaryName, IsPrimaryDisplay = true };
                gpu.Vendor = VendorOf("", "", primaryName);
                ClassifyGpu(gpu);
                ApplyFeatureSupport(gpu);
                adapters.Add(gpu);
            }

            if (primaryName.Length > 0)
            {
                var primary = adapters.Find(g => SameAdapter(g.Name, primaryName));
                if (primary != null) primary.IsPrimaryDisplay = true;
            }

            if (adapters.Count == 0)
            {
                var gpu = new GpuAdapter { Name = "Unknown GPU" };
                adapters.Add(gpu);
            }

            return adapters;
        }

        // Name of the adapter Windows uses for the desktop (the integrated one on most laptops).
        private static string PrimaryDisplayAdapterName()
        {
            string attached = "";
            try
            {
                for (int i = 0; ; i++)
                {
                    var dd = new DisplayDevice { cb = Marshal.SizeOf(typeof(DisplayDevice)) };
                    if (!EnumDisplayDevices(null, i, ref dd, 0)) break;
                    var name = dd.DeviceString?.Trim() ?? "";
                    if (name.Length == 0) continue;
                    if ((dd.StateFlags & 4) != 0) return name;      // DISPLAY_DEVICE_PRIMARY_DEVICE
                    if ((dd.StateFlags & 1) != 0 && attached.Length == 0)
                        attached = name;                            // DISPLAY_DEVICE_ATTACHED_TO_DESKTOP
                }
            }
            catch { }
            return attached;
        }

        // Display drivers that are not a card the game can render on: Parsec, indirect display
        // drivers (IDD), remote sessions, USB displays and Microsoft's fallback adapter all show up
        // in Win32_VideoController as software devices (ROOT\...).
        private static bool IsVirtualAdapter(string name, string pnp)
        {
            if (pnp.StartsWith(@"ROOT\", StringComparison.OrdinalIgnoreCase)) return true;
            var n = name.ToLowerInvariant();
            foreach (var word in new[]
            {
                "virtual", "parsec", "remote", "indirect", "basic render", "basic display",
                "spacedesk", "usb display", "displaylink", "idd"
            })
            {
                if (n.Contains(word)) return true;
            }
            return false;
        }

        private static string VendorOf(string? compatibility, string pnp, string name)
        {
            var c = (compatibility ?? "").ToLowerInvariant();
            if (c.Contains("nvidia")) return "NVIDIA";
            if (c.Contains("advanced micro devices") || c.Contains("amd")) return "AMD";
            if (c.Contains("intel")) return "Intel";
            if (pnp.IndexOf("VEN_10DE", StringComparison.OrdinalIgnoreCase) >= 0) return "NVIDIA";
            if (pnp.IndexOf("VEN_1002", StringComparison.OrdinalIgnoreCase) >= 0) return "AMD";
            if (pnp.IndexOf("VEN_8086", StringComparison.OrdinalIgnoreCase) >= 0) return "Intel";

            var n = name.ToLowerInvariant();
            if (n.Contains("nvidia") || n.Contains("geforce") || n.Contains("quadro")) return "NVIDIA";
            if (n.Contains("amd") || n.Contains("radeon") || n.Contains("ati ")) return "AMD";
            if (n.Contains("intel")) return "Intel";
            return "";
        }

        // Integrated or discrete. The renderer prefers a discrete card over integrated graphics, so
        // this decides which card the default settings describe (and what the GPU dropdown says).
        private static void ClassifyGpu(GpuAdapter gpu)
        {
            var n = gpu.Name.ToLowerInvariant();

            if (gpu.Vendor == "NVIDIA")
            {
                gpu.IsDiscrete = true;
            }
            else if (gpu.Vendor == "Intel")
            {
                // Arc is a discrete card; UHD, HD, Iris and Iris Xe are the processor's graphics.
                gpu.IsDiscrete = n.Contains("arc");
                gpu.IsIntegrated = !gpu.IsDiscrete;
            }
            else if (gpu.Vendor == "AMD")
            {
                // A Radeon RX / Pro / FirePro card is discrete; "Radeon(TM) Graphics", Vega 8 and
                // the 700M/800M series are the processor's integrated graphics.
                bool card = n.Contains("rx ") || n.Contains("rx5") || n.Contains("rx6") ||
                            n.Contains("rx7") || n.Contains("rx9") || n.Contains("pro w") ||
                            n.Contains("firepro") || n.Contains("radeon pro");
                gpu.IsDiscrete = card;
                gpu.IsIntegrated = !card;
            }
        }

        private static void ApplyFeatureSupport(GpuAdapter gpu)
        {
            // NVIDIA RTX 20 series and newer: DLSS and Reflex
            var match = Regex.Match(gpu.Name, @"RTX\s*(\d{1,2})\d{2}", RegexOptions.IgnoreCase);
            if (match.Success && int.TryParse(match.Groups[1].Value, out var series))
            {
                gpu.RtxSeries = series;
            }

            if (gpu.RtxSeries >= 20)
            {
                gpu.SupportsDlss = true;
                gpu.SupportsReflex = true;
            }

            // NVIDIA RTX 40 series and newer: Frame Generation
            if (gpu.RtxSeries >= 40)
            {
                gpu.SupportsFrameGeneration = true;
                gpu.MaxFrameGenMultiplier = gpu.RtxSeries >= 50 ? 4 : 2; // RTX 50 supports up to 4x
            }

            // AMD FSR support would go here if implemented
            // Intel XeSS support would go here if implemented
        }

        private static bool SameAdapter(string a, string b)
        {
            var left = OnlyLettersAndDigits(a);
            var right = OnlyLettersAndDigits(b);
            if (left.Length == 0 || right.Length == 0) return false;
            return left.Contains(right) || right.Contains(left);
        }

        private static string OnlyLettersAndDigits(string text)
        {
            var buffer = new System.Text.StringBuilder(text.Length);
            foreach (var c in text)
            {
                if (char.IsLetterOrDigit(c)) buffer.Append(char.ToLowerInvariant(c));
            }
            return buffer.ToString();
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
