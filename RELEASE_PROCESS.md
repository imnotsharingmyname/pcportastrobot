# AstroHybrid Launcher Release Process

This document describes how to prepare and publish a new launcher release that works with the automatic update system.

## Overview

The launcher checks for updates via the GitHub Releases API. When a new release is published with a properly formatted launcher package, users will be notified and can update directly from the launcher.

## Release Package Requirements

The launcher expects a ZIP file with the following naming convention:
- **Pattern**: `AstroBotPC-AstroHybrid-{version}.zip` (e.g., `AstroBotPC-AstroHybrid-1.1.0.zip`)
- **Size**: Less than 300 MB (to distinguish from full game packages)
- **Contents**: The launcher executable (`AstroHybrid.exe`), its dependencies (DLLs, config files), and any resources

## Preparing a Release

### 1. Build the Launcher

```powershell
# Build in Release configuration
msbuild AstroHybrid.App/AstroHybrid.App.csproj /p:Configuration=Release
```

The built files will be in `AstroHybrid.App/bin/Release/`

### 2. Create the Release Package

Create a ZIP file containing:
- `AstroHybrid.exe`
- All `*.dll` files from the build output
- `AstroHybrid.exe.config` (the generated config with binding redirects)
- `Resources/astrohybrid.ico` (if not embedded)
- Any other required runtime files

**Example PowerShell script:**
```powershell
$version = "1.1.0"
$buildDir = "AstroHybrid.App\bin\Release"
$packageName = "AstroBotPC-AstroHybrid-$version"
$stageDir = "release\$packageName"

# Clean and create staging directory
if (Test-Path $stageDir) { Remove-Item -Recurse -Force $stageDir }
New-Item -ItemType Directory -Force $stageDir | Out-Null

# Copy launcher files
Get-ChildItem $buildDir -File | Where-Object { $_.Extension -in '.exe', '.dll', '.config' } | Copy-Item -Destination $stageDir

# Copy the generated config (has binding redirects)
Copy-Item "$buildDir\AstroHybrid.exe.config" $stageDir

# Create ZIP
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($stageDir, "release\$packageName.zip", [IO.Compression.CompressionLevel]::Optimal, $true)
```

### 3. Generate SHA256 (Optional but Recommended)

```powershell
$zipPath = "release\AstroBotPC-AstroHybrid-$version.zip"
$sha256 = Get-FileHash $zipPath -Algorithm SHA256 | Select-Object -ExpandProperty Hash
Write-Host "SHA256: $sha256"
```

### 4. Create GitHub Release

1. Go to https://github.com/imnotsharingmyname/pcportastrobot/releases/new
2. **Tag version**: `v{version}` (e.g., `v1.1.0`)
3. **Release title**: `AstroHybrid {version}` (e.g., `AstroHybrid 1.1.0`)
4. **Description**: Include release notes with:
   - New features
   - Bug fixes
   - Improvements
   - Known issues (if any)
5. **Attach the ZIP file**: Upload `AstroBotPC-AstroHybrid-{version}.zip`
6. **Publish release** (not as pre-release)

### 5. Version Numbering

Use semantic versioning: `MAJOR.MINOR.PATCH`
- **MAJOR**: Breaking changes, major redesigns
- **MINOR**: New features, significant improvements
- **PATCH**: Bug fixes, small improvements

The launcher compares versions numerically (e.g., 1.1.0 > 1.0.9).

## How the Update System Works

### Update Check Flow

1. **On startup** (if enabled): Launcher checks for updates after a 3-second delay
2. **Manual check**: User clicks "Check for Updates" in settings
3. **Interval**: Checks at most once per 24 hours (configurable via `AutoCheckUpdates` setting)

### GitHub API Query

- Endpoint: `https://api.github.com/repos/imnotsharingmyname/pcportastrobot/releases`
- Filters: Non-prerelease releases only
- Asset matching: ZIP files containing "astrohybrid" in name, under 300 MB

### Update Notification

When an update is available:
1. **Banner notification** appears at top of home page
2. **Version badges** show "AVAILABLE" with new version number
3. **"What's New" section** displays release notes
4. **UPDATE button** appears next to PLAY button in bottom bar

### Update Installation

1. User clicks "Update Now" or "Update" button
2. Launcher downloads ZIP to `%APPDATA%\AstroHybrid\Build\launcher\temp\`
3. Verifies SHA256 (if provided in release metadata)
4. Backs up current launcher to `%APPDATA%\AstroHybrid\Build\launcher\backup\`
5. Extracts and installs new files
6. Restarts launcher automatically

### Error Handling

- **Network errors**: Graceful fallback, shows error in status bar
- **Download interrupted**: Can retry, partial downloads cleaned up
- **Installation failed**: Automatic rollback from backup
- **Offline mode**: Launcher works normally, update check skipped

## Settings

The following settings control update behavior (stored in `astrohybrid-settings.json`):

| Setting | Type | Default | Description |
|---------|------|---------|-------------|
| `autoCheckUpdates` | bool | `true` | Enable automatic update checks on startup |
| `lastUpdateCheck` | DateTime? | `null` | Timestamp of last update check |
| `ignoredUpdateVersion` | string? | `null` | Version user chose to ignore |

## Troubleshooting

### Update Not Detected

1. Verify release is **not** marked as pre-release
2. Verify ZIP filename matches `AstroBotPC-AstroHybrid-{version}.zip`
3. Verify ZIP size is under 300 MB
4. Check GitHub API rate limits (unauthenticated: 60/hour)

### Download Fails

- Check internet connectivity
- Verify GitHub CDN accessibility
- Check disk space in temp directory

### Installation Fails

- Check write permissions to application directory
- Antivirus may block file replacement
- Check backup directory for rollback files

## Example Release Notes Format

```markdown
## AstroHybrid 1.1.0

### New Features
- Added support for XYZ feature
- New graphics preset for RTX 50 series

### Improvements
- Faster launcher startup
- Reduced memory usage by 20%

### Bug Fixes
- Fixed crash when switching graphics cards
- Resolved controller detection issue on Windows 11

### Known Issues
- None
```