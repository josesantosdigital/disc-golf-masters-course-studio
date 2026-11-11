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

function Get-SafeDlls([string]$Folder, [string]$Pattern, [bool]$Strict = $false) {
    foreach ($entry in @(Get-ChildItem -LiteralPath $Folder -Force)) {
        if ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) { if ($Strict) { throw 'A linked item exists inside the loader folder; no files were changed.' }; continue }
        if ($entry.PSIsContainer) { Get-SafeDlls $entry.FullName $Pattern $Strict }
        elseif ($entry.Name -like $Pattern) { $entry.FullName }
    }
}

function Invoke-Toggle([string]$Root, [string]$Mode) {
    $rootPath = (Resolve-Path -LiteralPath $Root).Path
    if (-not (Test-Path -LiteralPath (Join-Path $rootPath 'Disc Golf Masters.exe') -PathType Leaf)) { throw 'The selected folder does not contain Disc Golf Masters.exe.' }
    $plugins = Join-Path $rootPath 'BepInEx\plugins'
    $loader = Join-Path $plugins 'CustomCourseLoader'
    foreach ($dir in @((Join-Path $rootPath 'BepInEx'), $plugins, $loader)) {
        if (-not (Test-Path -LiteralPath $dir -PathType Container)) { throw 'Course Studio was not found in this game folder.' }
        if ((Get-Item -LiteralPath $dir -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'A linked plugin folder was detected; no files were changed.' }
    }
    $prefix = [IO.Path]::GetFullPath($loader).TrimEnd('\') + '\'
    $statePath = Join-Path $loader 'course-studio-toggle.json'
    $enabled = @(Get-SafeDlls $loader 'CustomCourseLoader*.dll' $true)
    $outside = @(Get-SafeDlls $plugins 'CustomCourseLoader*.dll' | Where-Object { -not $_.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) })
    if ($outside.Count) { throw 'Another CustomCourseLoader DLL exists outside its normal plugin folder. Resolve the duplicate before using this tool.' }
    $main = Join-Path $loader 'CustomCourseLoader.dll'
    $mainDisabled = $main + '.disabled'
    if ($Mode -eq 'Status') {
        if ($enabled.Count -gt 0) { return 'Enabled (or partially enabled): loader DLL files are present.' }
        if (Test-Path -LiteralPath $mainDisabled -PathType Leaf) { return 'Disabled: the main loader DLL is inactive.' }
        return 'Loader DLL not found. Reinstall Course Studio if needed.'
    }
    if (@(Get-Process -Name 'Disc Golf Masters' -ErrorAction SilentlyContinue).Count) { throw 'Close Disc Golf Masters before changing the mod state.' }
    if ($Mode -eq 'Disable') {
        if (Test-Path -LiteralPath $statePath) { if ($enabled.Count -eq 0) { return 'Course Studio is already disabled.' }; throw 'The loader is partly enabled and a previous toggle record exists. Resolve the conflict before continuing.' }
        if (-not (Test-Path -LiteralPath $main -PathType Leaf)) { if ((Test-Path -LiteralPath $mainDisabled -PathType Leaf) -and $enabled.Count -eq 0) { return 'Course Studio is already disabled.' }; throw 'The main loader is missing or disabled while other loader copies remain active. No files were changed.' }
        foreach ($file in $enabled) { if (Test-Path -LiteralPath ($file + '.disabled')) { throw "A disabled copy already exists: $file.disabled. No files were changed." } }
        $relative = @($enabled | ForEach-Object { $_.Substring($prefix.Length) })
        $record = @{ version = 1; files = $relative } | ConvertTo-Json -Depth 3
        [IO.File]::WriteAllText($statePath, $record, [Text.UTF8Encoding]::new($false))
        $moved = [Collections.Generic.List[string]]::new()
        try {
            foreach ($file in $enabled) { Move-Item -LiteralPath $file -Destination ($file + '.disabled') -ErrorAction Stop; $moved.Add($file) }
        } catch {
            $failure = $_.Exception.Message
            $rollbackFailed = $false
            foreach ($file in $moved) { try { Move-Item -LiteralPath ($file + '.disabled') -Destination $file -ErrorAction Stop } catch { $rollbackFailed = $true } }
            if (-not $rollbackFailed) { Remove-Item -LiteralPath $statePath -Force }
            throw "Could not disable all loader files: $failure. Check the toggle record before retrying."
        }
        return "Course Studio disabled. $($enabled.Count) loader DLL file(s) switched off. Maps and courses were preserved."
    }
    if (-not (Test-Path -LiteralPath $statePath -PathType Leaf)) {
        if (Test-Path -LiteralPath $main -PathType Leaf) { return 'Course Studio is already enabled.' }
        if (-not (Test-Path -LiteralPath $mainDisabled -PathType Leaf)) { throw 'No disabled loader was found. Reinstall Course Studio if needed.' }
        Move-Item -LiteralPath $mainDisabled -Destination $main -ErrorAction Stop
        return 'Course Studio enabled. The main loader DLL was restored.'
    }
    $record = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
    if ($record.version -ne 1 -or -not $record.files -or @($record.files).Count -gt 1000) { throw 'The toggle record is invalid; no files were changed.' }
    $paths = [Collections.Generic.List[string]]::new()
    foreach ($relative in @($record.files)) {
        if ($relative -isnot [string] -or [IO.Path]::IsPathRooted($relative) -or ($relative -split '[\\/]') -contains '..' -or [IO.Path]::GetFileName($relative) -notlike 'CustomCourseLoader*.dll') { throw 'The toggle record contains an unsafe path.' }
        $file = [IO.Path]::GetFullPath((Join-Path $loader $relative))
        if (-not $file.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'The toggle record escapes the loader folder.' }
        $parent = Split-Path -Parent $file
        while ($parent.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
            if (-not (Test-Path -LiteralPath $parent -PathType Container) -or ((Get-Item -LiteralPath $parent -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'A recorded loader directory is missing or linked.' }
            $parent = Split-Path -Parent $parent
        }
        if ((Test-Path -LiteralPath $file) -and (Test-Path -LiteralPath ($file + '.disabled'))) { throw "Both enabled and disabled copies exist: $file. No files were changed." }
        if (-not (Test-Path -LiteralPath $file) -and -not (Test-Path -LiteralPath ($file + '.disabled'))) { throw "A recorded loader file is missing: $file. No files were changed." }
        $paths.Add($file)
    }
    foreach ($file in $paths) { if (Test-Path -LiteralPath ($file + '.disabled')) { Move-Item -LiteralPath ($file + '.disabled') -Destination $file -ErrorAction Stop } }
    Remove-Item -LiteralPath $statePath -Force
    return "Course Studio enabled. $($paths.Count) loader DLL file(s) restored."
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
        $choice = [Windows.Forms.MessageBox]::Show("$status`n`nYes = Disable Course Studio`nNo = Enable Course Studio`nCancel = Leave unchanged`n`nClose the game first. Maps and courses are preserved; other mods are unchanged.", 'Course Studio Mod Toggle', 'YesNoCancel', 'Information')
        if ($choice -eq [Windows.Forms.DialogResult]::Cancel) { exit 0 }
        $Action = if ($choice -eq [Windows.Forms.DialogResult]::Yes) { 'Disable' } else { 'Enable' }
    }
    $result = Invoke-Toggle $GameRoot $Action
    if ($interactive) { [void][Windows.Forms.MessageBox]::Show($result, 'Course Studio Mod Toggle', 'OK', 'Information') } else { Write-Output $result }
} catch {
    if ($interactive) { [void][Windows.Forms.MessageBox]::Show($_.Exception.Message, 'Course Studio Mod Toggle', 'OK', 'Error') } else { Write-Error $_.Exception.Message }
    exit 1
}
