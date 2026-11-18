// ffx_provider.h and ffx_api.cpp both use ffxProviderExternal, but the 2.4.0
// source drop never defines it: it is meant to wrap a driver-side FSR
// implementation discovered through a backend extension.
//
// No Linux Vulkan driver exposes that interface, so this reports invalid. The
// SDK's provider selection then falls back to the list we register ourselves,
// which is the behaviour we want.
//
// This header is included from a generated shadow copy of ffx_provider.h, right
// after class ffxProvider is complete, because the inline lookup functions in
// that file construct the type.
#pragma once

class ffxProviderExternal : public ffxProvider {
public:
    ffxProviderExternal(void* device, ffxStructType_t descType)
        : ffxProvider(0, descType & FFX_API_EFFECT_MASK, "external") {
        (void)device;
    }

    bool Valid() const { return false; }
    bool CanProvide(uint64_t descType) const override { (void)descType; return false; }
    bool IsSupported(void* device) const override { (void)device; return false; }

    ffxReturnCode_t CreateContext(ffxContext*, ffxCreateContextDescHeader*, Allocator&) override {
        return FFX_API_RETURN_NO_PROVIDER;
    }
    ffxReturnCode_t DestroyContext(ffxContext*, Allocator&) override {
        return FFX_API_RETURN_NO_PROVIDER;
    }
    ffxReturnCode_t Configure(ffxContext*, const ffxConfigureDescHeader*) const override {
        return FFX_API_RETURN_NO_PROVIDER;
    }
    ffxReturnCode_t Query(ffxContext*, ffxQueryDescHeader*) const override {
        return FFX_API_RETURN_NO_PROVIDER;
    }
    ffxReturnCode_t Dispatch(ffxContext*, const ffxDispatchDescHeader*) const override {
        return FFX_API_RETURN_NO_PROVIDER;
    }
};
