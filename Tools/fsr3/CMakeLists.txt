# Build AMD's FSR 3.1 upscaler provider for Linux.
#
# This links the FidelityFX SDK's own API core, Vulkan backend and FSR 3.1
# provider into libamd_fidelityfx_fsr3_vk.so. The shader tables that the SDK
# normally generates in its Windows sample build come from Tools/fsr3/build-shaders.py.

cmake_minimum_required(VERSION 3.20)
project(amd_fidelityfx_fsr3_vk C CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)

set(FSR3_SDK "" CACHE PATH "FidelityFX SDK root")
if(NOT FSR3_SDK)
  message(FATAL_ERROR "set FSR3_SDK to the FidelityFX SDK root")
endif()

set(KIT ${FSR3_SDK}/Kits/FidelityFX)
set(SHADERS ${CMAKE_CURRENT_BINARY_DIR}/../../fsr3/shaders
    CACHE PATH "generated shader tables")

if(NOT EXISTS ${SHADERS}/ffx_fsr3upscaler_accumulate_pass_permutations.c)
  message(FATAL_ERROR
    "shader tables missing in ${SHADERS}; run Tools/fsr3/build-shaders.py first")
endif()

file(GLOB UPSCALER_TABLES ${SHADERS}/ffx_fsr3upscaler_*_permutations.c)

# The vendor sources include each other by relative path, so a shadow include
# directory cannot reach them. Instead this builds from a copy of the SDK
# subtrees it needs and patches the two defects found in the 2.4.0 drop there.
# The user's SDK tree is never modified.
set(SDK_COPY ${CMAKE_BINARY_DIR}/sdk/FidelityFX)
if(NOT EXISTS ${SDK_COPY}/api/internal/ffx_provider.h)
  foreach(sub api backend upscalers framegeneration)
    file(COPY ${KIT}/${sub} DESTINATION ${SDK_COPY})
  endforeach()
endif()

set(PROVIDER_HEADER ${SDK_COPY}/api/internal/ffx_provider.h)
file(READ ${PROVIDER_HEADER} FFX_PROVIDER_HEADER)
set(SHADOW_ANCHOR "inline ffxProvider* GetAssociatedProvider")
string(FIND "${FFX_PROVIDER_HEADER}" "${SHADOW_ANCHOR}" SHADOW_POS)
if(SHADOW_POS EQUAL -1)
  message(FATAL_ERROR "ffx_provider.h no longer contains the insertion point")
endif()
string(REPLACE "${SHADOW_ANCHOR}"
  "#include \"ffx_provider_external.h\"\n\n${SHADOW_ANCHOR}"
  FFX_PROVIDER_HEADER "${FFX_PROVIDER_HEADER}")
file(WRITE ${PROVIDER_HEADER} "${FFX_PROVIDER_HEADER}")

# MSVC extension: 0xF5A5'CA1Eui64 (digit separator plus the ui64 suffix).
file(READ ${SDK_COPY}/upscalers/fsr3/internal/ffx_provider_fsr3upscale.cpp PROVIDER_CPP)
string(REGEX REPLACE "0x[0-9A-Fa-f]+'[0-9A-Fa-f]+ui64" "0xF5A5CA1EULL" PROVIDER_CPP "${PROVIDER_CPP}")
file(WRITE ${SDK_COPY}/upscalers/fsr3/internal/ffx_provider_fsr3upscale.cpp "${PROVIDER_CPP}")

# FFX_SDK_DEFAULT_CONTEXT_SIZE is 128K uint32 (512 KB), but the FSR 3.1 private
# context is 837 KB here: twelve FfxPipelineState at 68 KB each, because
# FfxResourceBinding tables for 64 SRVs and 64 UAVs dominate. The public opaque
# context must be at least as large, so the constant is raised in the copy.
file(READ ${SDK_COPY}/api/internal/ffx_internal_types.h INTERNAL_TYPES)
string(REPLACE "#define FFX_SDK_DEFAULT_CONTEXT_SIZE (1024 * 128)"
               "#define FFX_SDK_DEFAULT_CONTEXT_SIZE (1024 * 512)"
               INTERNAL_TYPES "${INTERNAL_TYPES}")
file(WRITE ${SDK_COPY}/api/internal/ffx_internal_types.h "${INTERNAL_TYPES}")

