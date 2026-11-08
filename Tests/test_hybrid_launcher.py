"""Exercise the actual batch launcher with a harmless environment-printing executable."""
import os
import pathlib
import shutil
import subprocess
import tempfile

root = pathlib.Path(__file__).resolve().parents[1]
work = pathlib.Path(tempfile.mkdtemp(prefix="launcher-smoke-", dir=root / "Build"))
run = work / "Build" / "RunHybrid"
run.mkdir(parents=True)
shutil.copy2(root / "Run-AstroHybrid.bat", work / "Run-AstroHybrid.bat")
shutil.copy2(root / "Build" / "RunHybrid" / "u59-preset.cmd", run / "u59-preset.cmd")
source = work / "env.c"
source.write_text(r'''
#include <stdio.h>
#include <stdlib.h>
#include <windows.h>
static const char* value(const char* key) {
    const char* v = getenv(key);
    return v ? v : "<unset>";
}
int main(void) {
    printf("priority=%s\nspin=%s\ncold=%s\nprocess=%lu\n",
           value("KYTY_DRAW_PREP_PRIORITY"), value("KYTY_DRAW_PREP_SPIN_US"),
           value("KYTY_DRAW_PREP_COLD_SPIN_US"),
           (unsigned long)GetPriorityClass(GetCurrentProcess()));
    return 0;
}
''', encoding="ascii")
compiler = root / "Tools" / "winlibs" / "mingw64" / "bin" / "gcc.exe"
env = os.environ.copy()
env["PATH"] = str(compiler.parent) + os.pathsep + env["PATH"]
subprocess.run([str(compiler), str(source), "-o", str(run / "AstroBot.exe")],
               env=env, check=True)
for key in ("KYTY_DRAW_PREP_PRIORITY", "KYTY_DRAW_PREP_SPIN_US", "KYTY_DRAW_PREP_COLD_SPIN_US",
            "ASTROHYBRID_DRAW_PREP_TUNING", "ASTROHYBRID_PRIORITY", "ASTROHYBRID_EXE"):
    env.pop(key, None)
cases = [
    ("default", {}, ("<unset>", "<unset>", "<unset>")),
    ("explicit-off", {"ASTROHYBRID_DRAW_PREP_TUNING": "0"},
     ("<unset>", "<unset>", "<unset>")),
    ("enabled", {"ASTROHYBRID_DRAW_PREP_TUNING": "1"}, ("1", "10", "5")),
    ("overrides", {"ASTROHYBRID_DRAW_PREP_TUNING": "1", "KYTY_DRAW_PREP_PRIORITY": "2",
                   "KYTY_DRAW_PREP_SPIN_US": "25", "KYTY_DRAW_PREP_COLD_SPIN_US": "15"},
     ("2", "25", "15")),
]
for name, values, expected in cases:
    result = subprocess.run(["cmd.exe", "/d", "/c", "Run-AstroHybrid.bat"],
                            cwd=work, env=env | values, capture_output=True, text=True)
    assert result.returncode == 0, (name, result.stdout, result.stderr)
    text = (work / "Logs" / "hybrid-run.out.log").read_text()
    actual = dict(line.split("=", 1) for line in text.splitlines() if "=" in line)
    assert tuple(actual[k] for k in ("priority", "spin", "cold")) == expected, (name, actual)
    assert actual["process"] == "32768", (name, actual)
    print(name, "PASS", actual)
print("4/4 launcher cases passed; isolated artifacts:", work)
