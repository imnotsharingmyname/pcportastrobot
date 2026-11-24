// Application-supplied provider list for the frame-generation library.
//
// The SDK declares GetProvider and GetProviderVersions without defining them, so
// each .so supplies its own. Same contract as provider_list.cpp.

#include "ffx_provider.h"

#include "ffx_provider_fsr3framegeneration.h"
#include "ffx_provider_fsr3framegenerationswapchain.h"

#include <array>

namespace {

std::array<ffxProvider*, 2> Providers() {
    return { &ffxProvider_Fsr3FrameGeneration::GetInstance(),
             &ffxProvider_Fsr3FrameGenerationSwapChain::GetInstance() };
}

} // namespace

ffxProvider* GetProvider(ffxStructType_t descType, uint64_t overrideId, void* device,
                         std::optional<ffxProviderExternal>& extProviderSlot) {
    const auto providers = Providers();
    return GetProvider(descType, overrideId, device, extProviderSlot,
                       std::span<ffxProvider* const>(providers));
}

uint64_t GetProviderVersions(ffxStructType_t descType, void* device, uint64_t capacity,
                             uint64_t* versionIds, const char** versionNames,
                             std::optional<ffxProviderExternal>& extProviderSlot) {
    const auto providers = Providers();
    return GetProviderVersions(descType, device, capacity, versionIds, versionNames,
                               extProviderSlot,
                               std::span<ffxProvider* const>(providers));
}