# ConvertUTF8ToUTF16/ConvertUTF16ToUTF8 unconditionally write to the name buffer,
# which CreatePipelineVK passes as null when the pipeline has no debug name. The
# Windows path has the same shape but its callers always supply one; on Linux the
# unrecognised-name path reaches it and faults. Guard both.
file(READ ${SDK_COPY}/backend/vk/ffx_vk.cpp VK_BACKEND)
string(REPLACE "void ConvertUTF8ToUTF16(const char* inputName, wchar_t* outputBuffer, size_t outputLen)\n{\n    memset(outputBuffer, 0, outputLen * sizeof(wchar_t));"
               "void ConvertUTF8ToUTF16(const char* inputName, wchar_t* outputBuffer, size_t outputLen)\n{\n    if (outputBuffer == nullptr || outputLen == 0 || inputName == nullptr) return;\n    memset(outputBuffer, 0, outputLen * sizeof(wchar_t));"
               VK_BACKEND "${VK_BACKEND}")
string(REPLACE "void ConvertUTF16ToUTF8(const wchar_t* inputName, char* outputBuffer, size_t outputLen)\n{\n    memset(outputBuffer, 0, outputLen * sizeof(char));"
               "void ConvertUTF16ToUTF8(const wchar_t* inputName, char* outputBuffer, size_t outputLen)\n{\n    if (outputBuffer == nullptr || outputLen == 0 || inputName == nullptr) return;\n    memset(outputBuffer, 0, outputLen * sizeof(char));"
               VK_BACKEND "${VK_BACKEND}")
file(WRITE ${SDK_COPY}/backend/vk/ffx_vk.cpp "${VK_BACKEND}")

# FG swapchain provider: same MSVC literal problem.
file(READ ${SDK_COPY}/framegeneration/fsr3/vk/ffx_provider_framegenerationswapchain_vk.cpp FG_SWAPCHAIN)
string(REGEX REPLACE "0x[0-9A-Fa-f]+'[0-9A-Fa-f]+ui64" "0xF3A3F1A9ULL" FG_SWAPCHAIN "${FG_SWAPCHAIN}")
file(WRITE ${SDK_COPY}/framegeneration/fsr3/vk/ffx_provider_framegenerationswapchain_vk.cpp "${FG_SWAPCHAIN}")

# The swapchain declares and handles Win32-only full-screen-exclusive structures
# unconditionally; Vulkan only defines them with VK_USE_PLATFORM_WIN32_KHR.
file(READ ${SDK_COPY}/framegeneration/fsr3/vk/FrameInterpolationSwapchainVK.cpp FI_SWAPCHAIN)
string(REPLACE
  "    VkSurfaceFullScreenExclusiveInfoEXT      surfaceFullScreenExclusive;\n    VkSurfaceFullScreenExclusiveWin32InfoEXT surfaceFullScreenExclusiveWin32;"
  "#if defined(VK_USE_PLATFORM_WIN32_KHR)\n    VkSurfaceFullScreenExclusiveInfoEXT      surfaceFullScreenExclusive;\n    VkSurfaceFullScreenExclusiveWin32InfoEXT surfaceFullScreenExclusiveWin32;\n#endif"
  FI_SWAPCHAIN "${FI_SWAPCHAIN}")
string(REPLACE
  "        case VK_STRUCTURE_TYPE_SURFACE_FULL_SCREEN_EXCLUSIVE_WIN32_INFO_EXT:"
  "#if defined(VK_USE_PLATFORM_WIN32_KHR)\n        case VK_STRUCTURE_TYPE_SURFACE_FULL_SCREEN_EXCLUSIVE_WIN32_INFO_EXT:"
  FI_SWAPCHAIN "${FI_SWAPCHAIN}")
string(REPLACE
  "        case VK_STRUCTURE_TYPE_SWAPCHAIN_COUNTER_CREATE_INFO_EXT:"
  "#endif\n        case VK_STRUCTURE_TYPE_SWAPCHAIN_COUNTER_CREATE_INFO_EXT:"
  FI_SWAPCHAIN "${FI_SWAPCHAIN}")
string(REPLACE
  "        case VK_STRUCTURE_TYPE_SURFACE_FULL_SCREEN_EXCLUSIVE_INFO_EXT:"
  "#if defined(VK_USE_PLATFORM_WIN32_KHR)\n        case VK_STRUCTURE_TYPE_SURFACE_FULL_SCREEN_EXCLUSIVE_INFO_EXT:"
  FI_SWAPCHAIN "${FI_SWAPCHAIN}")
