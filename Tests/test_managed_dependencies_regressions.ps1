[CmdletBinding()]
param(
    [string]$AppDir = ''
)

$ErrorActionPreference = 'Stop'
# Windows PowerShell 5.1 leaves $PSScriptRoot empty in param() defaults, so the default is set here.
if (-not $AppDir) { $AppDir = Join-Path (Split-Path -Parent $PSScriptRoot) 'AstroHybrid.App\bin\Release' }
$AppDir = (Resolve-Path -LiteralPath $AppDir).Path
$audit = Join-Path $PSScriptRoot 'test_managed_dependencies.ps1'
$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ("AstroHybrid-managed-deps-" + [Guid]::NewGuid().ToString('N'))
$script:failures = 0

function Assert-Result([bool]$Condition, [string]$Name, [string]$Details = '') {
    if ($Condition) {
        "PASS: $Name"
    }
    else {
        $script:failures++
        "FAIL: $Name$(if ($Details) { [Environment]::NewLine + $Details })"
    }
}

function Copy-AppFixture([string]$Name) {
    $destination = Join-Path $tempRoot $Name
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    Get-ChildItem -LiteralPath $AppDir -File | Copy-Item -Destination $destination
    return $destination
}

function Invoke-Audit([string]$Fixture) {
    $previousPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $output = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $audit -AppDir $Fixture 2>&1
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousPreference
    }
    return [PSCustomObject]@{
        ExitCode = $exitCode
        Text = ($output | Out-String)
    }
}

