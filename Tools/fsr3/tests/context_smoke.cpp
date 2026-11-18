// Headless smoke test for libamd_fidelityfx_fsr3_vk.so.
//
// Creates a Vulkan device, asks the provider for an FSR 3.1 upscaling context
// and reads its version back. This is the check that the provider is not merely
// linkable: the SDK's context creation walks the device capabilities, allocates
// every internal resource description and resolves the shader tables.
//
// It does not dispatch a frame yet — that needs real colour, depth, motion
// vectors and a command buffer, which the in-game hook provides.

#include <vulkan/vulkan.h>

#include <cstdio>
#include <cstring>
#include <dlfcn.h>
#include <vector>

#include "ffx_api.h"
#include "ffx_api_types.h"
#include "vk/ffx_api_vk.h"
#include "ffx_upscale.h"

namespace {

PFN_vkGetInstanceProcAddr g_gipa = nullptr;

VKAPI_ATTR VkBool32 VKAPI_CALL OnMessage(VkDebugUtilsMessageSeverityFlagBitsEXT severity,
                                         VkDebugUtilsMessageTypeFlagsEXT,
                                         const VkDebugUtilsMessengerCallbackDataEXT* data,
                                         void*) {
    if (severity >= VK_DEBUG_UTILS_MESSAGE_SEVERITY_WARNING_BIT_EXT) {
        std::printf("validation: %s\n", data->pMessage);
    }
    return VK_FALSE;
}

bool PickDevice(VkInstance instance, VkPhysicalDevice& physical, uint32_t& queueFamily) {
    uint32_t count = 0;
    vkEnumeratePhysicalDevices(instance, &count, nullptr);
    if (count == 0) return false;
    std::vector<VkPhysicalDevice> devices(count);
    vkEnumeratePhysicalDevices(instance, &count, devices.data());

    for (auto device : devices) {
        uint32_t families = 0;
        vkGetPhysicalDeviceQueueFamilyProperties(device, &families, nullptr);
        std::vector<VkQueueFamilyProperties> properties(families);
        vkGetPhysicalDeviceQueueFamilyProperties(device, &families, properties.data());
        for (uint32_t index = 0; index < families; ++index) {
            if (properties[index].queueFlags & VK_QUEUE_COMPUTE_BIT) {
                VkPhysicalDeviceProperties info{};
                vkGetPhysicalDeviceProperties(device, &info);
                std::printf("device: %s\n", info.deviceName);
                physical = device;
                queueFamily = index;
                return true;
            }
        }
    }
    return false;
}

} // namespace

