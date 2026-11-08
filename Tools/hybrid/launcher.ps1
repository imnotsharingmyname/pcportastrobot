# AstroBot launcher: settings window for AstroHybrid (AstroBot.exe + Kyty's GPU stack).
# Saves the choices to Build\astrohybrid-settings.json and starts Run-AstroHybrid.bat with them
# (environment variables for this launch only; nothing system-wide changes).
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[Windows.Forms.Application]::EnableVisualStyles()

$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$settingsPath = Join-Path $root 'Build\astrohybrid-settings.json'

$lightingLabels = @('Faster lighting (recommended)', 'Original lighting (slower)')
$lightingExes   = @('AstroBot-NonRT.exe', 'AstroBot.exe')
$fpsLabels = @('30 FPS - steady, even pacing (recommended)', '40 FPS', '45 FPS', '60 FPS', '120 FPS', 'No limit')
$fpsValues = @('30', '40', '45', '60', '120', 'unlimited')
$sizeLabels = @('1280 x 720', '1600 x 900', '1920 x 1080', '2560 x 1440')
$sizeValues = @(@(1280, 720), @(1600, 900), @(1920, 1080), @(2560, 1440))

$settings = [ordered]@{ lighting = 0; fps = 0; size = 2; fullscreen = $true; realspeed = $true }
if (Test-Path -LiteralPath $settingsPath) {
    try {
        $saved = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
        foreach ($key in @('lighting', 'fps', 'size', 'fullscreen', 'realspeed')) {
            if ($null -ne $saved.$key) { $settings[$key] = $saved.$key }
        }
    } catch { }
}
$clamp = { param($value, $count) if ($value -is [int] -and $value -ge 0 -and $value -lt $count) { $value } else { 0 } }

$form = New-Object Windows.Forms.Form
$form.Text = 'Astro Bot - AstroHybrid Launcher'
$form.AutoScaleMode = 'Dpi'
$form.ClientSize = New-Object Drawing.Size(460, 364)
$form.FormBorderStyle = 'FixedDialog'
$form.StartPosition = 'CenterScreen'
$form.MaximizeBox = $false

$y = 18
function Add-Row([string]$text, [string[]]$items, [int]$selected) {
    $label = New-Object Windows.Forms.Label
    $label.Text = $text
    $label.AutoSize = $true
    $label.Location = New-Object Drawing.Point(18, ($script:y + 4))
    $box = New-Object Windows.Forms.ComboBox
    $box.AccessibleName = $text
    $box.DropDownStyle = 'DropDownList'
    $box.Location = New-Object Drawing.Point(150, $script:y)
    $box.Width = 290
    $box.Items.AddRange($items)
    $box.SelectedIndex = $selected
    $form.Controls.AddRange(@($label, $box))
    $script:y += 36
    return $box
}

$lighting = Add-Row 'Lighting' $lightingLabels (& $clamp $settings.lighting $lightingLabels.Count)
$fps = Add-Row 'Frame rate' $fpsLabels (& $clamp $settings.fps $fpsLabels.Count)
$size = Add-Row 'Window size' $sizeLabels (& $clamp $settings.size $sizeLabels.Count)

$fullscreen = New-Object Windows.Forms.CheckBox
$fullscreen.Text = 'Start in fullscreen'
$fullscreen.AutoSize = $true
$fullscreen.Checked = [bool]$settings.fullscreen
$fullscreen.Location = New-Object Drawing.Point(150, $y)
$form.Controls.Add($fullscreen)
$y += 28
$realspeed = New-Object Windows.Forms.CheckBox
$realspeed.Text = 'Real-time game speed (no slow motion below 60 FPS)'
$realspeed.AutoSize = $true
$realspeed.Checked = [bool]$settings.realspeed
$realspeed.Location = New-Object Drawing.Point(150, $y)
$form.Controls.Add($realspeed)
$y += 34

$notes = New-Object Windows.Forms.Label
$notes.Location = New-Object Drawing.Point(18, $y)
$notes.Size = New-Object Drawing.Size(424, 112)
$notes.Text = "In game: F2 shows the performance overlay, Ctrl+F2 changes the frame-rate limit, F11 or Alt+Enter toggles fullscreen.`r`n`r`n" +
    "The game was made for 60 FPS and plays in slow motion below that unless real-time speed is on. It runs at about 30 FPS in heavy areas (the CPU is the limit), so a steady 30 feels smoother than an uneven 31-40. " +
    "Higher limits help in lighter areas such as the world map. The window size scales the picture; the game chooses its own render resolution."
$form.Controls.Add($notes)

$play = New-Object Windows.Forms.Button
$play.Text = 'Play'
$play.Size = New-Object Drawing.Size(100, 30)
$play.Location = New-Object Drawing.Point(236, 320)
$cancel = New-Object Windows.Forms.Button
$cancel.Text = 'Cancel'
$cancel.Size = New-Object Drawing.Size(100, 30)
$cancel.Location = New-Object Drawing.Point(342, 320)
$cancel.DialogResult = 'Cancel'
$play.Add_Click({
    try {
        $out = [ordered]@{ lighting = $lighting.SelectedIndex; fps = $fps.SelectedIndex; size = $size.SelectedIndex; fullscreen = $fullscreen.Checked; realspeed = $realspeed.Checked }
        [void][IO.Directory]::CreateDirectory((Split-Path $settingsPath -Parent))
        [IO.File]::WriteAllText($settingsPath, ($out | ConvertTo-Json))
        $form.DialogResult = 'OK'
    } catch {
        [void][Windows.Forms.MessageBox]::Show($form, $_.Exception.Message, 'Could not save settings')
    }
})
$form.AcceptButton = $play
$form.CancelButton = $cancel
$form.Controls.AddRange(@($play, $cancel))

$result = $form.ShowDialog()
$chosen = @{ lighting = $lighting.SelectedIndex; fps = $fps.SelectedIndex; size = $size.SelectedIndex; fullscreen = $fullscreen.Checked; realspeed = $realspeed.Checked }
$form.Dispose()
if ($result -ne 'OK') { exit 0 }

$env:ASTROHYBRID_EXE = $lightingExes[$chosen.lighting]
$env:ASTROHYBRID_FPS_LIMIT = $fpsValues[$chosen.fps]
$env:KYTY_GPU_WIDTH = [string]$sizeValues[$chosen.size][0]
$env:KYTY_GPU_HEIGHT = [string]$sizeValues[$chosen.size][1]
$env:KYTY_GPU_FULLSCREEN = if ($chosen.fullscreen) { '1' } else { '0' }
$env:ASTROHYBRID_GAME_SPEED = if ($chosen.realspeed) { '1' } else { '0' }
$launcher = Join-Path $root 'Run-AstroHybrid.bat'
$process = Start-Process -FilePath $env:ComSpec -ArgumentList '/c', "`"$launcher`"" -WorkingDirectory $root -WindowStyle Minimized -PassThru
$process.WaitForExit()
exit $process.ExitCode
