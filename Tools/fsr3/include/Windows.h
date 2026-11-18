#pragma once

// Win32 shim for the FidelityFX Vulkan swapchain sources.
//
// Those sources are written for MSVC: they take a CRITICAL_SECTION, start the
// presenter with CreateThread, pace it with an event and time it with
// QueryPerformanceCounter. On Linux the same structure maps onto pthreads and
// clock_gettime, which keeps AMD's threading model intact instead of replacing
// it.

#include <cstdint>
#include <cstring>
#include <ctime>
#include <pthread.h>
#include <time.h>

typedef uint64_t UINT64;
typedef uint32_t UINT;
typedef uint32_t DWORD;
typedef int32_t  BOOL;
typedef void*    LPVOID;
typedef int64_t  LONGLONG;

#define WINAPI
#define TRUE 1
#define FALSE 0
#define INFINITE 0xFFFFFFFFu
#define THREAD_PRIORITY_HIGHEST 2

typedef struct { int64_t QuadPart; } LARGE_INTEGER;

// One opaque type serves both thread and event handles, as it does on Win32.
typedef struct WinHandle* HANDLE;

struct WinHandle {
    int            kind;        // 0 = thread, 1 = event
    pthread_t      thread;
    pthread_mutex_t mutex;
    pthread_cond_t  cond;
    int            signaled;
    int            manualReset;
    int            joined;      // a thread handle must only be joined once
};

// ---------------------------------------------------------------- critical sections

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

// ------------------------------------------------------------------------ threads

static inline HANDLE CreateThread(void*, size_t, DWORD(WINAPI* start)(LPVOID),
                                  LPVOID parameter, DWORD, DWORD*) {
    auto* handle = new WinHandle{};
    handle->kind = 0;
    if (pthread_create(&handle->thread, nullptr, reinterpret_cast<void* (*)(void*)>(start), parameter) != 0) {
        delete handle;
        return nullptr;
    }
    return handle;
}

static inline BOOL SetThreadPriority(HANDLE, int) {
    return TRUE;
}

static inline BOOL SetThreadDescription(HANDLE, const wchar_t*) {
    return TRUE;
}

#define TEXT(x) x

// ------------------------------------------------------------------------- events

static inline HANDLE CreateEvent(void*, BOOL manualReset, BOOL initialState, const char*) {
    auto* handle = new WinHandle{};
    handle->kind = 1;
    handle->manualReset = manualReset;
    handle->signaled = initialState;
    pthread_mutex_init(&handle->mutex, nullptr);
    pthread_cond_init(&handle->cond, nullptr);
    return handle;
}

static inline BOOL SetEvent(HANDLE handle) {
    if (handle == nullptr || handle->kind != 1) return FALSE;
    pthread_mutex_lock(&handle->mutex);
    handle->signaled = 1;
    // Auto-reset events release one waiter; manual-reset release all.
    if (handle->manualReset) {
        pthread_cond_broadcast(&handle->cond);
    } else {
        pthread_cond_signal(&handle->cond);
    }
    pthread_mutex_unlock(&handle->mutex);
    return TRUE;
}

static inline BOOL ResetEvent(HANDLE handle) {
    if (handle == nullptr || handle->kind != 1) return FALSE;
    pthread_mutex_lock(&handle->mutex);
    handle->signaled = 0;
    pthread_mutex_unlock(&handle->mutex);
    return TRUE;
}

// Returns 0 (= WAIT_OBJECT_0) when the wait succeeded, 0x102 on timeout.
static inline DWORD WaitForSingleObject(HANDLE handle, DWORD milliseconds) {
    if (handle == nullptr) return 0xFFFFFFFFu;
    if (handle->kind == 0) {
        if (!handle->joined) {
            pthread_join(handle->thread, nullptr);
            handle->joined = 1;
        }
        return 0;
    }

    pthread_mutex_lock(&handle->mutex);
    if (milliseconds == INFINITE) {
        while (handle->signaled == 0) pthread_cond_wait(&handle->cond, &handle->mutex);
    } else {
        timespec deadline{};
        clock_gettime(CLOCK_MONOTONIC, &deadline);
        deadline.tv_sec += milliseconds / 1000;
        deadline.tv_nsec += (milliseconds % 1000) * 1000000L;
        if (deadline.tv_nsec >= 1000000000L) { deadline.tv_sec += 1; deadline.tv_nsec -= 1000000000L; }
        while (handle->signaled == 0) {
            if (pthread_cond_timedwait(&handle->cond, &handle->mutex, &deadline) != 0) break;
        }
    }
    const int wasSignaled = handle->signaled;
    if (!handle->manualReset) handle->signaled = 0;
    pthread_mutex_unlock(&handle->mutex);
    return wasSignaled ? 0u : 0x102u;
}

static inline BOOL CloseHandle(HANDLE handle) {
    if (handle == nullptr) return FALSE;
    if (handle->kind == 0) {
        if (!handle->joined) {
            pthread_join(handle->thread, nullptr);
            handle->joined = 1;
        }
    } else {
        pthread_mutex_destroy(&handle->mutex);
        pthread_cond_destroy(&handle->cond);
    }
    delete handle;
    return TRUE;
}

// ------------------------------------------------------------------------- timing

static inline BOOL QueryPerformanceFrequency(LARGE_INTEGER* frequency) {
    if (frequency == nullptr) return FALSE;
    frequency->QuadPart = 1000000000LL;   // nanoseconds
    return TRUE;
}

static inline BOOL QueryPerformanceCounter(LARGE_INTEGER* counter) {
    if (counter == nullptr) return FALSE;
    timespec now{};
    clock_gettime(CLOCK_MONOTONIC, &now);
    counter->QuadPart = static_cast<int64_t>(now.tv_sec) * 1000000000LL + now.tv_nsec;
    return TRUE;
}
