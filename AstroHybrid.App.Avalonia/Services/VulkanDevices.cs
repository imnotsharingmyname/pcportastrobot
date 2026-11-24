using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace AstroHybrid.App.Avalonia.Services;

/// <summary>
/// Enumerates the Vulkan physical devices so the adapter picker can offer the
/// same indices Kyty's renderer enumerates. Uses the loader directly, so it does
/// not depend on vulkaninfo being installed.
/// </summary>
public static class VulkanDevices
{
    private const uint VulkanStructureTypeApplicationInfo = 0;
    private const uint VulkanStructureTypeInstanceCreateInfo = 1;
    private const uint VulkanApiVersion10 = 1u << 22;

    [StructLayout(LayoutKind.Explicit)]
    private struct VkApplicationInfo
    {
        [FieldOffset(0)] public uint SType;
        [FieldOffset(8)] public IntPtr PNext;
        [FieldOffset(16)] public IntPtr PApplicationName;
        [FieldOffset(24)] public uint ApplicationVersion;
        [FieldOffset(32)] public IntPtr PEngineName;
        [FieldOffset(40)] public uint EngineVersion;
        [FieldOffset(44)] public uint ApiVersion;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct VkInstanceCreateInfo
    {
        [FieldOffset(0)] public uint SType;
        [FieldOffset(8)] public IntPtr PNext;
        [FieldOffset(16)] public uint Flags;
        [FieldOffset(24)] public IntPtr PApplicationInfo;
        [FieldOffset(32)] public uint EnabledLayerCount;
        [FieldOffset(40)] public IntPtr PpEnabledLayerNames;
        [FieldOffset(48)] public uint EnabledExtensionCount;
        [FieldOffset(56)] public IntPtr PpEnabledExtensionNames;
    }

    [DllImport("libvulkan.so.1", CallingConvention = CallingConvention.Cdecl)]
    private static extern int vkCreateInstance(ref VkInstanceCreateInfo createInfo, IntPtr allocator, out IntPtr instance);

    [DllImport("libvulkan.so.1", CallingConvention = CallingConvention.Cdecl)]
    private static extern void vkDestroyInstance(IntPtr instance, IntPtr allocator);

    [DllImport("libvulkan.so.1", CallingConvention = CallingConvention.Cdecl)]
    private static extern int vkEnumeratePhysicalDevices(IntPtr instance, ref uint count, IntPtr[]? devices);

    [DllImport("libvulkan.so.1", CallingConvention = CallingConvention.Cdecl)]
    private static extern void vkGetPhysicalDeviceProperties(IntPtr device, IntPtr properties);

    public sealed record Device(int Index, string Name, bool IsDiscrete);

    /// <summary>
    /// Devices in the loader's enumeration order, which is the order Kyty uses
    /// for KYTY_GPU_INDEX. Returns an empty list when Vulkan cannot be reached.
    /// </summary>
    public static IReadOnlyList<Device> Enumerate()
    {
        var result = new List<Device>();
        IntPtr instance = IntPtr.Zero;
        IntPtr namePtr = IntPtr.Zero;
        IntPtr enginePtr = IntPtr.Zero;

        try
        {
            namePtr = Marshal.StringToHGlobalAnsi("AstroHybrid");
            enginePtr = Marshal.StringToHGlobalAnsi("AstroHybrid");

            var application = new VkApplicationInfo
            {
                SType = VulkanStructureTypeApplicationInfo,
                PApplicationName = namePtr,
                ApplicationVersion = 1,
                PEngineName = enginePtr,
                EngineVersion = 1,
                ApiVersion = VulkanApiVersion10,
            };

            var createInfo = new VkInstanceCreateInfo
            {
                SType = VulkanStructureTypeInstanceCreateInfo,
            };

            var appHandle = Marshal.AllocHGlobal(Marshal.SizeOf<VkApplicationInfo>());
            try
            {
                Marshal.StructureToPtr(application, appHandle, false);
                createInfo.PApplicationInfo = appHandle;
                if (vkCreateInstance(ref createInfo, IntPtr.Zero, out instance) != 0 || instance == IntPtr.Zero)
                {
                    return result;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(appHandle);
            }

            uint count = 0;
            if (vkEnumeratePhysicalDevices(instance, ref count, null) != 0 || count == 0)
            {
                return result;
            }

            var handles = new IntPtr[count];
            if (vkEnumeratePhysicalDevices(instance, ref count, handles) != 0)
            {
                return result;
            }

            var buffer = Marshal.AllocHGlobal(1024);
            try
            {
                for (var index = 0; index < handles.Length; index++)
                {
                    Marshal.Copy(new byte[1024], 0, buffer, 1024);
                    vkGetPhysicalDeviceProperties(handles[index], buffer);

                    // VkPhysicalDeviceProperties: deviceType at 16, deviceName at 20 (256 bytes).
                    var type = Marshal.ReadInt32(buffer, 16);
                    var name = Marshal.PtrToStringAnsi(IntPtr.Add(buffer, 20)) ?? $"Device {index}";
                    result.Add(new Device(index, name.Trim(), type == 2));
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch
        {
            // No loader, no devices; the picker stays disabled.
        }
        finally
        {
            if (instance != IntPtr.Zero)
            {
                vkDestroyInstance(instance, IntPtr.Zero);
            }

            if (namePtr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(namePtr);
            }

            if (enginePtr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(enginePtr);
            }
        }

        return result;
    }
}
