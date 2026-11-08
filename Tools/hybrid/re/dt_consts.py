import collections, re, struct

ELF = r"C:\Users\GAMING PC\Desktop\AstroBot-PC-Port\Analysis\elf\eboot.elf"
d = open(ELF, "rb").read()
phoff, = struct.unpack_from("<Q", d, 0x20); phn, = struct.unpack_from("<H", d, 0x38)
ph = [struct.unpack_from("<IIQQQQQQ", d, phoff + i * 56) for i in range(phn)]
loads = [p for p in ph if p[0] == 1]
text = next(p for p in loads if p[1] & 1)
tv, to, tsz = text[3], text[2], text[5]
code = d[to:to + tsz]

def read_va(va, n):
    for t, f, off, v, _, fs, ms, _ in loads:
        if v <= va and va + n <= v + fs: return d[off + va - v: off + va - v + n]
    return None

cands = {}
for hz in (20, 24, 25, 30, 35, 40, 45, 48, 50, 60):
    cands[struct.pack("<f", 1.0 / hz)] = f"1/{hz} s (float)"
    cands[struct.pack("<d", 1.0 / hz)] = f"1/{hz} s (double)"
clock_regions = [(0x6f80000, 0x7000000, "GetProcessTime cluster"), (0xe00000, 0x10d0000, "ReadTsc cluster"),
                 (0x2a0000, 0x4b0000, "Counter cluster")]
hits = collections.defaultdict(list)
for m in re.finditer(rb"\xc5[\xf8\xfa\xfb\xf9]|\xc4\xe1[\x78\x7a\x7b\x79]|\xf3\x0f|\xf2\x0f", code):
    i = m.start()
    for modrm_at in (i + 2, i + 3):
        if modrm_at + 5 > len(code): continue
        if (code[modrm_at] & 0xc7) != 0x05: continue
        disp = struct.unpack_from("<i", code, modrm_at + 1)[0]
        end = modrm_at + 5
        tgt = tv + end + disp
        for size in (4, 8):
            b = read_va(tgt, size)
            if b and b in cands:
                hits[cands[b]].append(tv + i)
for k, v in sorted(hits.items()):
    near = [hex(x) + "(" + n + ")" for x in v for a, b, n in clock_regions if a <= x < b]
    print(f"{k}: {len(v)} uses; in clock regions: {len(near)} {' '.join(near[:12])}")
