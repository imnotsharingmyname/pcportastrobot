using System;
using System.Collections.Generic;
using System.Management;
using AstroHybrid.App.Models;

namespace AstroHybrid.App.Services
{
    public class ControllerDetector
    {
        public List<ControllerInfo> DetectControllers()
        {
            var controllers = new List<ControllerInfo>();

            try
            {
                // Use WMI to get connected game controllers
                using var searcher = new ManagementObjectSearcher(
                    "SELECT DeviceID, Name, PNPDeviceID, Status FROM Win32_PnPEntity WHERE (PNPClass = 'GameController' OR PNPClass = 'HIDClass') AND Status = 'OK'");

                foreach (var obj in searcher.Get())
                {
                    var controller = CreateControllerInfo(obj);
                    if (controller != null)
                        controllers.Add(controller);
                }

                // Also check for XInput devices (Xbox controllers)
                DetectXInputControllers(controllers);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Controller detection error: {ex.Message}");
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

            var controller = new ControllerInfo
            {
                Name = name.Trim(),
                InstanceId = deviceId,
                IsConnected = true
            };

            // Determine controller type from name/PNP ID
            var nameLower = name.ToLowerInvariant();
            var pnpLower = pnpId.ToLowerInvariant();

            if (nameLower.Contains("dualsense") || nameLower.Contains("dual sense") ||
                pnpLower.Contains("sony") && (pnpLower.Contains("dualsense") || pnpLower.Contains("ps5")))
            {
                controller.Type = "DualSense";
                controller.HasHaptics = true;
                controller.HasTriggerEffects = true;
                controller.HasMotionSensors = true;
            }
            else if (nameLower.Contains("dualshock 4") || nameLower.Contains("dualshock4") ||
                     pnpLower.Contains("sony") && pnpLower.Contains("dualshock"))
            {
                controller.Type = "DualShock 4";
                controller.HasHaptics = true;
                controller.HasMotionSensors = true;
            }
            else if (nameLower.Contains("xbox") || nameLower.Contains("xinput") ||
                     pnpLower.Contains("microsoft") && (pnpLower.Contains("xbox") || pnpLower.Contains("xinput")))
            {
                controller.Type = "Xbox";
                controller.HasHaptics = true; // Rumble
                controller.HasMotionSensors = false; // Xbox controllers don't have motion
            }
            else if (nameLower.Contains("switch") || nameLower.Contains("pro controller") ||
                     pnpLower.Contains("nintendo") && pnpLower.Contains("pro"))
            {
                controller.Type = "Switch Pro";
                controller.HasHaptics = true;
                controller.HasMotionSensors = true;
            }
            else if (nameLower.Contains("gamepad") || nameLower.Contains("joystick") ||
                     nameLower.Contains("controller") || pnpLower.Contains("hid"))
            {
                controller.Type = "Generic";
                // Can't determine capabilities for generic
            }

            return controller;
        }

        private void DetectXInputControllers(List<ControllerInfo> controllers)
        {
            // XInput supports up to 4 controllers
            for (int i = 0; i < 4; i++)
            {
                try
                {
                    // This would require XInput DLL - simplified check
                    // For now, we'll rely on WMI detection
                }
                catch { }
            }
        }

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