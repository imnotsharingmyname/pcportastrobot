// The intermediate type AMD's shader-blob generator emits.
//
// ffx_internal_types.h converts this into FfxShaderBlob with
// POPULATE_SHADER_BLOB_FFX, which reads these exact field names. FfxShaderBlob
// itself uses different names (data, size, cbvCount, boundConstantBuffers...),
// so the generated tables cannot be FfxShaderBlob literals.
#pragma once

#include <stdint.h>

typedef struct FfxShaderBlobHeader {
    const uint8_t* blobData;
    uint32_t       blobSize;
    const char*    entryName;

    uint32_t numConstantBuffers;
    uint32_t numSRVTextures;
    uint32_t numUAVTextures;
    uint32_t numSRVBuffers;
    uint32_t numUAVBuffers;
    uint32_t numSamplers;
    uint32_t numRTAccelerationStructures;

    const char**    constantBufferNames;
    const uint32_t* constantBufferBindings;
    const uint32_t* constantBufferCounts;
    const uint32_t* constantBufferSpaces;

    const char**    srvTextureNames;
    const uint32_t* srvTextureBindings;
    const uint32_t* srvTextureCounts;
    const uint32_t* srvTextureSpaces;

    const char**    uavTextureNames;
    const uint32_t* uavTextureBindings;
    const uint32_t* uavTextureCounts;
    const uint32_t* uavTextureSpaces;

    const char**    srvBufferNames;
    const uint32_t* srvBufferBindings;
    const uint32_t* srvBufferCounts;
    const uint32_t* srvBufferSpaces;

    const char**    uavBufferNames;
    const uint32_t* uavBufferBindings;
    const uint32_t* uavBufferCounts;
    const uint32_t* uavBufferSpaces;

    const char**    samplerNames;
    const uint32_t* samplerBindings;
    const uint32_t* samplerCounts;
    const uint32_t* samplerSpaces;

    const char**    rtAccelerationStructureNames;
    const uint32_t* rtAccelerationStructureBindings;
    const uint32_t* rtAccelerationStructureCounts;
    const uint32_t* rtAccelerationStructureSpaces;
} FfxShaderBlobHeader;
