#pragma once

// The FidelityFX Vulkan swapchain sources are written for MSVC: they include
// <Windows.h>, take a CRITICAL_SECTION for their lock, and reach the backend
// header by a backslash path. On Linux the guarded paths need none of the Win32
// API, but they do need the lock primitives to exist, so those are mapped onto
// pthreads here.

#include <cstdint>
#include <pthread.h>

typedef uint64_t UINT64;
typedef uint32_t UINT;
typedef uint32_t DWORD;
typedef int32_t  BOOL;
typedef void*    HANDLE;
typedef struct { int64_t QuadPart; } LARGE_INTEGER;
#define TRUE 1
#define FALSE 0

typedef struct CRITICAL_SECTION {
    pthread_mutex_t mutex;
} CRITICAL_SECTION;

typedef CRITICAL_SECTION* LPCRITICAL_SECTION;

static inline void InitializeCriticalSection(CRITICAL_SECTION* section) {
    pthread_mutex_init(&section->mutex, nullptr);
}

static inline void DeleteCriticalSection(CRITICAL_SECTION* section) {
    pthread_mutex_destroy(&section->mutex);
}

static inline void EnterCriticalSection(CRITICAL_SECTION* section) {
    pthread_mutex_lock(&section->mutex);
}

static inline void LeaveCriticalSection(CRITICAL_SECTION* section) {
    pthread_mutex_unlock(&section->mutex);
}
