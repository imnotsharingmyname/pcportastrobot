@echo off
rem Recreated 2026-11-12 from Tools\hybrid\build_gpu_v3.cmd (the original was deleted with E:\AstroHybrid-Work).
rem PGO build of kyty_emulator from the hybrid worktree; the DLL is then linked by tools\hybrid\build_kyty_gpu.py.
rem Usage: build-kyty-pgo.cmd [configure|build]
setlocal
set "ROOT=C:\Users\GAMING PC\Desktop\AstroBot-PC-Port"
set "SRC=C:\kyty-src19"
set "OUT=E:\AstroHybrid-Work\build\kyty-pgo"
set "DEPS=%ROOT%\Tools\KytyPS5\_Build\windows\_deps"
set "LLVM=E:/AstroHybrid-Work/tools/LLVM-22.1.3/bin"
call "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvars64.bat" >nul || exit /b 10
set "PATH=E:\AstroHybrid-Work\tools\LLVM-22.1.3\bin;C:\VulkanSDK\1.4.363.0\Bin;%PATH%"
if "%1"=="build" goto build
cmake -S "%SRC%" -B "%OUT%" -G Ninja ^
  -DCMAKE_BUILD_TYPE=Release ^
  -DCMAKE_C_COMPILER="%LLVM%/clang-cl.exe" -DCMAKE_CXX_COMPILER="%LLVM%/clang-cl.exe" ^
  -DCMAKE_LINKER="%LLVM%/lld-link.exe" -DCMAKE_AR="%LLVM%/llvm-lib.exe" ^
  -DKYTY_BUILD_LAUNCHER=OFF -DKYTY_EMULATOR_IPO=ON -DKYTY_CP_DIAGNOSTICS=OFF ^
  -DKYTY_PGO_USE=C:/kyty-src19/tools/pgo/u59-int21-1.profdata ^
  -DKYTY_BUILD_ORIGIN=Source -DKYTY_MARCH= ^
  -DFETCHCONTENT_FULLY_DISCONNECTED=ON ^
  -DFETCHCONTENT_SOURCE_DIR_OPUS="%DEPS%\opus-src" ^
  -DFETCHCONTENT_SOURCE_DIR_OPENXR="%DEPS%\openxr-src" ^
  -DFETCHCONTENT_SOURCE_DIR_XBYAK="%DEPS%\xbyak-src" ^
  -DFETCHCONTENT_SOURCE_DIR_ZYDIS="%DEPS%\zydis-src" ^
  -DFETCHCONTENT_SOURCE_DIR_ZSTD="%DEPS%\zstd-src" ^
  -DFETCHCONTENT_SOURCE_DIR_ZARCHIVE_SOURCE="%DEPS%\zarchive_source-src" || exit /b 11
if "%1"=="configure" exit /b 0
:build
cmake --build "%OUT%" --target kyty_emulator --parallel 10 || exit /b 12
exit /b 0