string(REPLACE
  "        case VK_STRUCTURE_TYPE_SWAPCHAIN_DISPLAY_NATIVE_HDR_CREATE_INFO_AMD:"
  "#endif\n        case VK_STRUCTURE_TYPE_SWAPCHAIN_DISPLAY_NATIVE_HDR_CREATE_INFO_AMD:"
  FI_SWAPCHAIN "${FI_SWAPCHAIN}")
# void* to a function pointer needs reinterpret_cast; MSVC accepts static_cast.
string(REPLACE "static_cast<FfxWaitCallbackFunc>" "reinterpret_cast<FfxWaitCallbackFunc>"
  FI_SWAPCHAIN "${FI_SWAPCHAIN}")
file(WRITE ${SDK_COPY}/framegeneration/fsr3/vk/FrameInterpolationSwapchainVK.cpp "${FI_SWAPCHAIN}")

# The debug-pacing entry point is declared without C linkage in the swapchain
# header but defined with FFX_API (= extern "C"), so the call site looks for a
# mangled symbol that does not exist. Drop the linkage specifier on the
# definition so both sides agree on the C++ name.
file(READ ${SDK_COPY}/framegeneration/fsr3/vk/FrameInterpolationSwapchainVK_DebugPacing.cpp DEBUG_PACING)
string(REPLACE "FFX_API FfxErrorCode ffxFrameInterpolationDebugPacing("
               "FfxErrorCode ffxFrameInterpolationDebugPacing("
               DEBUG_PACING "${DEBUG_PACING}")
file(WRITE ${SDK_COPY}/framegeneration/fsr3/vk/FrameInterpolationSwapchainVK_DebugPacing.cpp "${DEBUG_PACING}")

# FG provider: same MSVC literal problem as the upscaler provider.
file(READ ${SDK_COPY}/framegeneration/fsr3/internal/ffx_provider_fsr3framegeneration.cpp FG_PROVIDER)
string(REGEX REPLACE "0x[0-9A-Fa-f]+'[0-9A-Fa-f]+ui64" "0xF3A3F1A9ULL" FG_PROVIDER "${FG_PROVIDER}")
file(WRITE ${SDK_COPY}/framegeneration/fsr3/internal/ffx_provider_fsr3framegeneration.cpp "${FG_PROVIDER}")

