[CmdletBinding()]
param(
    [string]$AppDir = '',
    [switch]$InIsolatedProcess
)

$ErrorActionPreference = 'Stop'
# Windows PowerShell 5.1 leaves $PSScriptRoot empty in param() defaults, so the default is set here.
if (-not $AppDir) { $AppDir = Join-Path (Split-Path -Parent $PSScriptRoot) 'AstroHybrid.App\bin\Release' }
if (-not $InIsolatedProcess) {
    # Reflection-only assemblies cannot be unloaded. Isolate them so interactive rebuilds and releases never inherit file locks.
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $PSCommandPath -AppDir $AppDir -InIsolatedProcess
    $auditExitCode = $LASTEXITCODE
    if ($auditExitCode -ne 0) {
        throw "Managed dependency audit failed with exit code $auditExitCode."
    }
    exit 0
}

$AppDir = (Resolve-Path -LiteralPath $AppDir).Path
$exe = Join-Path $AppDir 'AstroHybrid.exe'
$configPath = Join-Path $AppDir 'AstroHybrid.exe.config'
foreach ($requiredFile in $exe, $configPath) {
    if (-not (Test-Path -LiteralPath $requiredFile -PathType Leaf)) {
        throw "Managed dependency audit requires $requiredFile"
    }
}

$files = @(Get-Item -LiteralPath $exe) + @(Get-ChildItem -LiteralPath $AppDir -Filter '*.dll' -File)
$identities = @{}
$badImages = @{}
$assemblies = @()
foreach ($file in $files) {
    try {
        # The isolated child process releases path mappings when it exits; avoid a second full-file byte copy.
        $assembly = [Reflection.Assembly]::ReflectionOnlyLoadFrom($file.FullName)
        $identity = $assembly.GetName()
    }
    catch [BadImageFormatException] {
        if ($file.Extension -ieq '.exe') { throw }
        $badImages[$file.BaseName] = $file
        continue
    }
    if ($identities.ContainsKey($identity.Name)) {
        throw "Multiple bundled assemblies have the simple name $($identity.Name)"
    }
    $identities[$identity.Name] = $identity
    $assemblies += $assembly
}

if ($assemblies.Count -eq 0) { throw "No managed assemblies found in $AppDir" }

[xml]$config = Get-Content -LiteralPath $configPath
$namespaces = New-Object Xml.XmlNamespaceManager($config.NameTable)
$namespaces.AddNamespace('asm', 'urn:schemas-microsoft-com:asm.v1')
$redirects = @{}
foreach ($node in $config.SelectNodes('//asm:dependentAssembly', $namespaces)) {
    $identityNode = $node.SelectSingleNode('asm:assemblyIdentity', $namespaces)
    $redirectNodes = @($node.SelectNodes('asm:bindingRedirect', $namespaces))
    if (($null -eq $identityNode) -or ($redirectNodes.Count -eq 0)) { continue }
    $name = $identityNode.GetAttribute('name')
    # Scoped redirects need target-specific evaluation. Reject them until supported instead of accepting an invalid release.
    if ($identityNode.GetAttribute('processorArchitecture')) {
        throw "Processor-architecture-scoped binding redirects are not supported by this audit: $name"
    }
    if ($node.ParentNode.GetAttribute('appliesTo')) {
        throw "Runtime-scoped binding redirects are not supported by this audit: $name"
    }
    foreach ($redirectNode in $redirectNodes) {
        $versions = $redirectNode.GetAttribute('oldVersion').Split('-')
        $redirect = [PSCustomObject]@{
            Minimum = [version]$versions[0]
            Maximum = [version]$versions[-1]
            NewVersion = [version]$redirectNode.GetAttribute('newVersion')
            PublicKeyToken = $identityNode.GetAttribute('publicKeyToken')
            Culture = $identityNode.GetAttribute('culture')
        }
        if (-not $redirects.ContainsKey($name)) { $redirects[$name] = @() }
        $redirects[$name] += $redirect
    }
}

