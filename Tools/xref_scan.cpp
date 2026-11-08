// Finds RIP-relative references to target addresses in the executable PT_LOAD of an ELF.
// For every byte position it treats the next 4 bytes as a rel32 displacement and reports the
// positions where (end of instruction) + rel32 equals a target, for instructions that end right
// after the displacement or after a 1-, 2- or 4-byte immediate. Candidates still need disassembly.
// Usage: xref_scan <elf> <target> [<target>...]
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <fstream>
#include <iterator>
#include <vector>

int main(int argc, char** argv) {
    if (argc < 3) {
        std::fprintf(stderr, "usage: xref_scan <elf> <target>...\n");
        return 2;
    }
    std::ifstream file(argv[1], std::ios::binary);
    std::vector<unsigned char> data((std::istreambuf_iterator<char>(file)), std::istreambuf_iterator<char>());
    std::vector<std::uint64_t> targets;
    for (int i = 2; i < argc; ++i) targets.push_back(std::strtoull(argv[i], nullptr, 0));

    std::uint64_t phoff = 0;
    std::uint16_t phnum = 0;
    std::memcpy(&phoff, &data[0x20], 8);
    std::memcpy(&phnum, &data[0x38], 2);
    for (std::uint16_t i = 0; i < phnum; ++i) {
        const unsigned char* ph = &data[phoff + i * 56u];
        std::uint32_t type = 0, flags = 0;
        std::uint64_t offset = 0, vaddr = 0, filesz = 0;
        std::memcpy(&type, ph, 4);
        std::memcpy(&flags, ph + 4, 4);
        std::memcpy(&offset, ph + 8, 8);
        std::memcpy(&vaddr, ph + 16, 8);
        std::memcpy(&filesz, ph + 32, 8);
        if (type != 1 || (flags & 1u) == 0) continue;
        for (std::uint64_t pos = 0; pos + 4 <= filesz; ++pos) {
            std::int32_t displacement = 0;
            std::memcpy(&displacement, &data[offset + pos], 4);
            for (std::uint64_t tail : {0u, 1u, 2u, 4u}) {
                const std::uint64_t end = vaddr + pos + 4 + tail;
                const std::uint64_t resolved = end + static_cast<std::int64_t>(displacement);
                for (std::uint64_t target : targets) {
                    if (resolved == target) std::printf("%#llx disp_at=%#llx imm_bytes=%llu target=%#llx\n", static_cast<unsigned long long>(vaddr + pos), static_cast<unsigned long long>(vaddr + pos), static_cast<unsigned long long>(tail), static_cast<unsigned long long>(target));
                }
            }
        }
    }
    return 0;
}
