#!/usr/bin/env python3
"""Move libkernel.prx ahead of the graphics libraries in the ELF DT_NEEDED order.

The relinked title imports kernel NIDs such as KernelMprotect from libkernel.prx,
but the hybrid graphics libraries are kyty_gpu.so and export the same kernel NIDs
for the host bridge.  ELF symbol lookup is breadth-first over DT_NEEDED, so the
graphics library, listed first, would otherwise satisfy the kernel import.  The
loader then sends kernel mprotect to Kyty for a range Kyty never mapped.
"""

from __future__ import annotations

import struct
import sys
from pathlib import Path

PT_LOAD = 1
PT_DYNAMIC = 2
DT_NEEDED = 1
DT_STRTAB = 5


def read_cstr(data: bytes, offset: int) -> str:
    end = data.find(b"\0", offset)
    return data[offset:end].decode("utf-8", "replace")


def main() -> int:
    if len(sys.argv) != 2:
        raise SystemExit("usage: reorder-needed.py <elf>")
    path = Path(sys.argv[1])
    data = bytearray(path.read_bytes())

    if data[:4] != b"\x7fELF" or data[4] != 2:
        raise SystemExit(f"{path}: not ELF64")
    e_phoff, = struct.unpack_from("<Q", data, 0x20)
    e_phentsize, e_phnum = struct.unpack_from("<HH", data, 0x36)

    loads: list[tuple[int, int, int, int]] = []
    dynamic: tuple[int, int] | None = None
    for index in range(e_phnum):
        base = e_phoff + index * e_phentsize
        p_type, = struct.unpack_from("<I", data, base)
        p_offset, p_vaddr, _p_paddr, p_filesz = struct.unpack_from("<QQQQ", data, base + 8)
        if p_type == PT_LOAD:
            loads.append((p_vaddr, p_vaddr + p_filesz, p_offset))
        elif p_type == PT_DYNAMIC:
            dynamic = (p_offset, p_filesz)
    if dynamic is None:
        raise SystemExit(f"{path}: no PT_DYNAMIC")

    entries: list[tuple[int, int, int]] = []
    offset, size = dynamic
    for index in range(size // 16):
        base = offset + index * 16
        tag, value = struct.unpack_from("<qQ", data, base)
        entries.append((tag, value, base))

    strtab_vaddr = next(value for tag, value, _ in entries if tag == DT_STRTAB)
    strtab_offset = 0
    for vaddr_start, vaddr_end, file_offset in loads:
        if vaddr_start <= strtab_vaddr < vaddr_end:
            strtab_offset = file_offset + (strtab_vaddr - vaddr_start)
            break
    else:
        raise SystemExit(f"{path}: DT_STRTAB is not in a load segment")

    needed = [(index, read_cstr(data, strtab_offset + value))
              for index, (tag, value, _) in enumerate(entries) if tag == DT_NEEDED]
    names = [name for _, name in needed]
    if "libkernel.prx" not in names:
        raise SystemExit(f"{path}: libkernel.prx is not in DT_NEEDED")
    if names.index("libkernel.prx") < 3:
        print(f"{path}: libkernel.prx is already early ({names.index('libkernel.prx')})")
        return 0

    kernel_index = names.index("libkernel.prx")
    # Keep the converted guest modules first, then the host kernel; the guest libc
    # must stay ahead of the host libc for the title's own allocator.
    guest = [name for name in names if name.startswith("$ORIGIN/")]
    kernel = names[kernel_index]
    rest = [name for name in names if name not in guest and name != kernel]
    order = guest + [kernel] + rest

    for position, (entry_index, _) in enumerate(needed):
        new_name = order[position]
        needle = new_name.encode() + b"\0"
        string_offset = data.find(needle, strtab_offset)
        if string_offset < 0:
            raise SystemExit(f"{path}: no .dynstr entry for {new_name}")
        base = entries[entry_index][2]
        struct.pack_into("<Q", data, base + 8, string_offset - strtab_offset)

    path.write_bytes(data)
    print(f"{path}: DT_NEEDED order: " + ", ".join(order))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