# MSVC backslash includes in the swapchain sources: rewrite to forward slashes.
file(GLOB FG_VK_HEADERS ${SDK_COPY}/framegeneration/fsr3/vk/*.h)
foreach(header ${FG_VK_HEADERS})
  file(READ ${header} HEADER_TEXT)
  string(REPLACE "\\" "/" HEADER_TEXT "${HEADER_TEXT}")
  file(WRITE ${header} "${HEADER_TEXT}")
endforeach()

set(KIT ${SDK_COPY})

add_library(amd_fidelityfx_fsr3_vk SHARED
  ${KIT}/api/internal/ffx_api.cpp
  ${KIT}/api/internal/ffx_assert.cpp
  ${KIT}/api/internal/ffx_message.cpp
  ${KIT}/api/internal/ffx_object_management.cpp
  ${KIT}/api/internal/ffx_query_fallback.cpp
  ${KIT}/backend/vk/ffx_backends_vk.cpp
  ${KIT}/backend/vk/ffx_vk.cpp
  ${KIT}/upscalers/fsr3/internal/ffx_fsr3upscaler.cpp
  ${KIT}/upscalers/fsr3/internal/ffx_fsr3upscaler_shaderblobs.cpp
  ${KIT}/upscalers/fsr3/internal/ffx_provider_fsr3upscale.cpp
  ${UPSCALER_TABLES}
  src/provider_list.cpp
)

target_include_directories(amd_fidelityfx_fsr3_vk PRIVATE
  ${CMAKE_CURRENT_SOURCE_DIR}/include
  ${KIT}/api/include
  ${KIT}/api/internal
  ${KIT}/backend/vk
  ${KIT}/upscalers/include
  ${KIT}/upscalers/fsr3/include
  ${KIT}/upscalers/fsr3/internal
  ${SHADERS}
)

target_compile_definitions(amd_fidelityfx_fsr3_vk PRIVATE
  FFX_BACKEND_VK=1
  FFX_USE_VK=1
)

# The SDK targets MSVC in a few places; the compat header supplies the export
# attribute and MSVC-only helpers without editing the vendor sources.
target_compile_options(amd_fidelityfx_fsr3_vk PRIVATE
  $<$<COMPILE_LANGUAGE:CXX>:-include${CMAKE_CURRENT_SOURCE_DIR}/include/ffx_compat.h>
  -Wno-unused-parameter
)

# The provider is loaded with dlopen; nothing else should be exported.
target_link_libraries(amd_fidelityfx_fsr3_vk PRIVATE ${CMAKE_DL_LIBS} vulkan z)
set_target_properties(amd_fidelityfx_fsr3_vk PROPERTIES
  CXX_VISIBILITY_PRESET hidden
  VISIBILITY_INLINES_HIDDEN ON)

# ---------------------------------------------------------------- frame generation
#
# The FG provider wraps the game's swapchain, so it links the same SDK core and
# Vulkan backend plus the optical-flow, frame-interpolation and swapchain
# translation units.

file(GLOB FG_TABLES
  ${SHADERS}/ffx_frameinterpolation_*_permutations.c
  ${SHADERS}/ffx_opticalflow_*_permutations.c)

add_library(amd_fidelityfx_framegeneration_vk SHARED
  ${KIT}/api/internal/ffx_api.cpp
  ${KIT}/api/internal/ffx_assert.cpp
  ${KIT}/api/internal/ffx_message.cpp
  ${KIT}/api/internal/ffx_object_management.cpp
  ${KIT}/api/internal/ffx_query_fallback.cpp
  ${KIT}/backend/vk/ffx_backends_vk.cpp
  ${KIT}/backend/vk/ffx_vk.cpp
  ${KIT}/framegeneration/fsr3/internal/ffx_frameinterpolation.cpp
  ${KIT}/framegeneration/fsr3/internal/ffx_frameinterpolation_shaderblobs.cpp
  ${KIT}/framegeneration/fsr3/internal/ffx_opticalflow.cpp
  ${KIT}/framegeneration/fsr3/internal/ffx_opticalflow_shaderblobs.cpp
  ${KIT}/framegeneration/fsr3/internal/ffx_provider_fsr3framegeneration.cpp
  ${KIT}/framegeneration/fsr3/vk/ffx_provider_framegenerationswapchain_vk.cpp
  ${KIT}/framegeneration/fsr3/vk/FrameInterpolationSwapchainVK.cpp
  ${KIT}/framegeneration/fsr3/vk/FrameInterpolationSwapchainVK_Helpers.cpp
  ${KIT}/framegeneration/fsr3/vk/FrameInterpolationSwapchainVK_UiComposition.cpp
  ${KIT}/framegeneration/fsr3/vk/FrameInterpolationSwapchainVK_DebugPacing.cpp
  ${FG_TABLES}
  src/provider_list_fg.cpp
)

target_include_directories(amd_fidelityfx_framegeneration_vk PRIVATE
  ${CMAKE_CURRENT_SOURCE_DIR}/include
  ${KIT}/api/include
  ${KIT}/api/internal
  ${KIT}/backend/vk
  ${KIT}/framegeneration/include
  ${KIT}/framegeneration/fsr3/include
  ${KIT}/framegeneration/fsr3/internal
  ${SHADERS}
)

target_compile_definitions(amd_fidelityfx_framegeneration_vk PRIVATE
  FFX_BACKEND_VK=1
  FFX_USE_VK=1
  FFX_FRAMEGENERATION=1
)

target_compile_options(amd_fidelityfx_framegeneration_vk PRIVATE
  $<$<COMPILE_LANGUAGE:CXX>:-include${CMAKE_CURRENT_SOURCE_DIR}/include/ffx_compat.h>
  -Wno-unused-parameter
)

target_link_libraries(amd_fidelityfx_framegeneration_vk PRIVATE ${CMAKE_DL_LIBS} vulkan z)
set_target_properties(amd_fidelityfx_framegeneration_vk PROPERTIES
  CXX_VISIBILITY_PRESET hidden
  VISIBILITY_INLINES_HIDDEN ON)
