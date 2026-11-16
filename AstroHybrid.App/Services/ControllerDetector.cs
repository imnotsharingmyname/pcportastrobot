using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using AstroHybrid.App.Models;

namespace AstroHybrid.App.Services
{
    public class ControllerDetector
    {
        public List<ControllerInfo> DetectControllers()
        {
            var controllers = new List<ControllerInfo>();

            // Keep WMI, WinMM, and XInput isolated so a WMI failure (e.g. under Wine - #59, #61)
            // never prevents WinMM and XInput detection from running.
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT DeviceID, Name, PNPDeviceID, Status FROM Win32_PnPEntity WHERE (PNPClass = 'GameController' OR PNPClass = 'HIDClass') AND Status = 'OK'");

                foreach (var obj in searcher.Get())
                {
                    var controller = CreateControllerInfo(obj);
                    if (controller != null)
                        controllers.Add(controller);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"WMI controller detection error: {ex.Message}");
            }

            try
            {
                DetectWinMmControllers(controllers);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"WinMM controller detection error: {ex.Message}");
            }

            try
            {
                DetectXInputControllers(controllers);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"XInput controller detection error: {ex.Message}");
            }

            // If no controllers found, add a generic entry
            if (controllers.Count == 0)
            {
                controllers.Add(new ControllerInfo
                {
                    Name = "No controller detected",
                    Type = "None",
                    InstanceId = "",
                    IsConnected = false
                });
            }

            return controllers;
        }

        private ControllerInfo? CreateControllerInfo(ManagementBaseObject obj)
        {
            var name = obj["Name"]?.ToString() ?? "";
            var deviceId = obj["DeviceID"]?.ToString() ?? "";
            var pnpId = obj["PNPDeviceID"]?.ToString() ?? "";

            if (string.IsNullOrEmpty(name))
                return null;

            return ClassifyController(name.Trim(), deviceId, pnpId, 0, 0);
        }

        private static ControllerInfo ClassifyController(string name, string instanceId, string pnpId, ushort vid, ushort pid)
        {
            var controller = new ControllerInfo
            {
                Name = name,
                InstanceId = instanceId,
                IsConnected = true
            };

            var nameLower = name.ToLowerInvariant();
            var pnpLower = pnpId.ToLowerInvariant();

            if (vid == 0x054C && (pid == 0x0CE6 || pid == 0x0DF2) ||
                nameLower.Contains("dualsense") || nameLower.Contains("dual sense") || nameLower.Contains("wireless controller") ||
                pnpLower.Contains("sony") && (pnpLower.Contains("dualsense") || pnpLower.Contains("ps5")) ||
                pnpLower.Contains("vid_054c") && (pnpLower.Contains("pid_0ce6") || pnpLower.Contains("pid_0df2")))
            {
                controller.Type = "DualSense";
                controller.HasHaptics = true;
                controller.HasTriggerEffects = true;
                controller.HasMotionSensors = true;
            }
            else if (vid == 0x054C ||
                     nameLower.Contains("dualshock 4") || nameLower.Contains("dualshock4") ||
                     pnpLower.Contains("sony") && pnpLower.Contains("dualshock") ||
                     pnpLower.Contains("vid_054c"))
            {
                controller.Type = "DualShock 4";
                controller.HasHaptics = true;
                controller.HasMotionSensors = true;
            }
            else if (vid == 0x057E ||
                     nameLower.Contains("switch") || nameLower.Contains("pro controller") ||
                     pnpLower.Contains("nintendo") || pnpLower.Contains("vid_057e"))
            {
                controller.Type = "Switch Pro";
                controller.HasHaptics = true;
                controller.HasMotionSensors = true;
            }
            else if (vid == 0x045E ||
                     nameLower.Contains("xbox") || nameLower.Contains("xinput") ||
                     pnpLower.Contains("microsoft") && (pnpLower.Contains("xbox") || pnpLower.Contains("xinput")) ||
                     pnpLower.Contains("vid_045e"))
            {
                controller.Type = "Xbox";
                controller.HasHaptics = true;
                controller.HasMotionSensors = false;
            }
            else if (vid != 0 || nameLower.Contains("gamepad") || nameLower.Contains("joystick") ||
                     nameLower.Contains("controller") || pnpLower.Contains("hid"))
            {
                controller.Type = "Generic";
            }

            return controller;
        }

        private static void DetectWinMmControllers(List<ControllerInfo> controllers)
        {
            uint count = joyGetNumDevs();
            uint capsSize = (uint)Marshal.SizeOf<JOYCAPSW>();
            for (uint id = 0; id < count && id < 16; id++)
            {
                if (joyGetDevCapsW(id, out var caps, capsSize) != 0)
                    continue;

                var info = new JOYINFOEX { dwSize = (uint)Marshal.SizeOf<JOYINFOEX>(), dwFlags = 0xFF };
                if (joyGetPosEx(id, ref info) != 0)
                    continue;

                string rawName = string.IsNullOrWhiteSpace(caps.szPname)
                    ? $"Joystick {id + 1}"
                    : caps.szPname.Trim();
                string name = caps.wMid == 0x054C && (caps.wPid == 0x0CE6 || caps.wPid == 0x0DF2) && !rawName.ToLowerInvariant().Contains("dualsense")
                    ? $"DualSense ({rawName})"
                    : rawName;
                string instanceId = $"WINMM\\VID_{caps.wMid:X4}&PID_{caps.wPid:X4}\\{id}";
                var classified = ClassifyController(name, instanceId, instanceId, caps.wMid, caps.wPid);

                bool alreadyListed = controllers.Any(c =>
                    !string.IsNullOrEmpty(c.Type) && c.Type == classified.Type ||
                    string.Equals(c.Name, classified.Name, StringComparison.OrdinalIgnoreCase));
                if (!alreadyListed)
                    controllers.Add(classified);
            }
        }

        private void DetectXInputControllers(List<ControllerInfo> controllers)
        {
            int xinputFound = 0;
            for (uint i = 0; i < 4; i++)
            {
                if (!TryQueryXInputState(i))
                    continue;

                xinputFound++;
                bool alreadyListed = controllers.Any(c =>
                    c.Type == "Xbox" || c.InstanceId.StartsWith("XINPUT\\", StringComparison.OrdinalIgnoreCase));
                if (!alreadyListed || xinputFound > controllers.Count(c => c.Type == "Xbox"))
                {
                    controllers.Add(new ControllerInfo
                    {
                        Name = $"XInput Controller {i + 1}",
                        Type = "Xbox",
                        InstanceId = $"XINPUT\\{i}",
                        IsConnected = true,
                        HasHaptics = true,
                        HasMotionSensors = false
                    });
                }
            }
        }

        private static bool TryQueryXInputState(uint userIndex)
        {
            try
            {
                return XInputGetState14(userIndex, out _) == 0;
            }
            catch (DllNotFoundException)
            {
                try
                {
                    return XInputGetState910(userIndex, out _) == 0;
                }
                catch (DllNotFoundException)
                {
                    return false;
                }
            }
            catch (EntryPointNotFoundException)
            {
                return false;
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct JOYCAPSW
        {
            public ushort wMid;
            public ushort wPid;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szPname;
            public uint wXmin, wXmax, wYmin, wYmax, wZmin, wZmax;
            public uint wNumButtons, wPeriodMin, wPeriodMax, wRmin, wRmax, wUmin, wUmax, wVmin, wVmax;
            public uint wCaps, wMaxAxes, wNumAxes, wMaxButtons;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szRegKey;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szOEMVxD;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JOYINFOEX
        {
            public uint dwSize, dwFlags, dwXpos, dwYpos, dwZpos, dwRpos, dwUpos, dwVpos;
            public uint dwButtons, dwButtonNumber, dwPOV, dwReserved1, dwReserved2;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct XInputGamepad
        {
            public ushort wButtons;
            public byte bLeftTrigger;
            public byte bRightTrigger;
            public short sThumbLX, sThumbLY, sThumbRX, sThumbRY;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct XInputState
        {
            public uint dwPacketNumber;
            public XInputGamepad Gamepad;
        }

        [DllImport("winmm.dll")]
        private static extern uint joyGetNumDevs();

        [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
        private static extern uint joyGetDevCapsW(uint uJoyID, out JOYCAPSW pjc, uint cbjc);

        [DllImport("winmm.dll")]
        private static extern uint joyGetPosEx(uint uJoyID, ref JOYINFOEX pji);

        [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
        private static extern uint XInputGetState14(uint dwUserIndex, out XInputState pState);

        [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState")]
        private static extern uint XInputGetState910(uint dwUserIndex, out XInputState pState);

        public string GetRecommendedControllerType(List<ControllerInfo> controllers)
        {
            // Priority: DualSense > Switch Pro > Xbox > Generic
            var priority = new Dictionary<string, int>
            {
                { "DualSense", 4 },
                { "DualShock 4", 3 },
                { "Switch Pro", 2 },
                { "Xbox", 1 },
                { "Generic", 0 }
            };

            ControllerInfo? best = null;
            int bestPriority = -1;

            foreach (var c in controllers)
            {
                if (c.IsConnected && priority.TryGetValue(c.Type, out int p) && p > bestPriority)
                {
                    best = c;
                    bestPriority = p;
                }
            }

            return best?.Type ?? "Auto";
        }
    }
}