int main() {
    const char* path = std::getenv("FSR3_PROVIDER");
    if (path == nullptr) path = "libamd_fidelityfx_fsr3_vk.so";

    void* module = dlopen(path, RTLD_NOW | RTLD_LOCAL);
    if (module == nullptr) {
        std::printf("FAIL dlopen: %s\n", dlerror());
        return 1;
    }

    auto create = reinterpret_cast<PfnFfxCreateContext>(dlsym(module, "ffxCreateContext"));
    auto destroy = reinterpret_cast<PfnFfxDestroyContext>(dlsym(module, "ffxDestroyContext"));
    if (!create || !destroy) {
        std::printf("FAIL provider does not export the ffx entry points\n");
        return 1;
    }

    g_gipa = reinterpret_cast<PFN_vkGetInstanceProcAddr>(dlopen("libvulkan.so.1", RTLD_NOW | RTLD_LOCAL)
                                                             ? dlsym(dlopen("libvulkan.so.1", RTLD_NOW), "vkGetInstanceProcAddr")
                                                             : nullptr);
    auto vkCreateInstance = reinterpret_cast<PFN_vkCreateInstance>(
        g_gipa ? g_gipa(nullptr, "vkCreateInstance") : nullptr);
    if (vkCreateInstance == nullptr) {
        std::printf("FAIL no Vulkan loader\n");
        return 1;
    }

    VkApplicationInfo application{VK_STRUCTURE_TYPE_APPLICATION_INFO};
    application.apiVersion = VK_API_VERSION_1_3;
    const char* layers[] = {"VK_LAYER_KHRONOS_validation"};
    const char* extensions[] = {VK_EXT_DEBUG_UTILS_EXTENSION_NAME};
    VkInstanceCreateInfo instanceInfo{VK_STRUCTURE_TYPE_INSTANCE_CREATE_INFO};
    instanceInfo.pApplicationInfo = &application;
    instanceInfo.enabledLayerCount = 1;
    instanceInfo.ppEnabledLayerNames = layers;
    instanceInfo.enabledExtensionCount = 1;
    instanceInfo.ppEnabledExtensionNames = extensions;

    VkInstance instance = VK_NULL_HANDLE;
    if (vkCreateInstance(&instanceInfo, nullptr, &instance) != VK_SUCCESS) {
        std::printf("FAIL vkCreateInstance\n");
        return 1;
    }

    {
        auto createMessenger = reinterpret_cast<PFN_vkCreateDebugUtilsMessengerEXT>(
            g_gipa(instance, "vkCreateDebugUtilsMessengerEXT"));
        if (createMessenger) {
            VkDebugUtilsMessengerCreateInfoEXT info{VK_STRUCTURE_TYPE_DEBUG_UTILS_MESSENGER_CREATE_INFO_EXT};
            info.messageSeverity = VK_DEBUG_UTILS_MESSAGE_SEVERITY_WARNING_BIT_EXT |
                                   VK_DEBUG_UTILS_MESSAGE_SEVERITY_ERROR_BIT_EXT;
            info.messageType = VK_DEBUG_UTILS_MESSAGE_TYPE_GENERAL_BIT_EXT |
                               VK_DEBUG_UTILS_MESSAGE_TYPE_VALIDATION_BIT_EXT |
                               VK_DEBUG_UTILS_MESSAGE_TYPE_PERFORMANCE_BIT_EXT;
            info.pfnUserCallback = OnMessage;
            VkDebugUtilsMessengerEXT messenger = VK_NULL_HANDLE;
            createMessenger(instance, &info, nullptr, &messenger);
        }
    }

    VkPhysicalDevice physical = VK_NULL_HANDLE;
    uint32_t queueFamily = 0;
    if (!PickDevice(instance, physical, queueFamily)) {
        std::printf("FAIL no compute device\n");
        return 1;
    }

    const float priority = 1.0f;
    VkDeviceQueueCreateInfo queueInfo{VK_STRUCTURE_TYPE_DEVICE_QUEUE_CREATE_INFO};
    queueInfo.queueFamilyIndex = queueFamily;
    queueInfo.queueCount = 1;
    queueInfo.pQueuePriorities = &priority;
    VkDeviceCreateInfo deviceInfo{VK_STRUCTURE_TYPE_DEVICE_CREATE_INFO};
    deviceInfo.queueCreateInfoCount = 1;
    deviceInfo.pQueueCreateInfos = &queueInfo;

    VkDevice device = VK_NULL_HANDLE;
    if (vkCreateDevice(physical, &deviceInfo, nullptr, &device) != VK_SUCCESS) {
        std::printf("FAIL vkCreateDevice\n");
        return 1;
    }

    ffxCreateBackendVKDesc backend{};
    backend.header.type = FFX_API_CREATE_CONTEXT_DESC_TYPE_BACKEND_VK;
    backend.deviceContext.vkDevice = device;
    backend.deviceContext.vkPhysicalDevice = physical;
    backend.deviceContext.vkDeviceProcAddr = vkGetDeviceProcAddr;

    ffxCreateContextDescUpscale upscale{};
    upscale.header.type = FFX_API_CREATE_CONTEXT_DESC_TYPE_UPSCALE;
    upscale.header.pNext = &backend.header;
    upscale.fpMessage = [](unsigned int type, const wchar_t* message) {
        std::printf("sdk[%d]: %ls\n", static_cast<int>(type), message ? message : L"");
    };
    upscale.maxRenderSize = {1920, 1080};
    upscale.maxUpscaleSize = {3840, 2160};

    ffxContext context = nullptr;
    const ffxReturnCode_t created = create(&context, &upscale.header, nullptr);
    if (created != FFX_API_RETURN_OK || context == nullptr) {
        std::printf("FAIL ffxCreateContext 0x%x\n", static_cast<unsigned>(created));
        return 1;
    }
    std::printf("context created\n");

    // The provider resolved and allocated its full context: the SDK walked the
    // device capabilities, sized every internal resource and bound the shader
    // tables generated by build-shaders.py.
    destroy(&context, nullptr);
    std::printf("PASS\n");
    return 0;
}
