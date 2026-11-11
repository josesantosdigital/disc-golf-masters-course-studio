[CmdletBinding()]
param(
    [string]$GameRoot,
    [ValidateSet('Enable','Disable','Status')][string]$Action
)
$ErrorActionPreference = 'Stop'

function Find-SteamGame {
    $steam = (Get-ItemProperty -Path 'HKCU:\Software\Valve\Steam' -ErrorAction SilentlyContinue).SteamPath
    if ([string]::IsNullOrWhiteSpace($steam) -and ${env:ProgramFiles(x86)}) { $steam = Join-Path ${env:ProgramFiles(x86)} 'Steam' }
    if ([string]::IsNullOrWhiteSpace($steam)) { return $null }
    $libraries = @($steam)
    $vdf = Join-Path $steam 'steamapps\libraryfolders.vdf'
    if (Test-Path -LiteralPath $vdf -PathType Leaf) {
        foreach ($match in [regex]::Matches((Get-Content -LiteralPath $vdf -Raw), '(?m)"path"\s*"((?:\\\\|[^"\\])*)"')) { $libraries += $match.Groups[1].Value.Replace('\\', '\') }
    }
    foreach ($library in ($libraries | Select-Object -Unique)) {
        $manifest = Join-Path $library 'steamapps\appmanifest_2819400.acf'
        if (-not (Test-Path -LiteralPath $manifest -PathType Leaf)) { continue }
        $match = [regex]::Match((Get-Content -LiteralPath $manifest -Raw), '"installdir"\s*"([^"\\/]+)"')
        if ($match.Success) {
            $candidate = Join-Path (Join-Path $library 'steamapps\common') $match.Groups[1].Value
            if (Test-Path -LiteralPath (Join-Path $candidate 'Disc Golf Masters.exe') -PathType Leaf) { return $candidate }
        }
    }
    return $null
}

function Invoke-Toggle([string]$Root, [string]$Mode) {
    $rootPath = (Resolve-Path -LiteralPath $Root).Path
    if (-not (Test-Path -LiteralPath (Join-Path $rootPath 'Disc Golf Masters.exe') -PathType Leaf)) { throw 'The selected folder does not contain Disc Golf Masters.exe.' }
    $config = Join-Path $rootPath 'doorstop_config.ini'
    $core = Join-Path $rootPath 'BepInEx\core\BepInEx.Unity.IL2CPP.dll'
    if (-not (Test-Path -LiteralPath $config -PathType Leaf) -or -not (Test-Path -LiteralPath $core -PathType Leaf)) { throw 'A BepInEx IL2CPP installation was not found in this folder.' }
    if ((Get-Item -LiteralPath $config -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'The Doorstop configuration is linked; no files were changed.' }
    $text = Get-Content -LiteralPath $config -Raw
    if ($text -notmatch '(?im)^\s*target_assembly\s*=\s*BepInEx[\\/]core[\\/]BepInEx\.Unity\.IL2CPP\.dll\s*$') { throw 'Doorstop does not point to the expected BepInEx IL2CPP loader; no files were changed.' }
    $hook = Join-Path $rootPath 'winhttp.dll'
    $disabled = Join-Path $rootPath 'winhttp.dll.course-studio-disabled'
    foreach ($item in @($hook,$disabled)) { if ((Test-Path -LiteralPath $item) -and ((Get-Item -LiteralPath $item -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'A linked startup file was detected; no files were changed.' } }
    $hasHook = Test-Path -LiteralPath $hook -PathType Leaf
    $hasDisabled = Test-Path -LiteralPath $disabled -PathType Leaf
    if ($hasHook -and $hasDisabled) { throw 'Both enabled and disabled startup files exist. No files were changed; reconcile the conflict first.' }
    $legacyRecord = Join-Path $rootPath 'BepInEx\plugins\CustomCourseLoader\course-studio-toggle.json'
    if ($Mode -eq 'Status') {
        if ($hasHook) { if (Test-Path -LiteralPath $legacyRecord) { return 'BepInEx is enabled, but the older Course Studio toggle has disabled the course loader.' }; return 'BepInEx startup hook is enabled.' }
        if ($hasDisabled) { return 'BepInEx startup hook is disabled by this tool.' }
        return 'BepInEx startup hook not found. No files were changed.'
    }
    if (@(Get-Process -Name 'Disc Golf Masters' -ErrorAction SilentlyContinue).Count) { throw 'Close Disc Golf Masters before changing the mod state.' }
    if ($Mode -eq 'Disable') {
        if ($hasDisabled) { return 'All BepInEx mods are already disabled by this tool.' }
        if (-not $hasHook) { throw 'The BepInEx winhttp.dll startup hook is missing; no files were changed.' }
        Move-Item -LiteralPath $hook -Destination $disabled -ErrorAction Stop
        return 'BepInEx startup disabled. All BepInEx mods will be inactive on the next game launch. Files, maps and courses are preserved.'
    }
    if (-not $hasHook -and -not $hasDisabled) { throw 'The BepInEx startup hook was not found. Reinstall or repair BepInEx if needed.' }
    if (Test-Path -LiteralPath $legacyRecord -PathType Leaf) {
        $legacy = Join-Path $PSScriptRoot 'Toggle-CourseStudio.ps1'
        if (-not (Test-Path -LiteralPath $legacy -PathType Leaf)) { throw 'The previous toggle record exists, but the bundled legacy restore script is missing. No startup files were changed.' }
        $previousPreference = $ErrorActionPreference
        try {
            $ErrorActionPreference = 'Continue'
            $legacyOutput = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $legacy -GameRoot $rootPath -Action Enable 2>&1 | Out-String
            $legacyExit = $LASTEXITCODE
        } finally { $ErrorActionPreference = $previousPreference }
        if ($legacyExit -ne 0) { throw "The older course-loader toggle could not be restored. BepInEx startup was not changed. Details: $legacyOutput" }
    }
    if ($hasDisabled) { Move-Item -LiteralPath $disabled -Destination $hook -ErrorAction Stop }
    return 'BepInEx startup enabled. The previous Course Studio toggle was restored if present; other individual mod settings are unchanged.'
}

$interactive = [string]::IsNullOrWhiteSpace($Action)
try {
    if (-not $GameRoot) { $GameRoot = Find-SteamGame }
    if (-not $GameRoot) {
        if (-not $interactive) { throw 'Game folder not found. Pass -GameRoot with the game folder.' }
        Add-Type -AssemblyName System.Windows.Forms
        $picker = [Windows.Forms.FolderBrowserDialog]::new()
        $picker.Description = 'Select the folder containing Disc Golf Masters.exe'
        if ($picker.ShowDialog() -ne [Windows.Forms.DialogResult]::OK) { exit 0 }
        $GameRoot = $picker.SelectedPath
    }
    if ($interactive) {
        Add-Type -AssemblyName System.Windows.Forms
        $status = Invoke-Toggle $GameRoot 'Status'
        $choice = [Windows.Forms.MessageBox]::Show("$status`n`nYes = Disable ALL BepInEx mods`nNo = Re-enable BepInEx mods`nCancel = Leave unchanged`n`nClose the game first. Courses, maps and mod files are preserved.", 'Disc Golf Masters Mod Toggle', 'YesNoCancel', 'Information')
        if ($choice -eq [Windows.Forms.DialogResult]::Cancel) { exit 0 }
        $Action = if ($choice -eq [Windows.Forms.DialogResult]::Yes) { 'Disable' } else { 'Enable' }
    }
    $result = Invoke-Toggle $GameRoot $Action
    if ($interactive) { [void][Windows.Forms.MessageBox]::Show($result, 'Disc Golf Masters Mod Toggle', 'OK', 'Information') } else { Write-Output $result }
} catch {
    if ($interactive) { [void][Windows.Forms.MessageBox]::Show($_.Exception.Message, 'Disc Golf Masters Mod Toggle', 'OK', 'Error') } else { Write-Error $_.Exception.Message }
    exit 1
}
