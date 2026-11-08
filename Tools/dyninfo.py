"""Dump PS4/PS5 ELF dynamic info: needed modules/libraries, imports (NIDs) and exports, relocations summary."""
import sys, struct, json, collections

DT = {0:'NULL',1:'NEEDED',2:'PLTRELSZ',3:'PLTGOT',4:'HASH',5:'STRTAB',6:'SYMTAB',7:'RELA',8:'RELASZ',9:'RELAENT',10:'STRSZ',11:'SYMENT',
      12:'INIT',13:'FINI',14:'SONAME',15:'RPATH',16:'SYMBOLIC',20:'PLTREL',23:'JMPREL',24:'BIND_NOW',25:'INIT_ARRAY',26:'FINI_ARRAY',
      27:'INIT_ARRAYSZ',28:'FINI_ARRAYSZ',30:'FLAGS',32:'PREINIT_ARRAY',33:'PREINIT_ARRAYSZ',0x6ffffff9:'RELACOUNT',0x6ffffffb:'FLAGS_1',
      0x61000005:'SCE_FINGERPRINT',0x61000007:'SCE_ORIGINAL_FILENAME',0x6100000d:'SCE_MODULE_INFO',0x61000041:'SCE_ORIGINAL_FILENAME_PPR',0x61000043:'SCE_MODULE_INFO_PPR',0x61000045:'SCE_NEEDED_MODULE_PPR',0x61000047:'SCE_EXPORT_LIB_PPR',0x61000049:'SCE_IMPORT_LIB_PPR',0x6100000f:'SCE_NEEDED_MODULE',
      0x61000011:'SCE_MODULE_ATTR',0x61000013:'SCE_EXPORT_LIB',0x61000015:'SCE_IMPORT_LIB',0x61000017:'SCE_EXPORT_LIB_ATTR',
      0x61000019:'SCE_IMPORT_LIB_ATTR',0x6100001b:'SCE_STUB_MODULE_NAME',0x6100001d:'SCE_STUB_MODULE_VERSION',0x6100001f:'SCE_STUB_LIBRARY_NAME',
      0x61000021:'SCE_STUB_LIBRARY_VERSION',0x61000025:'SCE_HASH',0x61000027:'SCE_PLTGOT',0x61000029:'SCE_JMPREL',0x6100002b:'SCE_PLTREL',
      0x6100002d:'SCE_PLTRELSZ',0x6100002f:'SCE_RELA',0x61000031:'SCE_RELASZ',0x61000033:'SCE_RELAENT',0x61000035:'SCE_STRTAB',
      0x61000037:'SCE_STRSZ',0x61000039:'SCE_SYMTAB',0x6100003b:'SCE_SYMENT',0x6100003d:'SCE_HASHSZ',0x6100003f:'SCE_SYMTABSZ',
      0x6ffffff0:'VERSYM',0x6ffffffe:'VERNEED',0x6fffffff:'VERNEEDNUM'}
R = {0:'NONE',1:'64',2:'PC32',6:'GLOB_DAT',7:'JUMP_SLOT',8:'RELATIVE',16:'DTPMOD64',17:'DTPOFF64',18:'TPOFF64'}

def load(path):
    d = open(path,'rb').read()
    phoff, = struct.unpack_from('<Q', d, 0x20); phn, = struct.unpack_from('<H', d, 0x38)
    ph = [struct.unpack_from('<IIQQQQQQ', d, phoff+i*56) for i in range(phn)]
    return d, ph

def v2o(ph, va):
    for t,f,off,v,_,fs,ms,_ in ph:
        if t == 1 and v <= va < v+fs: return off + va - v
    return None

