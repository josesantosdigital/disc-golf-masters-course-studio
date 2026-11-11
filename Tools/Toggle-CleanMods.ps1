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

function Assert-Loader([string]$Directory) {
    $config = Join-Path $Directory 'doorstop_config.ini'
    $core = Join-Path $Directory 'BepInEx\core\BepInEx.Unity.IL2CPP.dll'
    if (-not (Test-Path -LiteralPath $config -PathType Leaf) -or -not (Test-Path -LiteralPath $core -PathType Leaf)) { throw 'Expected BepInEx IL2CPP files are missing.' }
    foreach ($path in @($Directory,(Join-Path $Directory 'BepInEx'),$config,$core)) {
        if ((Get-Item -LiteralPath $path -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Linked loader paths are not supported; no files were changed.' }
    }
    if ((Get-Content -LiteralPath $config -Raw) -notmatch '(?im)^\s*target_assembly\s*=\s*BepInEx[\\/]core[\\/]BepInEx\.Unity\.IL2CPP\.dll\s*$') { throw 'Doorstop does not point to the expected BepInEx loader.' }
}

function Enable-Startup([string]$GamePath) {
    $helper = Join-Path $PSScriptRoot 'Toggle-AllMods.ps1'
    if (-not (Test-Path -LiteralPath $helper -PathType Leaf)) { throw 'The startup helper is missing; no startup files were changed.' }
    $previous = $ErrorActionPreference
    try { $ErrorActionPreference = 'Continue'; $output = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $helper -GameRoot $GamePath -Action Enable 2>&1 | Out-String; $code=$LASTEXITCODE } finally { $ErrorActionPreference=$previous }
    if ($code -ne 0) { throw "BepInEx could not be enabled. Details: $output" }
}

function Invoke-Toggle([string]$Root, [string]$Mode) {
    $game = (Resolve-Path -LiteralPath $Root).Path.TrimEnd('\')
    if (-not (Test-Path -LiteralPath (Join-Path $game 'Disc Golf Masters.exe') -PathType Leaf)) { throw 'Select the folder containing Disc Golf Masters.exe.' }
    if ((Get-Item -LiteralPath $game -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'The game folder is linked; no files were changed.' }
    $parent = Split-Path -Parent $game
    $backup = Join-Path $parent 'Disc Golf Masters - Mod Backup'
    $backup = [IO.Path]::GetFullPath($backup).TrimEnd('\')
    if (-not $backup.StartsWith([IO.Path]::GetFullPath($parent).TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetPathRoot($game) -ne [IO.Path]::GetPathRoot($backup)) { throw 'Invalid backup path.' }
    $legacy = $null
    $grandparent = Split-Path -Parent $parent
    if ((Split-Path -Leaf $parent) -ieq 'common' -and (Split-Path -Leaf $grandparent) -ieq 'steamapps') {
        $candidate = Join-Path $grandparent 'Disc Golf Masters Mod Backup Safe'
        $instructions = Join-Path $candidate 'RESTORE-INSTRUCTIONS.txt'
        if ((Test-Path -LiteralPath $instructions -PathType Leaf) -and (Get-Content -LiteralPath $instructions -Raw) -match '^Restorable BepInEx files for Disc Golf Masters\.') {
            $legacyEntries = @('BepInEx','doorstop_config.ini','.doorstop_version','changelog.txt','winhttp.dll','winhttp.dll.course-studio-disabled')
            $remaining = @($legacyEntries | Where-Object { Test-Path -LiteralPath (Join-Path $candidate $_) })
            if ($remaining.Count) { $legacy = $candidate }
        }
    }
    $saved = if (Test-Path -LiteralPath $backup) { $backup } elseif ($legacy) { $legacy } else { $null }
    $installed = Test-Path -LiteralPath (Join-Path $game 'BepInEx') -PathType Container
    if ($saved -and $installed) { throw 'Both a backup and installed BepInEx exist. No files were changed; reconcile them first.' }
    if ($Mode -eq 'Status') {
        if ($saved) { Assert-Loader $saved; return "Mods are backed up outside the game folder: $saved" }
        if ($installed) { Assert-Loader $game; return 'BepInEx is installed in the game folder.' }
        return 'No BepInEx installation or recognised backup was found.'
    }
    if (@(Get-Process -Name 'Disc Golf Masters' -ErrorAction SilentlyContinue).Count) { throw 'Close Disc Golf Masters before changing mods.' }
    if ($Mode -eq 'Disable') {
        if ($saved) { Assert-Loader $saved; return "Mods are already backed up at: $saved" }
        if (-not $installed) { throw 'No BepInEx installation was found.' }
        Assert-Loader $game
        $hook = Join-Path $game 'winhttp.dll'; $disabledHook = Join-Path $game 'winhttp.dll.course-studio-disabled'
        if ((Test-Path -LiteralPath $hook) -and (Test-Path -LiteralPath $disabledHook)) { throw 'Both enabled and disabled startup hooks exist. No files were changed.' }
        if (-not (Test-Path -LiteralPath $hook -PathType Leaf) -and -not (Test-Path -LiteralPath $disabledHook -PathType Leaf)) { throw 'The BepInEx startup hook is missing. No files were changed.' }
        $names = [Collections.Generic.List[string]]::new()
        foreach ($name in @('BepInEx','doorstop_config.ini','.doorstop_version','winhttp.dll','winhttp.dll.course-studio-disabled')) { if (Test-Path -LiteralPath (Join-Path $game $name)) { $names.Add($name) } }
        $changelog = Join-Path $game 'changelog.txt'
        if ((Test-Path -LiteralPath $changelog -PathType Leaf) -and (Get-FileHash -LiteralPath $changelog -Algorithm SHA256).Hash -eq 'BEEB8B48CC135810C371D9BA7919283B055B289B2D4B3401AC5763C196E15579') { $names.Add('changelog.txt') }
        foreach ($name in $names) { if ((Get-Item -LiteralPath (Join-Path $game $name) -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Linked file or folder refused: $name" } }
        $editorScript = Join-Path $game 'BepInEx\plugins\CustomCourseLoader\CourseEditor\server.js'
        try { $servers = @(Get-CimInstance Win32_Process -Filter "Name='node.exe'" -ErrorAction Stop | Where-Object { $_.CommandLine -and $_.CommandLine.IndexOf($editorScript,[StringComparison]::OrdinalIgnoreCase) -ge 0 }) }
        catch { throw 'Could not check Course Studio servers. Close Course Studio and try again.' }
        foreach ($server in $servers) { Stop-Process -Id $server.ProcessId -ErrorAction Stop }
        New-Item -ItemType Directory -Path $backup -ErrorAction Stop | Out-Null
        $state = @{ version=1; game=$game; files=@($names.ToArray()) } | ConvertTo-Json -Depth 3
        [IO.File]::WriteAllText((Join-Path $backup 'course-studio-backup.json'), $state, [Text.UTF8Encoding]::new($false))
        $moved = [Collections.Generic.List[string]]::new()
        try {
            foreach ($name in $names) {
                if (@(Get-Process -Name 'Disc Golf Masters' -ErrorAction SilentlyContinue).Count) { throw 'The game started during the operation.' }
                if ($name -eq 'BepInEx') { [IO.Directory]::Move((Join-Path $game $name),(Join-Path $backup $name)) } else { [IO.File]::Move((Join-Path $game $name),(Join-Path $backup $name)) }
                $moved.Add($name)
            }
        } catch {
            $reason = $_.Exception.Message; $failed = [Collections.Generic.List[string]]::new()
            for($i=$moved.Count-1;$i -ge 0;$i--) { $name=$moved[$i]; try { if ($name -eq 'BepInEx') { [IO.Directory]::Move((Join-Path $backup $name),(Join-Path $game $name)) } else { [IO.File]::Move((Join-Path $backup $name),(Join-Path $game $name)) } } catch { $failed.Add($name) } }
            if ($failed.Count) { throw "Move failed: $reason. Some files remain in $backup. Do not delete it." }
            Remove-Item -LiteralPath (Join-Path $backup 'course-studio-backup.json') -Force
            [IO.Directory]::Delete($backup)
            throw "Move failed and all moved files were restored: $reason. Close Course Studio and retry."
        }
        return "BepInEx was moved out of the game folder. All mod and course data is preserved in: $backup"
    }
    if (-not $saved) {
        if ($installed) { Enable-Startup $game; return 'BepInEx startup enabled. The previous course-only toggle was restored if present.' }
        throw 'No recognised mod backup was found.'
    }
    Assert-Loader $saved
    if (-not (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'Toggle-AllMods.ps1') -PathType Leaf)) { throw 'The startup helper is missing. No files were changed.' }
    $statePath = Join-Path $saved 'course-studio-backup.json'
    $allowed = @('BepInEx','doorstop_config.ini','.doorstop_version','changelog.txt','winhttp.dll','winhttp.dll.course-studio-disabled')
    if (Test-Path -LiteralPath $statePath -PathType Leaf) {
        $state = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
        if ($state.version -ne 1 -or $state.game -ine $game -or -not $state.files) { throw 'The backup record is invalid or belongs to another game folder.' }
        $names = @($state.files)
        if (($names | Select-Object -Unique).Count -ne $names.Count -or @($names | Where-Object { $_ -notin $allowed }).Count -or 'BepInEx' -notin $names -or 'doorstop_config.ini' -notin $names) { throw 'The backup record contains invalid entries.' }
    } elseif ($saved -eq $legacy) {
        $names = @($allowed | Where-Object { Test-Path -LiteralPath (Join-Path $saved $_) })
    } else { throw 'The backup record is missing. No files were changed.' }
    foreach($name in $names) {
        if (-not (Test-Path -LiteralPath (Join-Path $saved $name)) -or (Test-Path -LiteralPath (Join-Path $game $name))) { throw "Missing backup or destination conflict: $name. No files were changed." }
        if ((Get-Item -LiteralPath (Join-Path $saved $name) -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Linked backup entry refused: $name" }
    }
    if (('winhttp.dll' -in $names) -eq ('winhttp.dll.course-studio-disabled' -in $names)) { throw 'The backup must contain exactly one BepInEx startup hook.' }
    if ((Test-Path -LiteralPath (Join-Path $game 'winhttp.dll')) -or (Test-Path -LiteralPath (Join-Path $game 'winhttp.dll.course-studio-disabled'))) { throw 'A BepInEx startup hook already exists in the game folder.' }
    $moved = [Collections.Generic.List[string]]::new()
    try {
        foreach($name in $names) { if ($name -eq 'BepInEx') { [IO.Directory]::Move((Join-Path $saved $name),(Join-Path $game $name)) } else { [IO.File]::Move((Join-Path $saved $name),(Join-Path $game $name)) }; $moved.Add($name) }
    } catch {
        $reason=$_.Exception.Message; $failed=[Collections.Generic.List[string]]::new()
        for($i=$moved.Count-1;$i -ge 0;$i--) { $name=$moved[$i]; try { if($name -eq 'BepInEx') { [IO.Directory]::Move((Join-Path $game $name),(Join-Path $saved $name)) } else { [IO.File]::Move((Join-Path $game $name),(Join-Path $saved $name)) } } catch { $failed.Add($name) } }
        if($failed.Count) { throw "Restore failed: $reason. Some files may be in the game folder and backup. Do not delete either." }
        throw "Restore failed and all moves were rolled back: $reason"
    }
    try { Enable-Startup $game } catch {
        $reason=$_.Exception.Message; $failed=[Collections.Generic.List[string]]::new()
        for($i=$moved.Count-1;$i -ge 0;$i--) { $name=$moved[$i]; try { if($name -eq 'BepInEx') { [IO.Directory]::Move((Join-Path $game $name),(Join-Path $saved $name)) } else { [IO.File]::Move((Join-Path $game $name),(Join-Path $saved $name)) } } catch { $failed.Add($name) } }
        if($failed.Count) { throw "Startup failed: $reason. Some files may be in the game folder and backup. Do not delete either." }
        throw "Startup failed; files remain in the backup: $reason"
    }
    if (Test-Path -LiteralPath $statePath -PathType Leaf) { Remove-Item -LiteralPath $statePath -Force }
    if ($saved -eq $backup -and @(Get-ChildItem -LiteralPath $saved -Force).Count -eq 0) { [IO.Directory]::Delete($saved) }
    return 'BepInEx and its mod files were restored. Previous course-only toggle changes were also restored if present.'
}

$interactive = [string]::IsNullOrWhiteSpace($Action)
try {
    if (-not $GameRoot) { $GameRoot = Find-SteamGame }
    if (-not $GameRoot) {
        if (-not $interactive) { throw 'Game folder not found. Pass -GameRoot.' }
        Add-Type -AssemblyName System.Windows.Forms
        $picker = [Windows.Forms.FolderBrowserDialog]::new(); $picker.Description = 'Select the folder containing Disc Golf Masters.exe'
        if ($picker.ShowDialog() -ne [Windows.Forms.DialogResult]::OK) { exit 0 }; $GameRoot = $picker.SelectedPath
    }
    if ($interactive) {
        Add-Type -AssemblyName System.Windows.Forms
        $status = Invoke-Toggle $GameRoot 'Status'
        $choice = [Windows.Forms.MessageBox]::Show("$status`n`nYes = Disable all BepInEx mods`nNo = Restore and enable mods`nCancel = Leave unchanged`n`nClose the game and save your Course Studio work first. Disabling stops the local editor server. Maps and courses are preserved in the backup.", 'Disc Golf Masters Mod Toggle', 'YesNoCancel', 'Information')
        if ($choice -eq [Windows.Forms.DialogResult]::Cancel) { exit 0 }
        $Action = if ($choice -eq [Windows.Forms.DialogResult]::Yes) { 'Disable' } else { 'Enable' }
    }
    $result=Invoke-Toggle $GameRoot $Action
    if ($interactive) { [void][Windows.Forms.MessageBox]::Show($result, 'Disc Golf Masters Mod Toggle', 'OK', 'Information') } else { Write-Output $result }
} catch {
    if ($interactive) { [void][Windows.Forms.MessageBox]::Show($_.Exception.Message, 'Disc Golf Masters Mod Toggle', 'OK', 'Error') } else { Write-Error $_.Exception.Message }
    exit 1
}
