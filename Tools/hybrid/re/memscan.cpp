// memscan: external memory scanner for a running process (Windows x64), used to find Astro Bot's
// game clock and per-frame time step.
//   memscan snap  <pid> <out> <lo> <hi>              float32/float64 values in [lo, hi] -> candidates file
//   memscan delta <pid> <in> <out> <dlo> <dhi>       keep candidates whose value changed by [dlo, dhi]
//   memscan show  <pid> <in> [max]                   print candidates with their current values
//   memscan near  <pid> <addr> [bytes]               dump floats/doubles/ints around an address
//   memscan write <pid> <addr> f32|f64|u64 <value>       write one value (live experiments only)
// Candidates file: records {uint64 address; uint8 kind (4 = float, 8 = double); double value}.
#include <windows.h>
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <vector>

#pragma pack(push, 1)
struct Rec {
    std::uint64_t addr;
    std::uint8_t kind;
    double value;
};
#pragma pack(pop)

static HANDLE OpenTarget(DWORD pid, bool write) {
    DWORD access = PROCESS_QUERY_INFORMATION | PROCESS_VM_READ;
    if (write) access |= PROCESS_VM_WRITE | PROCESS_VM_OPERATION;
    HANDLE h = OpenProcess(access, FALSE, pid);
    if (!h) { std::fprintf(stderr, "OpenProcess failed %lu\n", GetLastError()); std::exit(2); }
    return h;
}

static bool Readable(const MEMORY_BASIC_INFORMATION& m) {
    if (m.State != MEM_COMMIT) return false;
    const DWORD p = m.Protect & 0xff;
    if (m.Protect & PAGE_GUARD) return false;
    return p == PAGE_READWRITE || p == PAGE_WRITECOPY || p == PAGE_READONLY;
}

static bool ReadValue(HANDLE h, std::uint64_t addr, std::uint8_t kind, double& out) {
    SIZE_T got = 0;
    if (kind == 4) {
        float f;
        if (!ReadProcessMemory(h, reinterpret_cast<LPCVOID>(addr), &f, 4, &got) || got != 4) return false;
        out = f;
    } else {
        double d;
        if (!ReadProcessMemory(h, reinterpret_cast<LPCVOID>(addr), &d, 8, &got) || got != 8) return false;
        out = d;
    }
    return true;
}

static int Snap(DWORD pid, const char* path, double lo, double hi) {
    HANDLE h = OpenTarget(pid, false);
    FILE* f = std::fopen(path, "wb");
    std::vector<unsigned char> buf;
    std::uint64_t addr = 0, regions = 0, bytes = 0, found = 0;
    MEMORY_BASIC_INFORMATION m;
    while (VirtualQueryEx(h, reinterpret_cast<LPCVOID>(addr), &m, sizeof(m)) == sizeof(m)) {
        const std::uint64_t base = reinterpret_cast<std::uint64_t>(m.BaseAddress);
        const std::uint64_t size = m.RegionSize;
        if (Readable(m) && (m.Type == MEM_PRIVATE || m.Type == MEM_MAPPED)) {
            for (std::uint64_t at = 0; at < size; at += (64ull << 20)) {
                const std::uint64_t n = std::min<std::uint64_t>(64ull << 20, size - at);
                buf.resize(n);
                SIZE_T got = 0;
                if (!ReadProcessMemory(h, reinterpret_cast<LPCVOID>(base + at), buf.data(), n, &got) || got == 0) continue;
                bytes += got;
                for (SIZE_T i = 0; i + 4 <= got; i += 4) {
                    float v; std::memcpy(&v, &buf[i], 4);
                    if (v >= lo && v <= hi) { Rec r{base + at + i, 4, v}; std::fwrite(&r, sizeof r, 1, f); ++found; }
                }
                for (SIZE_T i = 0; i + 8 <= got; i += 8) {
                    double v; std::memcpy(&v, &buf[i], 8);
                    if (v >= lo && v <= hi) { Rec r{base + at + i, 8, v}; std::fwrite(&r, sizeof r, 1, f); ++found; }
                }
            }
            ++regions;
        }
        addr = base + size;
        if (addr >= 0x7fffffff0000ull) break;
    }
    std::fclose(f);
    std::printf("regions %llu, read %.2f GiB, candidates %llu\n", (unsigned long long)regions, bytes / 1073741824.0, (unsigned long long)found);
    return 0;
}

static std::vector<Rec> Load(const char* path) {
    std::vector<Rec> v;
    FILE* f = std::fopen(path, "rb");
    if (!f) { std::fprintf(stderr, "cannot open %s\n", path); std::exit(2); }
    Rec r;
    while (std::fread(&r, sizeof r, 1, f) == 1) v.push_back(r);
    std::fclose(f);
    return v;
}

