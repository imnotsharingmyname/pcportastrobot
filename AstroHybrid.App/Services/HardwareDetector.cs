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
                    gpu.VramMb = VideoMemoryMb(pnp);

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

        // Dedicated video memory of the card with this PNP device ID, in MiB, from its display driver key
        // (HardwareInformation.qwMemorySize; Win32_VideoController.AdapterRAM stops at 4 GB). 0 when unknown.
        private static int VideoMemoryMb(string pnpDeviceId)
        {
            if (string.IsNullOrEmpty(pnpDeviceId)) return 0;
            try
            {
                var device = pnpDeviceId.Split('\\');
                var hardwareId = device.Length >= 2 ? (device[0] + "\\" + device[1]).ToLowerInvariant() : pnpDeviceId.ToLowerInvariant();
                using var displayClass = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
                if (displayClass == null) return 0;
                foreach (var subName in displayClass.GetSubKeyNames())
                {
                    using var key = displayClass.OpenSubKey(subName);
                    var matching = (key?.GetValue("MatchingDeviceId") as string ?? "").ToLowerInvariant();
                    if (matching.Length == 0 || !hardwareId.StartsWith(matching, StringComparison.Ordinal)) continue;
                    var value = key?.GetValue("HardwareInformation.qwMemorySize");
                    long bytes = value is long l ? l : value is byte[] b && b.Length >= 8 ? BitConverter.ToInt64(b, 0) : 0;
                    if (bytes > 0) return (int)(bytes / (1024 * 1024));
                }
            }
            catch { }
            return 0;
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
                // "Arc" alone does not mean a card: Meteor Lake, Lunar Lake, Arrow Lake and Panther
                // Lake processors call their graphics Arc too ("Arc Graphics", "Arc 140V", "Arc B390",
                // as in the MSI Claw 8 AI+). The discrete cards are the A3xx-A7xx (Alchemist),
                // B5xx-B7xx (Battlemage) and Arc Pro models; UHD, HD and Iris are integrated.
                gpu.IsDiscrete = IsDiscreteIntel(gpu.Name, gpu.PnpDeviceId);
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

            // Intel XeSS Super Resolution (Vulkan): every GPU with DP4a. XeSS frame generation and
            // Xe Low Latency exist only for D3D12, so this Vulkan port has no Intel frame generation.
            ApplyXessSupport(gpu);
        }

        // PCI device IDs of Intel's discrete GPUs: DG1 (Iris Xe MAX, 0x4905-0x4909), Alchemist
        // (0x56xx) and Battlemage (0xE2xx). Everything else from Intel is the processor's graphics.
        internal static bool IsDiscreteIntel(string name, string pnp)
        {
            var id = Regex.Match(pnp ?? "", @"DEV_([0-9A-F]{4})", RegexOptions.IgnoreCase);
            if (id.Success)
            {
                int dev = Convert.ToInt32(id.Groups[1].Value, 16);
                return (dev >= 0x4905 && dev <= 0x4909) || (dev & 0xFF00) == 0x5600 || (dev & 0xFF00) == 0xE200;
            }
            // No PCI ID: the model number. A3xx-A7xx (with M for laptops) and B5xx-B9xx are cards,
            // Arc Pro is a card; B3xx (Panther Lake), 1x0V/T and plain "Arc Graphics" are integrated.
            var n = name ?? "";
            if (Regex.IsMatch(n, @"Arc(?:\(TM\))?\s+Pro\b", RegexOptions.IgnoreCase)) return true;
            if (Regex.IsMatch(n, @"Arc(?:\(TM\))?\s+A[3-7]\d{2}M?\b", RegexOptions.IgnoreCase)) return true;
            if (Regex.IsMatch(n, @"Arc(?:\(TM\))?\s+B[5-9]\d{2}\b", RegexOptions.IgnoreCase)) return true;
            return n.IndexOf("Iris Xe MAX", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // XeSS needs DP4a (shader model 6.4 class hardware). Older generations are left out so the
        // launcher does not offer it there; the renderer falls back by itself in any case.
        internal static void ApplyXessSupport(GpuAdapter gpu)
        {
            var n = gpu.Name.ToLowerInvariant();
            switch (gpu.Vendor)
            {
                case "Intel":
                    // Gen12 (Xe-LP) and newer: Iris Xe, UHD 7xx, "Intel Graphics" (Meteor Lake) and Arc.
                    // Gen9/Gen11 (HD, UHD 6xx, Iris Plus/Pro) have no DP4a.
                    bool gen9 = n.Contains("hd graphics") || Regex.IsMatch(n, @"uhd graphics\s*6\d{2}") ||
                                n.Contains("iris(r) plus") || n.Contains("iris plus") || n.Contains("iris pro");
                    gpu.SupportsXess = !gen9;
                    // XMX: Arc cards, and Arc integrated graphics from Lunar Lake on (Xe2/Xe3: 1x0V,
                    // B3xx). Meteor Lake's "Arc Graphics" (Xe-LPG) uses the DP4a path.
                    gpu.XessXmx = gpu.IsDiscrete || Regex.IsMatch(n, @"arc(?:\(tm\))?\s+(?:b\d{3}|1\d0[vt])");
                    break;
                case "NVIDIA":
                    // Pascal (GTX 10) and newer; Maxwell and Kepler (GTX 9xx/7xx, GT 7xx) have no DP4a.
                    gpu.SupportsXess = !Regex.IsMatch(n, @"gtx\s*(?:[4-9]\d{2})\b") && !n.Contains("gt 7");
                    break;
                case "AMD":
                    // RDNA and newer; the old Radeon R5/R7/R9 and HD series are GCN 1-3.
                    gpu.SupportsXess = !Regex.IsMatch(n, @"radeon\s*(?:\(tm\)\s*)?(?:r[579]|hd)\b");
                    break;
                default:
                    gpu.SupportsXess = false;
                    break;
            }
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
            else if (info.GpuVendor == "Intel" && info.SupportsXess)
                parts.Add(info.XessXmx ? "Intel XeSS (XMX)" : "Intel XeSS");
            else
                parts.Add("Game's own upscaling (no NVIDIA features)");

            return string.Join(", ", parts);
        }
    }
}
