"""Rebuild a plain ELF from a PS4/PS5 SELF whose segments are NOT encrypted or compressed.

Refuses to run on encrypted/compressed segments (those need console keys, which this project does not use).
"""
import sys, struct

def self2elf(src, dst):
    d = open(src, 'rb').read()
    if d[:4] not in (b'\x54\x14\xf5\xee', b'\x4f\x15\x3d\x1d'):
        raise SystemExit(f'{src}: not a SELF')
    n_ent, = struct.unpack_from('<H', d, 24)
    ents = [struct.unpack_from('<QQQQ', d, 32 + i*32) for i in range(n_ent)]
    eo = 32 + n_ent*32
    assert d[eo:eo+4] == b'\x7fELF'
    e_phoff, = struct.unpack_from('<Q', d, eo+0x20)
    e_phentsize, e_phnum = struct.unpack_from('<HH', d, eo+0x36)
    phdrs = [struct.unpack_from('<IIQQQQQQ', d, eo + e_phoff + i*e_phentsize) for i in range(e_phnum)]
    end = max(p[2] + p[5] for p in phdrs)
    out = bytearray(end)
    hdr_len = e_phoff + e_phnum*e_phentsize
    out[:hdr_len] = d[eo:eo+hdr_len]
    # no section headers are shipped in the SELF
    struct.pack_into('<QHHH', out, 0x28, 0, 0, 0, 0) if False else None
    struct.pack_into('<Q', out, 0x28, 0)        # e_shoff
    struct.pack_into('<HHH', out, 0x3a, 0, 0, 0)  # e_shentsize, e_shnum, e_shstrndx
    placed = set()
    for props, off, fsz, msz in ents:
        if not props & 0x800:  # non-blocked entries are digest/extent tables
            continue
        if props & 0x2 or props & 0x8:
            raise SystemExit(f'{src}: segment is encrypted/compressed (props={props:#x}); cannot convert without keys')
        idx = (props >> 20) & 0xffff
        p = phdrs[idx]
        if fsz != p[5]:
            raise SystemExit(f'{src}: size mismatch seg {idx}: self {fsz:#x} vs phdr {p[5]:#x}')
        out[p[2]:p[2]+fsz] = d[off:off+fsz]
        placed.add(idx)
    # phdrs that live inside another segment's file range (EH_FRAME, RELRO, TLS, ...) are covered implicitly.
    # Segments with no backing entry (SCE_VERSION etc.) are appended after the SELF data in the file.
    tail = n_ent and max(e[1] + e[2] for e in ents)
    for i, p in enumerate(phdrs):
        if i in placed or p[5] == 0: continue
        covered = any(q[2] <= p[2] and p[2] + p[5] <= q[2] + q[5] for j, q in enumerate(phdrs) if j in placed)
        if not covered:
            # SCE_VERSION is stored after the SELF segments (unsigned trailer)
            if p[0] == 0x6fffff01 and len(d) > tail:
                blob = d[tail:tail+p[5]]
                out[p[2]:p[2]+len(blob)] = blob
                print(f'  ph[{i}] SCE_VERSION copied from SELF trailer ({len(blob):#x} bytes)')
            else:
                print(f'  ph[{i}] type={p[0]:#x} off={p[2]:#x} size={p[5]:#x} has no SELF backing (left zeroed)')
    open(dst, 'wb').write(out)
    print(f'{src} -> {dst} ({len(out):#x} bytes, {len(placed)} segments)')

if __name__ == '__main__':
    import os
    outdir = sys.argv[1]
    os.makedirs(outdir, exist_ok=True)
    for s in sys.argv[2:]:
        base = os.path.basename(s).rsplit('.', 1)[0]
        self2elf(s, os.path.join(outdir, base + '.elf'))
