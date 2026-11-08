"""Read-only external sampler for one thread of a running AstroBot process (Windows, x64).

Every ~1 ms it suspends the chosen thread, reads its instruction pointer, resumes it, and at the end
symbolizes the addresses with dbghelp (PDBs next to the modules, e.g. kyty_gpu.pdb).
Self time only (no stack walk): the hottest functions the thread is executing.

  python -I Tests/thread_sampler.py --list [--process AstroBot.exe]
  python -I Tests/thread_sampler.py --thread-name <substring> --seconds 30 --out <file>
"""
import argparse, collections, ctypes, ctypes.wintypes as W, os, sys, time

k32 = ctypes.WinDLL("kernel32", use_last_error=True)
dbg = ctypes.WinDLL("dbghelp", use_last_error=True)
psapi = ctypes.WinDLL("psapi", use_last_error=True)

TH32CS_SNAPPROCESS, TH32CS_SNAPTHREAD = 0x2, 0x4
THREAD_ACCESS = 0x0002 | 0x0008 | 0x0040 | 0x0800  # suspend/resume, get context, query, query limited
PROCESS_ACCESS = 0x0400 | 0x0010  # query information, vm read
CONTEXT_CONTROL = 0x00100001


class PROCESSENTRY32W(ctypes.Structure):
    _fields_ = [("dwSize", W.DWORD), ("cntUsage", W.DWORD), ("th32ProcessID", W.DWORD),
                ("th32DefaultHeapID", ctypes.c_void_p), ("th32ModuleID", W.DWORD), ("cntThreads", W.DWORD),
                ("th32ParentProcessID", W.DWORD), ("pcPriClassBase", W.LONG), ("dwFlags", W.DWORD),
                ("szExeFile", W.WCHAR * 260)]


class THREADENTRY32(ctypes.Structure):
    _fields_ = [("dwSize", W.DWORD), ("cntUsage", W.DWORD), ("th32ThreadID", W.DWORD),
                ("th32OwnerProcessID", W.DWORD), ("tpBasePri", W.LONG), ("tpDeltaPri", W.LONG), ("dwFlags", W.DWORD)]


class CONTEXT(ctypes.Structure):
    _pack_ = 16
    _fields_ = [("P1Home", ctypes.c_uint64 * 6), ("ContextFlags", W.DWORD), ("MxCsr", W.DWORD),
                ("Segs", W.WORD * 6), ("EFlags", W.DWORD), ("Dr", ctypes.c_uint64 * 6),
                ("Gpr", ctypes.c_uint64 * 16), ("Rip", ctypes.c_uint64), ("Rest", ctypes.c_byte * 976)]


class SYMBOL_INFOW(ctypes.Structure):
    _fields_ = [("SizeOfStruct", W.ULONG), ("TypeIndex", W.ULONG), ("Reserved", ctypes.c_uint64 * 2),
                ("Index", W.ULONG), ("Size", W.ULONG), ("ModBase", ctypes.c_uint64), ("Flags", W.ULONG),
                ("Value", ctypes.c_uint64), ("Address", ctypes.c_uint64), ("Register", W.ULONG),
                ("Scope", W.ULONG), ("Tag", W.ULONG), ("NameLen", W.ULONG), ("MaxNameLen", W.ULONG),
                ("Name", W.WCHAR * 512)]


k32.CreateToolhelp32Snapshot.restype = W.HANDLE
k32.OpenThread.restype = W.HANDLE
k32.OpenProcess.restype = W.HANDLE
k32.GetThreadDescription.argtypes = [W.HANDLE, ctypes.POINTER(W.LPWSTR)]
dbg.SymFromAddrW.argtypes = [W.HANDLE, ctypes.c_uint64, ctypes.POINTER(ctypes.c_uint64), ctypes.POINTER(SYMBOL_INFOW)]
dbg.SymInitializeW.argtypes = [W.HANDLE, W.LPCWSTR, W.BOOL]
dbg.SymSetOptions.argtypes = [W.DWORD]
psapi.EnumProcessModulesEx.argtypes = [W.HANDLE, ctypes.POINTER(W.HMODULE), W.DWORD, ctypes.POINTER(W.DWORD), W.DWORD]
psapi.GetModuleFileNameExW.argtypes = [W.HANDLE, W.HMODULE, W.LPWSTR, W.DWORD]


def find_process(name):
    snap = k32.CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0)
    e = PROCESSENTRY32W(); e.dwSize = ctypes.sizeof(e)
    found = None
    ok = k32.Process32FirstW(snap, ctypes.byref(e))
    while ok:
        if e.szExeFile.lower() == name.lower():
            found = e.th32ProcessID
        ok = k32.Process32NextW(snap, ctypes.byref(e))
    k32.CloseHandle(snap)
    return found


def threads(pid):
    snap = k32.CreateToolhelp32Snapshot(TH32CS_SNAPTHREAD, 0)
    e = THREADENTRY32(); e.dwSize = ctypes.sizeof(e)
    out = []
    ok = k32.Thread32First(snap, ctypes.byref(e))
    while ok:
        if e.th32OwnerProcessID == pid:
            h = k32.OpenThread(THREAD_ACCESS, False, e.th32ThreadID)
            if h:
                p = W.LPWSTR()
                name = ""
                if k32.GetThreadDescription(h, ctypes.byref(p)) >= 0 and p.value:
                    name = p.value
                    k32.LocalFree(p)
                out.append((e.th32ThreadID, name, h))
        ok = k32.Thread32Next(snap, ctypes.byref(e))
    k32.CloseHandle(snap)
    return out


