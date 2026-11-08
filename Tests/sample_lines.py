"""Resolve thread_sampler.py raw offsets of one module to source lines, offline, through its PDB.

  python -I Tests/sample_lines.py <samples.raw> <module.dll> [--function SUBSTRING] [--top 40]
Raw lines are "<module> 0x<offset> <count>". Only offsets of the given module's file name are used.
"""
import argparse, collections, ctypes, ctypes.wintypes as W, os

dbg = ctypes.WinDLL("dbghelp", use_last_error=True)
k32 = ctypes.WinDLL("kernel32")


class SYMBOL_INFOW(ctypes.Structure):
    _fields_ = [("SizeOfStruct", W.ULONG), ("TypeIndex", W.ULONG), ("Reserved", ctypes.c_uint64 * 2),
                ("Index", W.ULONG), ("Size", W.ULONG), ("ModBase", ctypes.c_uint64), ("Flags", W.ULONG),
                ("Value", ctypes.c_uint64), ("Address", ctypes.c_uint64), ("Register", W.ULONG),
                ("Scope", W.ULONG), ("Tag", W.ULONG), ("NameLen", W.ULONG), ("MaxNameLen", W.ULONG),
                ("Name", W.WCHAR * 512)]


class IMAGEHLP_LINEW64(ctypes.Structure):
    _fields_ = [("SizeOfStruct", W.DWORD), ("Key", ctypes.c_void_p), ("LineNumber", W.DWORD),
                ("FileName", W.LPWSTR), ("Address", ctypes.c_uint64)]


dbg.SymInitializeW.argtypes = [W.HANDLE, W.LPCWSTR, W.BOOL]
dbg.SymLoadModuleExW.argtypes = [W.HANDLE, W.HANDLE, W.LPCWSTR, W.LPCWSTR, ctypes.c_uint64, W.DWORD, ctypes.c_void_p, W.DWORD]
dbg.SymLoadModuleExW.restype = ctypes.c_uint64
dbg.SymFromAddrW.argtypes = [W.HANDLE, ctypes.c_uint64, ctypes.POINTER(ctypes.c_uint64), ctypes.POINTER(SYMBOL_INFOW)]
dbg.SymGetLineFromAddrW64.argtypes = [W.HANDLE, ctypes.c_uint64, ctypes.POINTER(W.DWORD), ctypes.POINTER(IMAGEHLP_LINEW64)]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("raw")
    ap.add_argument("module")
    ap.add_argument("--function")
    ap.add_argument("--top", type=int, default=40)
    a = ap.parse_args()
    name = os.path.basename(a.module).lower()
    proc = W.HANDLE(0x7F000001)
    dbg.SymSetOptions(0x2 | 0x10 | 0x10000)  # undname, load lines, no prompts
    dbg.SymInitializeW(proc, os.path.dirname(os.path.abspath(a.module)), False)
    base = 0x180000000
    if not dbg.SymLoadModuleExW(proc, None, os.path.abspath(a.module), None, base, 0, None, 0):
        raise SystemExit("could not load module symbols")
    sym = SYMBOL_INFOW(); sym.SizeOfStruct = 88; sym.MaxNameLen = 511
    line = IMAGEHLP_LINEW64(); line.SizeOfStruct = ctypes.sizeof(IMAGEHLP_LINEW64)
    disp64, disp32 = ctypes.c_uint64(), W.DWORD()
    lines, funcs, total = collections.Counter(), collections.Counter(), 0
    for row in open(a.raw, encoding="utf-8"):
        parts = row.split()
        if len(parts) != 3 or parts[0].lower() != name:
            continue
        address, count = base + int(parts[1], 16), int(parts[2])
        func = sym.Name if dbg.SymFromAddrW(proc, address, ctypes.byref(disp64), ctypes.byref(sym)) else "?"
        if a.function and a.function not in func:
            continue
        total += count
        funcs[func] += count
        if dbg.SymGetLineFromAddrW64(proc, address, ctypes.byref(disp32), ctypes.byref(line)):
            where = f"{os.path.basename(line.FileName)}:{line.LineNumber}"
        else:
            where = "?"
        lines[f"{where}  [{func[:60]}]"] += count
    print(f"{total} samples")
    for key, count in lines.most_common(a.top):
        print(f"{100 * count / max(total, 1):6.2f}%  {count:6}  {key}")


if __name__ == "__main__":
    main()
