import ctypes, struct, subprocess, sys, time
from ctypes import wintypes as W

K = ctypes.WinDLL("kernel32", use_last_error=True)
K.OpenProcess.restype = W.HANDLE
K.ReadProcessMemory.argtypes = [W.HANDLE, ctypes.c_void_p, ctypes.c_void_p, ctypes.c_size_t, ctypes.POINTER(ctypes.c_size_t)]
K.WriteProcessMemory.argtypes = K.ReadProcessMemory.argtypes
FPS, DT, US = 0x93F0040, 0x93F0048, 0x93F0050
MEMSCAN = r"C:\Users\GAMING PC\Desktop\AstroBot-PC-Port\work\re\memscan.exe"
CAND = r"C:\Users\GAMING PC\Desktop\AstroBot-PC-Port\work\re\probe-dt.bin"


def rd(h, a, n):
    b = ctypes.create_string_buffer(n); got = ctypes.c_size_t()
    return b.raw if K.ReadProcessMemory(h, a, b, n, ctypes.byref(got)) and got.value == n else None


def wr(h, a, data):
    got = ctypes.c_size_t()
    return K.WriteProcessMemory(h, a, data, len(data), ctypes.byref(got))


def globals_(h):
    b = rd(h, FPS, 24)
    return struct.unpack("<dfIQ", b)


def clocks(h, addrs):
    out = {}
    for a in addrs:
        b = rd(h, a - 4, 4)
        if b: out[a] = struct.unpack("<f", b)[0]
    return out


def speed(h, addrs, secs):
    a0 = clocks(h, addrs); t0 = time.perf_counter(); time.sleep(secs)
    a1 = clocks(h, addrs); t1 = time.perf_counter()
    ds = sorted((a1[k] - a0[k]) / (t1 - t0) for k in a0 if k in a1 and 0 < a1[k] - a0[k] < 10 * (t1 - t0))
    return (ds[len(ds) // 2] if ds else 0.0), len(ds)


def set_rate(h, fps):
    wr(h, FPS, struct.pack("<d", fps)); wr(h, DT, struct.pack("<f", 1.0 / fps)); wr(h, US, struct.pack("<Q", int(1e6 / fps)))


def main():
    pid = int(sys.argv[1]); plan = [] if sys.argv[2:] == ["measure"] else [float(x) for x in sys.argv[2:]] or [30.0, 60.0]
    h = K.OpenProcess(0x0438, False, pid)
    print("globals", globals_(h), flush=True)
    g = globals_(h)
    subprocess.run([MEMSCAN, "snap", str(pid), CAND, "%.7f" % (g[1] - 1e-5), "%.7f" % (g[1] + 1e-5)], check=True)
    d = open(CAND, "rb").read()
    recs = [struct.unpack_from("<QBd", d, i) for i in range(0, len(d), 17)]
    addrs = [a for a, k, v in recs if k == 4]
    v0 = clocks(h, addrs); time.sleep(2); v1 = clocks(h, addrs)
    live = [a for a in v0 if a in v1 and 0.3 < v1[a] - v0[a] < 4.5 and v1[a] > 5]
    print("dt+clock pairs", len(live), flush=True)
    for _ in range(1 if plan else 3):
        print("globals", globals_(h), "speed (game s per real s): %.3f n=%d" % speed(h, live, 8), flush=True)
    for fps in plan:
        set_rate(h, fps); time.sleep(2)
        print("globals", globals_(h), flush=True)
        print("speed at step 1/%g: %.3f n=%d" % ((fps,) + speed(h, live, 8)), flush=True)


main()
