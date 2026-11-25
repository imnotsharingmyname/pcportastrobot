# Using ZArchive game data

Use your own legally obtained, decrypted game dump. ZArchive (`.zar`) compresses and indexes files; it does not convert the game's resource formats.

## Source selection and quick validation

Use **Browse .zar...** to select any standalone archive, or **Browse folder...** for a dump folder. A standalone archive does not need to be renamed to `app0.zar` and does not need external sidecar files.

The pre-install quick check reads the complete archive index, rejects an `app0/` wrapper, checks the required Astro Bot layout, and reads/decompresses `eboot.bin`, original `sce_module/*.prx`, and `sce_sys` entries. The wizard shows genuine byte progress and enables Install only after these reads succeed. **It does not read or certify optional compressed game-data blocks under `data/`**; damage limited to those blocks can still be discovered when the runtime reads them later.

Setup extracts those required startup entries into app-owned `Build/ZarSource` for the relinker and writes a sibling `Build/RunHybrid/app0.zar` link or copy. It never unpacks optional game data, modifies the source archive, or requires an empty `data` directory. Repair/Rebuild repeats materialization from the saved standalone path; Uninstall removes only generated files and leaves the source archive intact.

## Dump folder and runtime layout

A dump folder with a sibling archive instead of loose `data` contains:

```text
dump/
  eboot.bin
  sce_module/
  sce_sys/
  app0.zar
```

For this dump-folder form, keep `eboot.bin`, the original `sce_module` modules, and `sce_sys` available as ordinary files: setup needs them before the game runtime starts. Other loose files, such as `args.txt` or `param.sfx`, may also remain in this folder. Do not create an empty `data` directory just to satisfy setup. Standalone archives instead get those files materialized automatically as described above.

The runtime layout is different:

```text
Build/RunHybrid/
  AstroBot.exe
  libs/
    libc.prx
    libkernel.prx
  app0.zar
  app0/
    eboot.bin
    sce_module/
    sce_sys/
    args.txt
```

Setup places the archive **beside** `app0`, not inside it. Loose entries take precedence over archived entries at the same guest path. Removing or reinstalling the game removes the runtime's links/copies, not the original dump.

## Paths inside the archive

Pack the **contents of the app0/dump root**, not its parent directory:

```text
Correct archive entries                 Incorrect extra wrapper
-----------------------                 -----------------------
data/prein/...                          app0/data/prein/...
sce_sys/...                             app0/sce_sys/...
param.sfx                               app0/param.sfx
```

The runtime mounts this archive at `/app0`: `/app0/data/example.bin` looks up `data/example.bin`. There must not be an extra `app0/` prefix inside the archive. An archive made from the `data` directory alone is also wrong if it omits the `data/` component from its entries.

ZArchive's tool takes the input directory and output archive as positional arguments. For a staging directory containing the wanted root-relative entries:

```bat
zarchive.exe "D:\ArchiveInput" "D:\Dump\app0.zar"
```

Put the output outside the input directory so that it is not included in its own archive. Keep the original external executable/modules/metadata available to setup. For an existing incorrectly wrapped archive, extract it to a separate staging directory and pack the contents of its inner `app0` directory; do not rename resource files.

## `.odx`, `.odxb`, and binary mode

The retail dump contains binary `.odxb` resources. The standard startup arguments include `-odxBinary`. Existing dump arguments are preserved rather than edited in place.

The compatibility patch resolves an archive request for `data/.../example.odx` as follows:

1. Use an exact `.odx` entry if one exists.
2. Otherwise try the same path ending in `.odxb`, accepting only a regular file.
3. If neither exists, return the ordinary missing-file result.

This is a **filename fallback**, not a text-to-binary conversion. It does not rename archived entries or modify their bytes. It is limited to read-only archive lookup under `/app0`; unrelated extensions and write destinations are not rewritten. Directory listings retain the actual names.

A message such as `odx not found [...]` alone does not prove which filename the engine passed to the filesystem. Check the archive contents and the runtime's file-open behavior when diagnosing another missing resource.

## Runtime update required

Issue [#39](https://github.com/imnotsharingmyname/pcportastrobot/issues/39) addressed setup accepting `app0.zar` and placing it correctly. That alone does not supply libc stdio support. An older runtime can still throw:

```text
reading /app0/param.sfx in app0.zar not implemented
```

Build the canonical `Source/anyps5` patch series, including the ZAR lookup and libc stdio patches, and deploy the rebuilt runtime libraries. See [Source/README.md](../Source/README.md). Running Repair only rebuilds game files and links; it does **not** replace the runtime libraries shipped with an older download.

The stdio backend is read-only and streams archive data using the existing ZArchive reader. It must not treat the native placeholder descriptor as the content source. Native-only operations that cannot handle archives remain explicit errors rather than silently reading empty data.

### Remaining libc limits

This patch does not claim complete libc compatibility for every archive operation:

- Native-only libc path consumers such as `stat`, `access` and `opendir` still refuse archive-only entries; `opendir` also refuses directories containing archive entries rather than returning an incomplete merged listing. The archive-aware libkernel file APIs are separate.
- Archive streams provide one byte of pushback and are genuinely unbuffered. `setvbuf(..., _IONBF, ...)` and `setbuf(stream, nullptr)` are supported; requesting full or line buffering remains an explicit unsupported-operation error.
- Byte-oriented scanning supports common integer/float, character/string, scanset, width, suppression and `%n` conversions. Formatted wide-character, positional, allocation and unknown scan directives remain explicit errors. Windows long-double scan conversions are also refused because the host and guest ABIs differ.
- Floating scans require a dot-decimal numeric locale. A non-dot host `LC_NUMERIC` is refused before input is consumed, rather than assigning an incorrectly parsed value.
- Numeric scan tokens are bounded to 4096 bytes; larger tokens are rejected explicitly after that bounded prefix has been consumed. String and scanset fields are consumed incrementally, rather than extracting the entire file.

These limits are separate from archive path layout and ZArchive format compatibility. The regression tests cover the reported failures, but a game build using another unsupported operation may still require a further runtime patch.

## Verification checklist

- Setup works without an empty `data` directory.
- Archive entries contain `data/...`, not `app0/data/...` or just `prein/...`.
- The runtime discovers sibling `app0.zar`.
- Archived `param.sfx` can be read through libc stdio.
- An exact `.odx` entry wins; the missing-name fallback reads the real `.odxb` entry.
- Loose-file overrides and normal extracted dumps still work.
- Corrupt compressed data reports an error, not successful EOF.
- On Windows, load the room and respawn effect that previously failed, and inspect `hybrid-run.err.log`.

Portable regression tests do not establish that the entire game runs correctly. A Windows runtime build and an actual game smoke test are required for final acceptance.