static int Delta(DWORD pid, const char* in, const char* out, double dlo, double dhi) {
    HANDLE h = OpenTarget(pid, false);
    auto v = Load(in);
    FILE* f = std::fopen(out, "wb");
    std::uint64_t kept = 0;
    for (auto& r : v) {
        double now;
        if (!ReadValue(h, r.addr, r.kind, now)) continue;
        const double d = now - r.value;
        if (d >= dlo && d <= dhi) { Rec n{r.addr, r.kind, now}; std::fwrite(&n, sizeof n, 1, f); ++kept; }
    }
    std::fclose(f);
    std::printf("in %zu, kept %llu\n", v.size(), (unsigned long long)kept);
    return 0;
}

static int Show(DWORD pid, const char* in, std::size_t max) {
    HANDLE h = OpenTarget(pid, false);
    auto v = Load(in);
    for (std::size_t i = 0; i < v.size() && i < max; ++i) {
        double now = 0;
        const bool ok = ReadValue(h, v[i].addr, v[i].kind, now);
        std::printf("0x%016llx %s was %.6f now %s%.6f\n", (unsigned long long)v[i].addr, v[i].kind == 4 ? "f32" : "f64", v[i].value, ok ? "" : "?", now);
    }
    std::printf("total %zu\n", v.size());
    return 0;
}

static int Near(DWORD pid, std::uint64_t addr, std::size_t bytes) {
    HANDLE h = OpenTarget(pid, false);
    const std::uint64_t start = (addr - bytes / 2) & ~7ull;
    std::vector<unsigned char> buf(bytes);
    SIZE_T got = 0;
    if (!ReadProcessMemory(h, reinterpret_cast<LPCVOID>(start), buf.data(), bytes, &got)) { std::printf("read failed\n"); return 1; }
    for (SIZE_T i = 0; i + 8 <= got; i += 8) {
        float f0, f1; double d; std::uint32_t u0, u1;
        std::memcpy(&f0, &buf[i], 4); std::memcpy(&f1, &buf[i + 4], 4); std::memcpy(&d, &buf[i], 8);
        std::memcpy(&u0, &buf[i], 4); std::memcpy(&u1, &buf[i + 4], 4);
        std::printf("%s0x%016llx  f32 %-14.7g %-14.7g  f64 %-16.9g  u32 %u %u\n", start + i == (addr & ~7ull) ? ">" : " ",
                    (unsigned long long)(start + i), f0, f1, d, u0, u1);
    }
    return 0;
}

static int Write(DWORD pid, std::uint64_t addr, const char* kind, double value) {
    HANDLE h = OpenTarget(pid, true);
    SIZE_T done = 0;
    BOOL ok;
    if (std::strcmp(kind, "f32") == 0) { float f = static_cast<float>(value); ok = WriteProcessMemory(h, reinterpret_cast<LPVOID>(addr), &f, 4, &done); }
    else if (std::strcmp(kind, "u64") == 0) { std::uint64_t u = static_cast<std::uint64_t>(value); ok = WriteProcessMemory(h, reinterpret_cast<LPVOID>(addr), &u, 8, &done); }
    else { ok = WriteProcessMemory(h, reinterpret_cast<LPVOID>(addr), &value, 8, &done); }
    std::printf("write %s (%zu bytes)\n", ok ? "ok" : "FAILED", (size_t)done);
    return ok ? 0 : 1;
}

int main(int argc, char** argv) {
    if (argc < 3) { std::fprintf(stderr, "see the header of memscan.cpp\n"); return 1; }
    const DWORD pid = static_cast<DWORD>(std::strtoul(argv[2], nullptr, 10));
    const char* cmd = argv[1];
    if (!std::strcmp(cmd, "snap") && argc == 6) return Snap(pid, argv[3], std::atof(argv[4]), std::atof(argv[5]));
    if (!std::strcmp(cmd, "delta") && argc == 7) return Delta(pid, argv[3], argv[4], std::atof(argv[5]), std::atof(argv[6]));
    if (!std::strcmp(cmd, "show") && argc >= 4) return Show(pid, argv[3], argc > 4 ? std::strtoull(argv[4], nullptr, 10) : 50);
    if (!std::strcmp(cmd, "near") && argc >= 4) return Near(pid, std::strtoull(argv[3], nullptr, 0), argc > 4 ? std::strtoull(argv[4], nullptr, 0) : 256);
    if (!std::strcmp(cmd, "write") && argc == 6) return Write(pid, std::strtoull(argv[3], nullptr, 0), argv[4], std::atof(argv[5]));
    std::fprintf(stderr, "bad arguments\n");
    return 1;
}
