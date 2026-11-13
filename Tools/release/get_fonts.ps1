# Downloads the openly licensed Noto fonts (SIL Open Font License) that AnyPS5's libSceFont accepts in place
# of the console's system fonts (Tools\AnyPS5\docs\user\USAGE.md, "System fonts"). Astro Bot opens the
# Japanese-region sets in several weights, so the CJK collections are needed even for English text.
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File Tools\release\get_fonts.ps1 [-Out E:\AstroHybrid-Work\fonts\noto]
param([string]$Out = 'E:\AstroHybrid-Work\fonts\noto')
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
New-Item -ItemType Directory -Force $Out | Out-Null
$noto = 'https://raw.githubusercontent.com/notofonts/notofonts.github.io/main/fonts'
$files = @{}
foreach ($w in 'Light', 'Regular', 'Medium', 'Bold', 'LightItalic', 'Italic', 'MediumItalic', 'BoldItalic') { $files["NotoSans-$w.ttf"] = "$noto/NotoSans/full/ttf/NotoSans-$w.ttf" }
foreach ($family in 'NotoSansThai', 'NotoSansArabic') {
    foreach ($w in 'Light', 'Regular', 'Medium', 'Bold') { $files["$family-$w.ttf"] = "$noto/$family/full/ttf/$family-$w.ttf" }
}
foreach ($w in 'Light', 'Regular', 'Medium', 'Bold') { $files["NotoSansCJK-$w.ttc"] = "https://raw.githubusercontent.com/notofonts/noto-cjk/main/Sans/OTC/NotoSansCJK-$w.ttc" }
$files['OFL-NotoSans.txt'] = 'https://raw.githubusercontent.com/notofonts/latin-greek-cyrillic/main/OFL.txt'
$files['OFL-NotoSansCJK.txt'] = 'https://raw.githubusercontent.com/notofonts/noto-cjk/main/Sans/LICENSE'
foreach ($name in $files.Keys) {
    $target = Join-Path $Out $name
    if (-not (Test-Path $target)) { Invoke-WebRequest -Uri $files[$name] -OutFile $target -UseBasicParsing }
}
"{0} files in {1}" -f (Get-ChildItem $Out -File).Count, $Out
