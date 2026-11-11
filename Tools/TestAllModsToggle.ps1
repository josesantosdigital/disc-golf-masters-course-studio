$ErrorActionPreference = 'Stop'
$toggle = Join-Path $PSScriptRoot 'Toggle-AllMods.ps1'
$legacy = Join-Path $PSScriptRoot 'Toggle-CourseStudio.ps1'
$root = Join-Path ([IO.Path]::GetTempPath()) ('DgmAllModsTest-' + [guid]::NewGuid().ToString('N'))
$plugin = Join-Path $root 'BepInEx\plugins\CustomCourseLoader'
$other = Join-Path $root 'BepInEx\plugins\AimElevationMod'
function Assert([bool]$condition, [string]$message) { if (-not $condition) { throw $message } }
function Run([string]$script, [string]$mode, [bool]$success) {
    $previous = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $output = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $script -GameRoot $root -Action $mode 2>&1 | Out-String
        $code = $LASTEXITCODE
    } finally { $ErrorActionPreference = $previous }
    if (($code -eq 0) -ne $success) { throw "$mode failed unexpectedly ($code): $output" }
}
try {
    New-Item -ItemType Directory -Force -Path $plugin,$other,(Join-Path $root 'BepInEx\core'),(Join-Path $plugin 'Courses'),(Join-Path $plugin 'Maps') | Out-Null
    [IO.File]::WriteAllText((Join-Path $root 'Disc Golf Masters.exe'), 'fixture')
    [IO.File]::WriteAllText((Join-Path $root 'doorstop_config.ini'), "[General]`ntarget_assembly = BepInEx\core\BepInEx.Unity.IL2CPP.dll`n")
    [IO.File]::WriteAllText((Join-Path $root 'BepInEx\core\BepInEx.Unity.IL2CPP.dll'), 'core')
    [IO.File]::WriteAllText((Join-Path $root 'winhttp.dll'), 'startup hook')
    [IO.File]::WriteAllText((Join-Path $plugin 'CustomCourseLoader.dll'), 'loader')
    [IO.File]::WriteAllText((Join-Path $plugin 'Courses\course.json'), 'course')
    [IO.File]::WriteAllText((Join-Path $plugin 'Maps\map.bundle'), 'map')
    [IO.File]::WriteAllText((Join-Path $other 'AimElevationMod.dll'), 'other mod')
    Run $toggle 'Status' $true
    Run $toggle 'Disable' $true
    Assert (-not (Test-Path (Join-Path $root 'winhttp.dll'))) 'Startup hook still present'
    Assert ((Get-Content (Join-Path $root 'winhttp.dll.course-studio-disabled') -Raw) -eq 'startup hook') 'Startup hook not preserved'
    Assert ((Get-Content (Join-Path $plugin 'CustomCourseLoader.dll') -Raw) -eq 'loader') 'Loader changed'
    Assert ((Get-Content (Join-Path $other 'AimElevationMod.dll') -Raw) -eq 'other mod') 'Other mod changed'
    Assert ((Get-Content (Join-Path $plugin 'Courses\course.json') -Raw) -eq 'course') 'Course changed'
    Assert ((Get-Content (Join-Path $plugin 'Maps\map.bundle') -Raw) -eq 'map') 'Map changed'
    Run $toggle 'Disable' $true
    Run $toggle 'Enable' $true
    Assert ((Get-Content (Join-Path $root 'winhttp.dll') -Raw) -eq 'startup hook') 'Startup hook not restored'
    Run $toggle 'Enable' $true
    Run $legacy 'Disable' $true
    Run $toggle 'Disable' $true
    Run $toggle 'Enable' $true
    Assert (Test-Path (Join-Path $plugin 'CustomCourseLoader.dll')) 'Older toggle was not restored'
    Assert (-not (Test-Path (Join-Path $plugin 'course-studio-toggle.json'))) 'Older toggle record remains'
    [IO.File]::WriteAllText((Join-Path $root 'winhttp.dll.course-studio-disabled'), 'conflict')
    Run $toggle 'Disable' $false
    Run $toggle 'Enable' $false
    Assert ((Get-Content (Join-Path $root 'winhttp.dll') -Raw) -eq 'startup hook') 'Conflict overwrote hook'
    Assert ((Get-Content (Join-Path $root 'winhttp.dll.course-studio-disabled') -Raw) -eq 'conflict') 'Conflict overwrote disabled hook'
    Remove-Item -LiteralPath (Join-Path $root 'winhttp.dll.course-studio-disabled')
    [IO.File]::WriteAllText((Join-Path $root 'doorstop_config.ini'), "[General]`ntarget_assembly = Other\Some.dll`n")
    Run $toggle 'Disable' $false
    Assert (Test-Path (Join-Path $root 'winhttp.dll')) 'Unrecognized setup was changed'
    Write-Output 'All-mod toggle fixture passed: status, disable/enable, older toggle recovery, conflict refusal, setup verification and data preservation.'
} finally {
    $prefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (-not ([IO.Path]::GetFullPath($root)).StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or -not ([IO.Path]::GetFileName($root)).StartsWith('DgmAllModsTest-')) { throw 'Refusing to clean an unexpected test path.' }
    if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
}
