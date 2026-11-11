$ErrorActionPreference = 'Stop'
$toggle = Join-Path $PSScriptRoot 'Toggle-CourseStudio.ps1'
$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ('DgmToggleTest-' + [guid]::NewGuid().ToString('N'))
$plugin = Join-Path $tempRoot 'BepInEx\plugins\CustomCourseLoader'
$backup = Join-Path $plugin 'backups\old'
$other = Join-Path $tempRoot 'BepInEx\plugins\AimElevationMod'
function Assert([bool]$condition, [string]$message) { if (-not $condition) { throw $message } }
function Run([string]$mode, [bool]$expectSuccess) {
    $previousPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $output = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $toggle -GameRoot $tempRoot -Action $mode 2>&1 | Out-String
        $exitCode = $LASTEXITCODE
    } finally { $ErrorActionPreference = $previousPreference }
    if (($exitCode -eq 0) -ne $expectSuccess) { throw "$mode unexpected result ($exitCode): $output" }
}
try {
    New-Item -ItemType Directory -Force -Path $backup,$other,(Join-Path $plugin 'Courses'),(Join-Path $plugin 'Maps') | Out-Null
    [IO.File]::WriteAllText((Join-Path $tempRoot 'Disc Golf Masters.exe'), 'fixture')
    [IO.File]::WriteAllText((Join-Path $plugin 'CustomCourseLoader.dll'), 'main')
    [IO.File]::WriteAllText((Join-Path $backup 'CustomCourseLoader-before.dll'), 'old')
    [IO.File]::WriteAllText((Join-Path $backup 'CustomCourseLoader-preexisting.dll.disabled'), 'already disabled')
    [IO.File]::WriteAllText((Join-Path $plugin 'Courses\course.json'), 'course')
    [IO.File]::WriteAllText((Join-Path $plugin 'Maps\map.bundle'), 'map')
    [IO.File]::WriteAllText((Join-Path $other 'AimElevationMod.dll'), 'other mod')
    Run 'Status' $true
    Run 'Disable' $true
    Assert (-not (Test-Path (Join-Path $plugin 'CustomCourseLoader.dll'))) 'Main loader still enabled'
    Assert (-not (Test-Path (Join-Path $backup 'CustomCourseLoader-before.dll'))) 'Nested backup still enabled'
    Assert (Test-Path (Join-Path $backup 'CustomCourseLoader-before.dll.disabled')) 'Nested backup was not preserved'
    Assert ((Get-Content (Join-Path $plugin 'Courses\course.json') -Raw) -eq 'course') 'Course changed'
    Assert ((Get-Content (Join-Path $plugin 'Maps\map.bundle') -Raw) -eq 'map') 'Map changed'
    Assert ((Get-Content (Join-Path $other 'AimElevationMod.dll') -Raw) -eq 'other mod') 'Other mod changed'
    Run 'Disable' $true
    Run 'Enable' $true
    Assert ((Get-Content (Join-Path $plugin 'CustomCourseLoader.dll') -Raw) -eq 'main') 'Main loader not restored'
    Assert ((Get-Content (Join-Path $backup 'CustomCourseLoader-before.dll') -Raw) -eq 'old') 'Nested backup not restored'
    Assert (Test-Path (Join-Path $backup 'CustomCourseLoader-preexisting.dll.disabled')) 'Previously disabled backup was enabled'
    Assert (-not (Test-Path (Join-Path $plugin 'course-studio-toggle.json'))) 'Toggle record not cleared'
    Run 'Enable' $true
    Run 'Disable' $true
    Move-Item -LiteralPath (Join-Path $backup 'CustomCourseLoader-before.dll.disabled') -Destination (Join-Path $backup 'CustomCourseLoader-before.dll')
    Run 'Enable' $true
    Assert (Test-Path (Join-Path $plugin 'CustomCourseLoader.dll')) 'Partial recovery did not restore main loader'
    Run 'Disable' $true
    [IO.File]::WriteAllText((Join-Path $plugin 'CustomCourseLoader.dll'), 'new installer file')
    Run 'Enable' $false
    Assert ((Get-Content (Join-Path $plugin 'CustomCourseLoader.dll') -Raw) -eq 'new installer file') 'Enable overwrote an update'
    Assert ((Get-Content (Join-Path $plugin 'CustomCourseLoader.dll.disabled') -Raw) -eq 'main') 'Enable overwrote a disabled loader'
    Remove-Item -LiteralPath (Join-Path $plugin 'CustomCourseLoader.dll')
    Run 'Enable' $true
    [IO.File]::WriteAllText((Join-Path $plugin 'CustomCourseLoader.dll.disabled'), 'conflict')
    Run 'Disable' $false
    Assert ((Get-Content (Join-Path $plugin 'CustomCourseLoader.dll') -Raw) -eq 'main') 'Conflict overwrote loader'
    Remove-Item -LiteralPath (Join-Path $plugin 'CustomCourseLoader.dll.disabled')
    [IO.File]::WriteAllText((Join-Path $other 'CustomCourseLoader.dll'), 'unexpected duplicate')
    Run 'Disable' $false
    Assert (Test-Path (Join-Path $plugin 'CustomCourseLoader.dll')) 'External duplicate changed the loader'
    Write-Output 'Toggle fixture passed: status, enable/disable, nested backups, pre-disabled files, conflicts, duplicate detection, and user data preservation.'
} finally {
    $prefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (-not ([IO.Path]::GetFullPath($tempRoot)).StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or -not ([IO.Path]::GetFileName($tempRoot)).StartsWith('DgmToggleTest-')) { throw 'Refusing to clean an unexpected test path.' }
    if (Test-Path -LiteralPath $tempRoot) { Remove-Item -LiteralPath $tempRoot -Recurse -Force }
}
