import collections, re, struct, sys

ELF = r"C:\Users\GAMING PC\Desktop\AstroBot-PC-Port\Analysis\elf\eboot.elf"
NIDS = {
    "-2IRUCO--PM": "sceKernelReadTsc", "1j3S3n-tTW4": "sceKernelGetTscFrequency",
    "4J2sUJmuHZQ": "sceKernelGetProcessTime", "fgxnMeTNUtY": "sceKernelGetProcessTimeCounter",
    "BNowx2l588E": "sceKernelGetProcessTimeCounterFrequency", "QBi7HCK03hw": "sceKernelClockGettime",
    "lLMT9vJAck0": "clock_gettime", "ejekcaNQNq0": "sceKernelGettimeofday", "n88vx3C5nW8": "gettimeofday",
    "1FZBKy8HeNU": "sceVideoOutGetVblankStatus", "SbU3dwp80lQ": "sceVideoOutGetFlipStatus",
    "CBiu4mCE1DA": "sceVideoOutSetFlipRate",
}
d = open(ELF, "rb").read()
phoff, = struct.unpack_from("<Q", d, 0x20); phn, = struct.unpack_from("<H", d, 0x38)
ph = [struct.unpack_from("<IIQQQQQQ", d, phoff + i * 56) for i in range(phn)]
def v2o(va):
    for t, f, off, v, _, fs, ms, _ in ph:
        if t == 1 and v <= va < v + fs: return off + va - v
dyn = next(p for p in ph if p[0] == 2)
tags = collections.defaultdict(list)
for i in range(0, dyn[5], 16):
    tag, val = struct.unpack_from("<qQ", d, dyn[2] + i)
    if tag == 0: break
    tags[tag & 0xffffffffffffffff].append(val)
dld = next((p for p in ph if p[0] == 0x61000010), None)
off = (lambda v: dld[2] + v) if dld else v2o
strtab = off((tags.get(5) or tags.get(0x61000035))[0]); strsz = (tags.get(10) or tags.get(0x61000037))[0]
strs = d[strtab:strtab + strsz]
name = lambda o: strs[o:strs.index(b"\0", o)].decode("latin1")
symtab = off((tags.get(6) or tags.get(0x61000039))[0])
symsz = (tags.get(0x6100003f) or [None])[0]
syms = [struct.unpack_from("<IBBHQQ", d, symtab + i * 24) for i in range(symsz // 24)]
slots = {}
for a, b in ((7, 8), (23, 2), (0x6100002f, 0x61000031), (0x61000029, 0x6100002d)):
    if a in tags and b in tags:
        ro = off(tags[a][0])
        for i in range(tags[b][0] // 24):
            r_off, inf, _ = struct.unpack_from("<QQq", d, ro + i * 24)
            si = inf >> 32
            if si and si < len(syms):
                nid = name(syms[si][0]).split("#")[0]
                if nid in NIDS: slots[r_off] = NIDS[nid]
print("GOT slots:", {hex(k): v for k, v in slots.items()})
text = next(p for p in ph if p[0] == 1 and (p[1] & 1))
tv, to, tsz = text[3], text[2], text[5]
code = d[to:to + tsz]
stubs = {}
for m in re.finditer(rb"\xff\x25", code):
    i = m.start(); disp = struct.unpack_from("<i", code, i + 2)[0]
    tgt = tv + i + 6 + disp
    if tgt in slots: stubs[tv + i] = slots[tgt]
direct = collections.defaultdict(list)
for m in re.finditer(rb"\xff\x15", code):
    i = m.start(); disp = struct.unpack_from("<i", code, i + 2)[0]
    tgt = tv + i + 6 + disp
    if tgt in slots: direct[slots[tgt]].append(tv + i)
print("stubs:", {hex(k): v for k, v in stubs.items()})
callers = collections.defaultdict(list)
for m in re.finditer(rb"[\xe8\xe9]", code):
    i = m.start()
    if i + 5 > len(code): continue
    disp = struct.unpack_from("<i", code, i + 1)[0]
    tgt = tv + i + 5 + disp
    if tgt in stubs: callers[stubs[tgt]].append(tv + i)
for fn in sorted(set(list(callers) + list(direct))):
    c = callers.get(fn, []) + direct.get(fn, [])
    print(f"{fn}: {len(c)} call sites", " ".join(hex(x) for x in c[:40]))
