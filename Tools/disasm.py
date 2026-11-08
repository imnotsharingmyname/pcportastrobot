"""Disassemble eboot code by virtual address with WinLibs objdump.

Usage: python -I Tools/disasm.py <va> [bytes_before=0x40] [bytes_after=0x80] [elf=Build/elf-input/eboot.elf]
The first lines can be mis-decoded when <va> - bytes_before is not an instruction boundary.
"""
import os
import struct
import subprocess
import sys
import tempfile

root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
va = int(sys.argv[1], 0)
before = int(sys.argv[2], 0) if len(sys.argv) > 2 else 0x40
after = int(sys.argv[3], 0) if len(sys.argv) > 3 else 0x80
elf = sys.argv[4] if len(sys.argv) > 4 else os.path.join(root, 'Build', 'elf-input', 'eboot.elf')

data = open(elf, 'rb').read()
phoff, = struct.unpack_from('<Q', data, 0x20)
phnum, = struct.unpack_from('<H', data, 0x38)
for i in range(phnum):
    p_type, p_flags, p_offset, p_vaddr, _, p_filesz, _, _ = struct.unpack_from('<IIQQQQQQ', data, phoff + i * 56)
    if p_type == 1 and p_vaddr <= va < p_vaddr + p_filesz:
        start = max(p_vaddr, va - before)
        end = min(p_vaddr + p_filesz, va + after)
        blob = data[p_offset + start - p_vaddr:p_offset + end - p_vaddr]
        break
else:
    sys.exit(f'va {va:#x} is not in a file-backed PT_LOAD')

with tempfile.NamedTemporaryFile(suffix='.bin', delete=False) as handle:
    handle.write(blob)
objdump = os.path.join(root, 'Tools', 'winlibs', 'mingw64', 'bin', 'objdump.exe')
result = subprocess.run([objdump, '-D', '-b', 'binary', '-m', 'i386:x86-64', '-M', 'intel', '--no-show-raw-insn',
                         f'--adjust-vma={start:#x}', handle.name], capture_output=True, text=True)
os.unlink(handle.name)
for line in result.stdout.splitlines():
    if line.startswith(' ') and ':' in line:
        marker = '  <<<' if line.strip().startswith(f'{va:x}:') else ''
        print(line.strip() + marker)
