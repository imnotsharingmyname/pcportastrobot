# Stability Mode and optional 24 MiB worker buffers (1.2.9 source add-on)

- Stability Mode is enabled by default for new settings and settings files that do not yet contain the key. An explicit saved `false` remains respected.
- Stability Mode applies conservative Kyty scheduling overrides and enables command-buffer watermark / hang diagnostics. It is not a guaranteed Error 321 or crash fix.
- The optional 24 MiB Command Buffers control is shown directly underneath Stability Mode. It is off by default and disabled in the UI when Stability Mode is off.
- The game executable's five worker command-buffer sizes can be switched between 16 and 24 MiB. The 1.2.9 legacy 8-to-16 MiB and Garlic-pool upgrade runs first and remains intact. Disabling the option restores 16 MiB.
- This 24 MiB setting is experimental and consumes more graphics memory. No in-game stability claim is made.
- The release packager now creates `Logs` alongside its existing `Build`, `tools`, and `licenses` output. Existing 1.2.9 runtime and NVIDIA feature logic is preserved.
