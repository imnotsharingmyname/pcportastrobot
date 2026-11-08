"""Parse a PS4/PS5 SELF container: header, segment entries, embedded ELF header, entropy per segment."""
import sys, struct, math, collections

def entropy(b):
    if not b: return 0.0
    c = collections.Counter(b); n = len(b)
    return -sum(v/n*math.log2(v/n) for v in c.values())

def main(path):
    d = open(path, 'rb').read()
    magic = d[:4].hex()
    ver, mode, endian, attr = d[4:8]
    key_type, hdr_size, meta_size = struct.unpack_from('<IHH', d, 8)
    file_size, = struct.unpack_from('<Q', d, 16)
    n_ent, flags = struct.unpack_from('<HH', d, 24)
    print(f'{path}\n magic={magic} ver={ver} mode={mode} endian={endian} attr={attr:#x} key_type={key_type:#x} hdr={hdr_size:#x} meta={meta_size:#x} file_size={file_size:#x} (actual {len(d):#x}) entries={n_ent} flags={flags:#x}')
    for i in range(n_ent):
        props, off, fsz, msz = struct.unpack_from('<QQQQ', d, 32 + i*32)
        flg = []
        if props & 1: flg.append('ordered')
        if props & 2: flg.append('ENCRYPTED')
        if props & 4: flg.append('signed')
        if props & 8: flg.append('COMPRESSED')
        if props & 0x800: flg.append('blocked')
        seg = (props >> 20) & 0xffff
        e = entropy(d[off:off+min(fsz, 1<<20)]) if off + 16 <= len(d) else -1
        print(f'  [{i:2}] props={props:#012x} seg={seg:3} off={off:#010x} filesz={fsz:#010x} memsz={msz:#010x} H={e:4.2f} {",".join(flg)}')
    eo = 32 + n_ent*32
    print(' ELF @', hex(eo), d[eo:eo+4])
    if d[eo:eo+4] == b'\x7fELF':
        (e_type, e_machine, e_version, e_entry, e_phoff, e_shoff, e_flags, e_ehsize, e_phentsize, e_phnum, e_shentsize, e_shnum, e_shstrndx) = struct.unpack_from('<HHIQQQIHHHHHH', d, eo+16)
        print(f'  e_ident={d[eo:eo+16].hex()} type={e_type:#x} machine={e_machine} entry={e_entry:#x} phnum={e_phnum} shnum={e_shnum} osabi={d[eo+7]}')
        PT = {1:'LOAD',2:'DYNAMIC',3:'INTERP',4:'NOTE',6:'PHDR',7:'TLS',0x61000000:'SCE_RELA',0x61000001:'SCE_PROCPARAM',0x61000002:'SCE_MODULE_PARAM',0x61000010:'SCE_DYNLIBDATA',0x6474e550:'GNU_EH_FRAME',0x6474e552:'GNU_RELRO',0x6fffff00:'SCE_COMMENT',0x6fffff01:'SCE_VERSION'}
        for i in range(e_phnum):
            p_type, p_flags, p_off, p_va, p_pa, p_fsz, p_msz, p_al = struct.unpack_from('<IIQQQQQQ', d, eo + e_phoff + i*e_phentsize)
            print(f'   ph[{i:2}] {PT.get(p_type, hex(p_type)):16} flags={p_flags:#x} off={p_off:#010x} va={p_va:#012x} filesz={p_fsz:#010x} memsz={p_msz:#010x} align={p_al:#x}')

for p in sys.argv[1:]:
    main(p); print()
