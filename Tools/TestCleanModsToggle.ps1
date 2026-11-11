$ErrorActionPreference='Stop'
$script=Join-Path $PSScriptRoot 'Toggle-CleanMods.ps1'
$old1=Join-Path $PSScriptRoot 'Toggle-CourseStudio.ps1'
$old2=Join-Path $PSScriptRoot 'Toggle-AllMods.ps1'
$temp=Join-Path ([IO.Path]::GetTempPath()) ('DgmCleanToggleTest-'+[guid]::NewGuid().ToString('N'))
$steamapps=Join-Path $temp 'steamapps'; $game=Join-Path $steamapps 'common\Disc Golf Masters'
$backup=Join-Path $steamapps 'common\Disc Golf Masters - Mod Backup'
$legacyBackup=Join-Path $steamapps 'Disc Golf Masters Mod Backup Safe'
$plugin=Join-Path $game 'BepInEx\plugins\CustomCourseLoader'; $other=Join-Path $game 'BepInEx\plugins\AimElevationMod'
function Assert([bool]$condition,[string]$message) { if(-not $condition) { throw $message } }
function Run([string]$file,[string]$mode,[bool]$success) {
    $old=$ErrorActionPreference
    try { $ErrorActionPreference='Continue'; $out=& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $file -GameRoot $game -Action $mode 2>&1 | Out-String; $code=$LASTEXITCODE } finally { $ErrorActionPreference=$old }
    if (($code -eq 0) -ne $success) { throw "$mode unexpected ($code): $out" }
}
try {
    New-Item -ItemType Directory -Force -Path $plugin,$other,(Join-Path $game 'BepInEx\core'),(Join-Path $plugin 'Courses'),(Join-Path $plugin 'Maps'),(Join-Path $plugin 'CourseEditor') | Out-Null
    [IO.File]::WriteAllText((Join-Path $game 'Disc Golf Masters.exe'),'game')
    [IO.File]::WriteAllText((Join-Path $game 'doorstop_config.ini'),"[General]`ntarget_assembly = BepInEx\core\BepInEx.Unity.IL2CPP.dll`n")
    [IO.File]::WriteAllText((Join-Path $game '.doorstop_version'),'4')
    [IO.File]::WriteAllText((Join-Path $game 'changelog.txt'),'unrelated game file')
    [IO.File]::WriteAllText((Join-Path $game 'BepInEx\core\BepInEx.Unity.IL2CPP.dll'),'core')
    [IO.File]::WriteAllText((Join-Path $game 'winhttp.dll'),'hook')
    [IO.File]::WriteAllText((Join-Path $plugin 'CustomCourseLoader.dll'),'loader')
    [IO.File]::WriteAllText((Join-Path $plugin 'Courses\course.json'),'course')
    [IO.File]::WriteAllText((Join-Path $plugin 'Maps\map.bundle'),'map')
    [IO.File]::WriteAllText((Join-Path $other 'AimElevationMod.dll'),'aim')
    $node=(Get-Command node.exe -ErrorAction Stop).Source
    $serverScript=Join-Path $plugin 'CourseEditor\server.js'
    [IO.File]::WriteAllText($serverScript,'setInterval(() => {}, 1000);')
    $server=Start-Process -FilePath $node -ArgumentList ('"'+$serverScript+'"') -WorkingDirectory (Split-Path -Parent $serverScript) -WindowStyle Hidden -PassThru
    Run $script 'Status' $true
    Run $script 'Disable' $true
    Assert (-not (Get-Process -Id $server.Id -ErrorAction SilentlyContinue)) 'Editor server was not stopped'
    Assert (-not (Test-Path (Join-Path $game 'BepInEx'))) 'BepInEx still in game folder'
    Assert (-not (Test-Path (Join-Path $game 'winhttp.dll'))) 'Startup hook still in game folder'
    Assert ((Get-Content (Join-Path $game 'changelog.txt') -Raw) -eq 'unrelated game file') 'Unrelated changelog moved'
    Assert ((Get-Content (Join-Path $backup 'BepInEx\plugins\CustomCourseLoader\Maps\map.bundle') -Raw) -eq 'map') 'Map not preserved'
    Assert ((Get-Content (Join-Path $backup 'BepInEx\plugins\CustomCourseLoader\Courses\course.json') -Raw) -eq 'course') 'Course not preserved'
    Run $script 'Disable' $true
    Run $script 'Enable' $true
    Assert ((Get-Content (Join-Path $game 'winhttp.dll') -Raw) -eq 'hook') 'Hook not restored'
    Assert ((Get-Content (Join-Path $other 'AimElevationMod.dll') -Raw) -eq 'aim') 'Other plugin not preserved'
    Assert (-not (Test-Path $backup)) 'Empty backup blocked a future cycle'
    Run $old1 'Disable' $true
    Run $old2 'Disable' $true
    Run $script 'Disable' $true
    Run $script 'Enable' $true
    Assert (Test-Path (Join-Path $plugin 'CustomCourseLoader.dll')) 'Old course toggle not restored'
    Assert (-not (Test-Path (Join-Path $plugin 'course-studio-toggle.json'))) 'Old toggle record remains'
    Assert (Test-Path (Join-Path $game 'winhttp.dll')) 'Old BepInEx toggle not restored'
    Run $script 'Disable' $true
    [IO.File]::WriteAllText((Join-Path $game 'winhttp.dll'),'conflict')
    Run $script 'Enable' $false
    Assert ((Get-Content (Join-Path $game 'winhttp.dll') -Raw) -eq 'conflict') 'Conflict overwritten'
    Assert (Test-Path (Join-Path $backup 'BepInEx')) 'Backup lost on conflict'
    Remove-Item -LiteralPath (Join-Path $game 'winhttp.dll')
    [IO.Directory]::Move($backup,$legacyBackup)
    Remove-Item -LiteralPath (Join-Path $legacyBackup 'course-studio-backup.json')
    [IO.File]::WriteAllText((Join-Path $legacyBackup 'RESTORE-INSTRUCTIONS.txt'),'Restorable BepInEx files for Disc Golf Masters. Test fixture.')
    Run $script 'Status' $true
    Run $script 'Enable' $true
    Assert (Test-Path (Join-Path $game 'BepInEx')) 'Existing legacy backup not restored'
    Assert (Test-Path (Join-Path $game 'winhttp.dll')) 'Existing legacy hook not restored'
    Assert ((Get-Content (Join-Path $game 'changelog.txt') -Raw) -eq 'unrelated game file') 'Unrelated file overwritten'
    Assert (Test-Path (Join-Path $legacyBackup 'RESTORE-INSTRUCTIONS.txt')) 'Legacy instructions unexpectedly removed'
    Run $script 'Status' $true
    Run $script 'Disable' $true
    Assert (Test-Path (Join-Path $backup 'BepInEx')) 'Stale legacy instructions blocked a later disable'
    Run $script 'Enable' $true
    Assert (Test-Path (Join-Path $game 'BepInEx')) 'Repeat cycle after legacy restoration failed'
    [IO.File]::WriteAllText((Join-Path $legacyBackup 'doorstop_config.ini'),'partial backup')
    Run $script 'Status' $false
    Assert (Test-Path (Join-Path $legacyBackup 'doorstop_config.ini')) 'Partial legacy backup was altered'
    Write-Output 'Clean toggle fixtures passed: repeat cycles, data preservation, old toggle and existing backup recovery, and conflict refusal.'
} finally {
    if ($server -and -not $server.HasExited) { Stop-Process -Id $server.Id -ErrorAction SilentlyContinue }
    $prefix=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')+'\'
    if (-not ([IO.Path]::GetFullPath($temp)).StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase) -or -not ([IO.Path]::GetFileName($temp)).StartsWith('DgmCleanToggleTest-')) { throw 'Refusing unsafe fixture cleanup.' }
    if(Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp -Recurse -Force }
}
