// The FidelityFX SDK declares a non-inline GetProvider() that each application
// supplies, so a provider .so decides which effects it exposes. This one exposes
// FSR 3.1 upscaling only.
//
// The driver-side provider slot (ffxProviderExternal) is still offered first:
// with FFX_BACKEND_VK defined, GetProvider's inline overload probes the driver
// for its own FSR implementation, and AMD's driver provider should win when it
// supports the requested effect.

#include "ffx_provider.h"

#include "ffx_provider_fsr3upscale.h"

#include <array>

namespace {

std::array<ffxProvider*, 1> Providers() {
    return { &ffxProvider_FSR3Upscale::GetInstance() };
}

} // namespace

ffxProvider* GetProvider(ffxStructType_t descType, uint64_t overrideId, void* device,
                         std::optional<ffxProviderExternal>& extProviderSlot) {
    const auto providers = Providers();
    return GetProvider(descType, overrideId, device, extProviderSlot,
                       std::span<ffxProvider* const>(providers));
}

// Same contract as GetProvider: the SDK declares these overrides without
// defining them, so every application that links ffx_api.cpp supplies both.
uint64_t GetProviderVersions(ffxStructType_t descType, void* device, uint64_t capacity,
                             uint64_t* versionIds, const char** versionNames,
                             std::optional<ffxProviderExternal>& extProviderSlot) {
    const auto providers = Providers();
    return GetProviderVersions(descType, device, capacity, versionIds, versionNames,
                               extProviderSlot,
                               std::span<ffxProvider* const>(providers));
}