try {
    New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null

    $clean = Copy-AppFixture 'clean'
    $result = Invoke-Audit $clean
    Assert-Result ($result.ExitCode -eq 0) 'Clean Release output passes' $result.Text

    $multipleRedirects = Copy-AppFixture 'multiple-redirects'
    $configPath = Join-Path $multipleRedirects 'AstroHybrid.exe.config'
    [xml]$config = Get-Content -LiteralPath $configPath
    $namespace = 'urn:schemas-microsoft-com:asm.v1'
    $namespaces = New-Object Xml.XmlNamespaceManager($config.NameTable)
    $namespaces.AddNamespace('asm', $namespace)
    $dependent = $config.SelectSingleNode("//asm:dependentAssembly[asm:assemblyIdentity[@name='System.Memory']]", $namespaces)
    foreach ($oldRedirect in @($dependent.SelectNodes('asm:bindingRedirect', $namespaces))) {
        [void]$dependent.RemoveChild($oldRedirect)
    }
    foreach ($range in '0.0.0.0-4.0.1.1', '4.0.1.2-4.0.2.0') {
        $redirect = $config.CreateElement('bindingRedirect', $namespace)
        $redirect.SetAttribute('oldVersion', $range)
        $redirect.SetAttribute('newVersion', '4.0.2.0')
        [void]$dependent.AppendChild($redirect)
    }
    $config.Save($configPath)
    $result = Invoke-Audit $multipleRedirects
    Assert-Result ($result.ExitCode -eq 0) 'Second bindingRedirect in dependentAssembly is honored' $result.Text

    $wrongToken = Copy-AppFixture 'wrong-token'
    $sourcePath = Join-Path $tempRoot 'UnsignedMemory.cs'
    @'
using System.Reflection;
[assembly: AssemblyVersion("4.0.2.0")]
public sealed class UnsignedMemoryFixture { }
'@ | Set-Content -LiteralPath $sourcePath -Encoding ASCII
    $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
    & $csc /nologo /target:library "/out:$(Join-Path $wrongToken 'System.Memory.dll')" $sourcePath
    if ($LASTEXITCODE -ne 0) { throw 'Could not compile the unsigned dependency fixture.' }
    $result = Invoke-Audit $wrongToken
    Assert-Result ($result.ExitCode -ne 0) 'An installed public key token mismatch is rejected' $result.Text
    Assert-Result ($result.Text -match 'public key token') 'Token mismatch diagnostics identify the identity problem' $result.Text

    $badImage = Copy-AppFixture 'bad-image'
    Set-Content -LiteralPath (Join-Path $badImage 'System.Memory.dll') -Value ([byte[]](0x4D, 0x5A, 0, 0)) -Encoding Byte
    $result = Invoke-Audit $badImage
    Assert-Result ($result.ExitCode -ne 0 -and $result.Text -match 'System.Memory') 'A required managed DLL with a bad image is rejected' $result.Text

    $missingAssembly = Copy-AppFixture 'missing-assembly'
    Remove-Item -LiteralPath (Join-Path $missingAssembly 'System.Text.Json.dll') -Force
    $result = Invoke-Audit $missingAssembly
    Assert-Result ($result.ExitCode -ne 0 -and $result.Text -match 'System.Text.Json') 'A missing required managed assembly is rejected' $result.Text

    $missingToken = Copy-AppFixture 'missing-redirect-token'
    $configPath = Join-Path $missingToken 'AstroHybrid.exe.config'
    [xml]$config = Get-Content -LiteralPath $configPath
    $namespaces = New-Object Xml.XmlNamespaceManager($config.NameTable)
    $namespaces.AddNamespace('asm', 'urn:schemas-microsoft-com:asm.v1')
    $identity = $config.SelectSingleNode("//asm:assemblyIdentity[@name='System.Memory']", $namespaces)
    $identity.RemoveAttribute('publicKeyToken')
    $config.Save($configPath)
    $result = Invoke-Audit $missingToken
    Assert-Result ($result.ExitCode -ne 0) 'A strong-name redirect without a public key token is rejected' $result.Text

    $wrongCulture = Copy-AppFixture 'wrong-redirect-culture'
    $configPath = Join-Path $wrongCulture 'AstroHybrid.exe.config'
    [xml]$config = Get-Content -LiteralPath $configPath
    $identity = $config.SelectSingleNode("//asm:assemblyIdentity[@name='System.Memory']", $namespaces)
    $identity.SetAttribute('culture', 'en-US')
    $config.Save($configPath)
    $result = Invoke-Audit $wrongCulture
    Assert-Result ($result.ExitCode -ne 0) 'A redirect with a mismatched culture is rejected' $result.Text

    $architecture = Copy-AppFixture 'architecture-scoped-redirect'
    $configPath = Join-Path $architecture 'AstroHybrid.exe.config'
    [xml]$config = Get-Content -LiteralPath $configPath
    $namespaces = New-Object Xml.XmlNamespaceManager($config.NameTable)
    $namespaces.AddNamespace('asm', 'urn:schemas-microsoft-com:asm.v1')
    $identity = $config.SelectSingleNode("//asm:assemblyIdentity[@name='System.Memory']", $namespaces)
    $identity.SetAttribute('processorArchitecture', 'x86')
    $config.Save($configPath)
    $result = Invoke-Audit $architecture
    Assert-Result ($result.ExitCode -ne 0) 'A processor-architecture-scoped redirect fails closed' $result.Text

    $runtime = Copy-AppFixture 'runtime-scoped-redirect'
    $configPath = Join-Path $runtime 'AstroHybrid.exe.config'
    [xml]$config = Get-Content -LiteralPath $configPath
    $dependent = $config.SelectSingleNode("//asm:dependentAssembly[asm:assemblyIdentity[@name='System.Memory']]", $namespaces)
    $binding = $dependent.ParentNode
    $binding.SetAttribute('appliesTo', 'v3.5')
    $config.Save($configPath)
    $result = Invoke-Audit $runtime
    Assert-Result ($result.ExitCode -ne 0) 'A runtime-scoped redirect fails closed' $result.Text

    $continuedAfterFailure = $false
    try {
        & $audit -AppDir (Join-Path $tempRoot 'does-not-exist') | Out-Null
        $continuedAfterFailure = $true
    }
    catch {
        # Expected: script-scoped callers receive a terminating audit failure.
    }
    Assert-Result (-not $continuedAfterFailure) 'Script callers stop after an isolated audit failure'

    try {
        $firstAuditOutput = & $audit -AppDir $clean 2>&1
        if ($LASTEXITCODE -ne 0) { throw "First audit failed: $firstAuditOutput" }
        $secondAuditOutput = & $audit -AppDir $clean 2>&1
        if ($LASTEXITCODE -ne 0) { throw "Second audit failed: $secondAuditOutput" }
        $stream = [IO.File]::Open((Join-Path $clean 'System.Memory.dll'), 'Open', 'ReadWrite', 'None')
        $stream.Dispose()
        Assert-Result $true 'Audited DLLs can be replaced in the same PowerShell process'
    }
    catch {
        Assert-Result $false 'Audited DLLs can be replaced in the same PowerShell process' $_.Exception.Message
    }
}
finally {
    if (Test-Path -LiteralPath $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}

if ($script:failures -gt 0) { throw "$script:failures managed dependency regression test(s) failed." }
'All managed dependency regression tests passed.'
