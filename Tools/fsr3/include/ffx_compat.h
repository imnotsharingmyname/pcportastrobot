// Force-included for every FidelityFX translation unit on Linux.
//
// The SDK targets MSVC: ffx_api.h spells the export attribute as
// __declspec(dllexport), and several declarations rely on MSVC-only helpers.
// Mapping them here keeps the vendor sources untouched.
#pragma once

#include <cstdint>
#include <cstring>
#include <cstdio>
#include <cwchar>
#include <wchar.h>
#include <cmath>
#include <cstdarg>
#include <codecvt>
#include <locale>

#ifndef UNREFERENCED_PARAMETER
#define UNREFERENCED_PARAMETER(x) ((void)(x))
#endif

using std::floor;
using std::log2;

#if !defined(_WIN32)
// FFX_API_ENTRY expands to this, which is what makes the five ffx* entry points
// visible to dlsym even though the target defaults to hidden visibility.
#define __declspec(x) __attribute__((visibility("default")))

#define _TRUNCATE ((size_t)-1)
#ifndef _countof
#define _countof(a) (sizeof(a) / sizeof((a)[0]))
#endif
static inline int wcscpy_s(wchar_t* dest, size_t destSize, const wchar_t* src) {
    const size_t copied = std::wcslen(src);
    if (copied >= destSize) {
        dest[0] = L'\0';
        return 1; // ERANGE
    }
    std::wmemcpy(dest, src, copied + 1);
    return 0;
}

template <size_t N>
static inline int wcscpy_s(wchar_t (&dest)[N], const wchar_t* src) {
    return wcscpy_s(dest, N, src);
}

static inline int swprintf_s(wchar_t* dest, size_t destSize, const wchar_t* format, ...) {
    va_list args;
    va_start(args, format);
    const int written = vswprintf(dest, destSize, format, args);
    va_end(args);
    return written;
}

static inline int strcpy_s(char* dest, size_t destSize, const char* src) {
    const size_t copied = strlen(src);
    if (copied >= destSize) {
        dest[0] = '\0';
        return 1; // ERANGE
    }
    memcpy(dest, src, copied + 1);
    return 0;
}

static inline int strncpy_s(char* dest, size_t destSize, const char* src, size_t count) {
    const size_t limit = count == _TRUNCATE ? destSize - 1 : count;
    const size_t copied = strnlen(src, limit);
    if (copied >= destSize) return 1; // ERANGE
    memcpy(dest, src, copied);
    dest[copied] = '\0';
    return 0;
}
#endif

#include "ffx_util.h"

#ifndef FFX_UNUSED
#define FFX_UNUSED(x) ((void)(x))
#endif