def modules(proc):
    arr = (W.HMODULE * 1024)(); needed = W.DWORD()
    psapi.EnumProcessModulesEx(proc, arr, ctypes.sizeof(arr), ctypes.byref(needed), 3)
    out = []
    buf = ctypes.create_unicode_buffer(520)
    for i in range(needed.value // ctypes.sizeof(W.HMODULE)):
        base = arr[i] or 0
        psapi.GetModuleFileNameExW(proc, arr[i], buf, 520)
        out.append((base, buf.value))
    return sorted(out)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--process", default="AstroBot.exe")
    ap.add_argument("--list", action="store_true")
    ap.add_argument("--thread-name")
    ap.add_argument("--busiest-unnamed", type=int, default=0, metavar="N")
    ap.add_argument("--seconds", type=float, default=30)
    ap.add_argument("--interval-ms", type=float, default=1.0)
    ap.add_argument("--out")
    ap.add_argument("--top", type=int, default=60)
    a = ap.parse_args()
    pid = find_process(a.process)
    if pid is None:
        sys.exit(f"{a.process} is not running")
    ts = threads(pid)
    if a.list:
        for tid, name, h in ts:
            print(tid, name)
        return
    if a.busiest_unnamed:
        def cpu(h):
            c, e, k, u = (W.FILETIME() for _ in range(4))
            k32.GetThreadTimes(h, ctypes.byref(c), ctypes.byref(e), ctypes.byref(k), ctypes.byref(u))
            return ((k.dwHighDateTime << 32) | k.dwLowDateTime) + ((u.dwHighDateTime << 32) | u.dwLowDateTime)
        unnamed = [t for t in ts if not t[1]]
        before = {t[0]: cpu(t[2]) for t in unnamed}
        time.sleep(2.0)
        load = {t[0]: (cpu(t[2]) - before[t[0]]) / 2e7 * 100 for t in unnamed}
        ranked = sorted(unnamed, key=lambda t: load[t[0]], reverse=True)
        for t in ranked[:max(5, a.busiest_unnamed)]:
            print(f"unnamed thread {t[0]}: {load[t[0]]:.0f}% of a core")
        chosen = ranked[:a.busiest_unnamed]
    else:
        chosen = [t for t in ts if a.thread_name and a.thread_name.lower() in t[1].lower()]
        if len(chosen) != 1:
            sys.exit(f"thread name '{a.thread_name}' matched {len(chosen)} threads: {[t[1] for t in chosen]}")
    buffer = ctypes.create_string_buffer(ctypes.sizeof(CONTEXT) + 16)
    aligned = (ctypes.addressof(buffer) + 15) & ~15
    ctx = CONTEXT.from_address(aligned); ctx.ContextFlags = CONTEXT_CONTROL
    counts = {t[0]: collections.Counter() for t in chosen}
    samples = {t[0]: 0 for t in chosen}
    end = time.perf_counter() + a.seconds
    while time.perf_counter() < end:
        for tid, _, h in chosen:
            if k32.SuspendThread(h) != 0xFFFFFFFF:
                if k32.GetThreadContext(h, ctypes.byref(ctx)):
                    counts[tid][ctx.Rip] += 1
                    samples[tid] += 1
                k32.ResumeThread(h)
        time.sleep(a.interval_ms / 1000.0)
    proc = k32.OpenProcess(PROCESS_ACCESS, False, pid)
    mods = modules(proc)
    dbg.SymSetOptions(0x00000002 | 0x00000004 | 0x00010000)
    dirs = ";".join(sorted({os.path.dirname(p) for _, p in mods}))
    dbg.SymInitializeW(proc, dirs, True)
    sym = SYMBOL_INFOW(); sym.SizeOfStruct = 88; sym.MaxNameLen = 511
    disp = ctypes.c_uint64()
    lines, raw_rows = [], []
    for tid, tname, _ in chosen:
        by_func, by_mod, n_all = collections.Counter(), collections.Counter(), max(samples[tid], 1)
        for rip, n in counts[tid].items():
            base, path = next(((b, p) for b, p in reversed(mods) if b <= rip), (0, "?"))
            mod = os.path.basename(path)
            by_mod[mod] += n
            raw_rows.append(f"{tid} {mod} 0x{rip - base:x} {n}" if len(chosen) > 1 else f"{mod} 0x{rip - base:x} {n}")
            name = sym.Name if dbg.SymFromAddrW(proc, rip, ctypes.byref(disp), ctypes.byref(sym)) else "?"
            by_func[f"{mod}!{name}"] += n
        lines.append(f"thread {tid} '{tname}' samples {samples[tid]} over {a.seconds:.0f} s")
        lines.append("modules: " + ", ".join(f"{m} {100*n/n_all:.1f}%" for m, n in by_mod.most_common(8)))
        for f, n in by_func.most_common(a.top):
            lines.append(f"{100*n/n_all:6.2f}%  {n:6}  {f}")
        lines.append("")
    text = "\n".join(lines)
    print(text)
    if a.out:
        open(a.out, "w", encoding="utf-8").write(text + "\n")
        open(a.out + ".raw", "w", encoding="utf-8").write("\n".join(raw_rows) + "\n")


if __name__ == "__main__":
    main()