def main(path, as_json=None):
    d, ph = load(path)
    dyn = next(p for p in ph if p[0] == 2)
    ents = []
    for i in range(0, dyn[5], 16):
        tag, val = struct.unpack_from('<qQ', d, dyn[2]+i)
        if tag == 0: break
        ents.append((tag & 0xffffffffffffffff, val))
    tags = collections.defaultdict(list)
    for t,v in ents: tags[t].append(v)
    # dynlib data segment: PS4 has PT_SCE_DYNLIBDATA (offsets relative to it); PS5 uses plain vaddrs
    dld = next((p for p in ph if p[0] == 0x61000010), None)
    def off(v): return dld[2]+v if dld else v2o(ph, v)
    strtab_v = (tags.get(5) or tags.get(0x61000035))[0]; strsz = (tags.get(10) or tags.get(0x61000037))[0]
    so = off(strtab_v); strs = d[so:so+strsz]
    def s(o): return strs[o:strs.index(b'\0', o)].decode('latin1')
    info = {'file': path, 'dt': [], 'needed': [], 'modules': [], 'import_libs': [], 'export_libs': []}
    for t,v in ents:
        info['dt'].append(f'{DT.get(t, hex(t))}={v:#x}')
    for v in tags.get(1, []): info['needed'].append(s(v & 0xffffffff))
    for t in (0x6100000d, 0x6100000f, 0x61000043, 0x61000045):
        for v in tags.get(t, []):
            info['modules'].append({'kind': 'self' if t in (0x6100000d, 0x61000043) else 'needed', 'name': s(v & 0xffffffff), 'ver': f'{(v>>32)&0xff}.{(v>>40)&0xff}', 'id': (v>>48)})
    for t,k in ((0x61000015,'import_libs'),(0x61000013,'export_libs'),(0x61000049,'import_libs'),(0x61000047,'export_libs')):
        for v in tags.get(t, []):
            info[k].append({'name': s(v & 0xffffffff), 'ver': (v>>32)&0xffff, 'id': v>>48})
    symtab_v = (tags.get(6) or tags.get(0x61000039))[0]
    symsz = (tags.get(0x6100003f) or [None])[0]
    hashv = (tags.get(4) or tags.get(0x61000025) or [None])[0]
    if symsz is None and hashv is not None:
        ho = off(hashv); nb, nc = struct.unpack_from('<II', d, ho); symsz = nc*24
    so2 = off(symtab_v)
    syms = []
    for i in range(symsz//24):
        nm, inf, oth, shn, val, sz = struct.unpack_from('<IBBHQQ', d, so2+i*24)
        syms.append((s(nm), inf>>4, inf&0xf, shn, val))
    imports = [x for x in syms if x[3] == 0 and x[0]]
    exports = [x for x in syms if x[3] != 0 and x[0]]
    info['n_syms'] = len(syms); info['n_imports'] = len(imports); info['n_exports'] = len(exports)
    # relocations
    rel = collections.Counter()
    for a,b in ((7,8),(23,2),(0x6100002f,0x61000031),(0x61000029,0x6100002d)):
        if a in tags and b in tags:
            ro = off(tags[a][0])
            for i in range(tags[b][0]//24):
                _, inf, _ = struct.unpack_from('<QQq', d, ro+i*24)
                rel[R.get(inf & 0xffffffff, hex(inf & 0xffffffff))] += 1
    info['relocs'] = dict(rel)
    # group imports by library id (NID#lib#mod)
    bylib = collections.Counter()
    for n,*_ in imports:
        parts = n.split('#'); bylib[parts[1] if len(parts) == 3 else '?'] += 1
    libid = {}
    for l in info['import_libs']:
        libid[l['id']] = l['name']
    enc = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+-'
    def dec(x):
        r = 0
        for ch in x: r = r*64 + enc.index(ch)
        return r
    info['imports_by_lib'] = {libid.get(dec(k), k): c for k, c in bylib.most_common()} if bylib else {}
    info['import_nids'] = [n for n,*_ in imports]
    info['export_nids'] = [(n, hex(v)) for n,_,_,_,v in exports]
    return info

if __name__ == '__main__':
    info = main(sys.argv[1])
    if len(sys.argv) > 2:
        json.dump(info, open(sys.argv[2], 'w'), indent=1)
    for k in ('needed','modules','import_libs','export_libs','n_syms','n_imports','n_exports','relocs','imports_by_lib'):
        print(k, '=', json.dumps(info[k]) if not isinstance(info[k], list) or len(info[k]) < 400 else len(info[k]))
    print('dt:', ' '.join(info['dt']))