$checkedReferences = 0
$failures = @()
foreach ($assembly in $assemblies) {
    foreach ($reference in $assembly.GetReferencedAssemblies()) {
        $referenceToken = (($reference.GetPublicKeyToken() | ForEach-Object { $_.ToString('x2') }) -join '')
        $referenceCulture = if ([string]::IsNullOrEmpty($reference.CultureName)) { 'neutral' } else { $reference.CultureName }
        if (-not $identities.ContainsKey($reference.Name)) {
            if ($badImages.ContainsKey($reference.Name)) {
                $failures += "$($assembly.GetName().Name) requires $($reference.Name) $($reference.Version), " +
                    "but $($badImages[$reference.Name].Name) is not a valid managed assembly."
            }
            else {
                try {
                    # Framework assemblies may resolve from the runtime/GAC. Missing application and package dependencies cannot.
                    [void][Reflection.Assembly]::ReflectionOnlyLoad($reference.FullName)
                }
                catch {
                    $frameworkResolved = $false
                    if ($referenceToken -in @('b77a5c561934e089', 'b03f5f7f11d50a3a')) {
                        try {
                            # .NET Framework unifies newer framework references onto its 4.0.0.0 GAC facades.
                            $frameworkName = "$($reference.Name), Version=4.0.0.0, Culture=$referenceCulture, PublicKeyToken=$referenceToken"
                            [void][Reflection.Assembly]::ReflectionOnlyLoad($frameworkName)
                            $frameworkResolved = $true
                        }
                        catch { }
                    }
                    if (-not $frameworkResolved) {
                        $failures += "$($assembly.GetName().Name) requires $($reference.FullName), " +
                            'which is neither bundled nor resolvable from the runtime/GAC.'
                    }
                }
            }
            continue
        }
        $checkedReferences++
        $installed = $identities[$reference.Name]
        $installedToken = (($installed.GetPublicKeyToken() | ForEach-Object { $_.ToString('x2') }) -join '')
        $installedCulture = if ([string]::IsNullOrEmpty($installed.CultureName)) { 'neutral' } else { $installed.CultureName }
        $identityMatches = ($referenceToken -ieq $installedToken) -and ($referenceCulture -ieq $installedCulture)
        $resolved = ($reference.Version -eq $installed.Version) -and $identityMatches
        if ($redirects.ContainsKey($reference.Name)) {
            foreach ($redirect in $redirects[$reference.Name]) {
                $redirectCulture = if ([string]::IsNullOrEmpty($redirect.Culture)) { 'neutral' } else { $redirect.Culture }
                $redirectIdentityMatches = [string]::Equals($redirect.PublicKeyToken, $referenceToken, [StringComparison]::OrdinalIgnoreCase) -and
                    $redirectCulture -ieq $referenceCulture
                $rangeMatches = $reference.Version -ge $redirect.Minimum -and $reference.Version -le $redirect.Maximum
                if ($redirectIdentityMatches -and $rangeMatches) {
                    # An applicable redirect changes the version, but never the strong-name identity.
                    $resolved = ($redirect.NewVersion -eq $installed.Version) -and $identityMatches
                    if ($resolved) { break }
                }
            }
        }
        if (-not $resolved) {
            $referenceTokenDisplay = if ($referenceToken) { $referenceToken } else { '<none>' }
            $installedTokenDisplay = if ($installedToken) { $installedToken } else { '<none>' }
            $failures += "$($assembly.GetName().Name) requires $($reference.Name) $($reference.Version) " +
                "(public key token $referenceTokenDisplay), but $($installed.Version) " +
                "(public key token $installedTokenDisplay) is bundled and no matching binding redirect resolves it."
        }
    }
}

if ($failures.Count -gt 0) {
    $details = ($failures | Sort-Object -Unique | ForEach-Object { " - $_" }) -join [Environment]::NewLine
    throw "Managed dependency audit failed:$([Environment]::NewLine)$details$([Environment]::NewLine)" +
        'Rebuild the launcher with MSBuild /t:Restore,Rebuild /p:Configuration=Release.'
}

"Managed dependency audit passed: $($assemblies.Count) assemblies, $checkedReferences bundled references."
