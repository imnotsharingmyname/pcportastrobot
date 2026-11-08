"""Per-thread CPU usage of a running process, by Windows thread description.

Usage: python -I thread_cpu.py <process name or pid> [seconds=5] [top=25]
Takes two GetThreadTimes snapshots `seconds` apart and prints each thread's share of one core,
grouped by description, plus the process total in cores.
"""
import collections
import ctypes
import ctypes.wintypes as wt
import sys
import time

k32 = ctypes.WinDLL('kernel32', use_last_error=True)
TH32CS_SNAPTHREAD, TH32CS_SNAPPROCESS = 0x4, 0x2
THREAD_QUERY_LIMITED_INFORMATION = 0x0800


class THREADENTRY32(ctypes.Structure):
    _fields_ = [('dwSize', wt.DWORD), ('cntUsage', wt.DWORD), ('th32ThreadID', wt.DWORD), ('th32OwnerProcessID', wt.DWORD),
                ('tpBasePri', wt.LONG), ('tpDeltaPri', wt.LONG), ('dwFlags', wt.DWORD)]


class PROCESSENTRY32W(ctypes.Structure):
    _fields_ = [('dwSize', wt.DWORD), ('cntUsage', wt.DWORD), ('th32ProcessID', wt.DWORD), ('th32DefaultHeapID', ctypes.c_void_p),
                ('th32ModuleID', wt.DWORD), ('cntThreads', wt.DWORD), ('th32ParentProcessID', wt.DWORD), ('pcPriClassBase', wt.LONG),
                ('dwFlags', wt.DWORD), ('szExeFile', ctypes.c_wchar * 260)]


k32.CreateToolhelp32Snapshot.restype = wt.HANDLE
k32.OpenThread.restype = wt.HANDLE
k32.GetThreadDescription.argtypes = [wt.HANDLE, ctypes.POINTER(ctypes.c_wchar_p)]


def find_pid(target):
    if target.isdigit():
        return int(target)
    snap = k32.CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0)
    entry = PROCESSENTRY32W(dwSize=ctypes.sizeof(PROCESSENTRY32W))
    ok = k32.Process32FirstW(snap, ctypes.byref(entry))
    while ok:
        if entry.szExeFile.lower() in (target.lower(), target.lower() + '.exe'):
            k32.CloseHandle(snap)
            return entry.th32ProcessID
        ok = k32.Process32NextW(snap, ctypes.byref(entry))
    k32.CloseHandle(snap)
    raise SystemExit(f'process {target} not found')


def snapshot(pid):
    snap = k32.CreateToolhelp32Snapshot(TH32CS_SNAPTHREAD, 0)
    entry = THREADENTRY32(dwSize=ctypes.sizeof(THREADENTRY32))
    result = {}
    ok = k32.Thread32First(snap, ctypes.byref(entry))
    while ok:
        if entry.th32OwnerProcessID == pid:
            handle = k32.OpenThread(THREAD_QUERY_LIMITED_INFORMATION, False, entry.th32ThreadID)
            if handle:
                times = [wt.FILETIME() for _ in range(4)]
                if k32.GetThreadTimes(handle, *[ctypes.byref(t) for t in times]):
                    cpu = sum((t.dwHighDateTime << 32 | t.dwLowDateTime) for t in times[2:]) / 1e7
                    desc = ctypes.c_wchar_p()
                    name = ''
                    if k32.GetThreadDescription(handle, ctypes.byref(desc)) >= 0 and desc.value:
                        name = desc.value
                        k32.LocalFree(desc)
                    result[entry.th32ThreadID] = (name or f'tid {entry.th32ThreadID}', cpu)
                k32.CloseHandle(handle)
        ok = k32.Thread32Next(snap, ctypes.byref(entry))
    k32.CloseHandle(snap)
    return result


def main():
    pid = find_pid(sys.argv[1])
    seconds = float(sys.argv[2]) if len(sys.argv) > 2 else 5.0
    top = int(sys.argv[3]) if len(sys.argv) > 3 else 25
    first = snapshot(pid)
    time.sleep(seconds)
    second = snapshot(pid)
    per_thread = []
    for tid, (name, cpu) in second.items():
        if tid in first:
            per_thread.append((name, (cpu - first[tid][1]) / seconds * 100))
    groups = collections.defaultdict(lambda: [0, 0.0, 0.0])
    for name, pct in per_thread:
        g = groups[name]
        g[0] += 1
        g[1] += pct
        g[2] = max(g[2], pct)
    total = sum(p for _, p in per_thread)
    print(f'pid {pid}: {len(per_thread)} threads, {total / 100:.2f} cores over {seconds:.0f} s')
    print(f'{"thread":40} {"count":>5} {"sum %core":>10} {"max %core":>10}')
    for name, (count, pct, peak) in sorted(groups.items(), key=lambda kv: -kv[1][1])[:top]:
        print(f'{name[:40]:40} {count:5} {pct:10.1f} {peak:10.1f}')


if __name__ == '__main__':
    main()
