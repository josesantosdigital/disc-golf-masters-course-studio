[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$ArchivePath,
    [Parameter(Mandatory=$true)][string]$BepInExArchive,
    [Parameter(Mandatory=$true)][string]$NodeBinary
)
$ErrorActionPreference = 'Stop'
$root = Join-Path ([IO.Path]::GetTempPath()) ('DgmOneClickTest-' + [Guid]::NewGuid().ToString('N'))
$game = Join-Path $root 'Game'
$package = Join-Path $root 'Package'
function Assert-Test([bool]$condition, [string]$message) { if (-not $condition) { throw "One-click setup test failed: $message" } }
try {
    New-Item -ItemType Directory -Path $game -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $game 'Disc Golf Masters.exe'), 'fixture')
    Expand-Archive -LiteralPath (Resolve-Path -LiteralPath $ArchivePath).Path -DestinationPath $package
    $setup = Join-Path $package 'Setup-CourseStudio.ps1'
    & $setup -GameRoot $game -PackageRoot $package -AllowDraft -NoShortcut -BepInExArchive $BepInExArchive -NodeBinary $NodeBinary
    $plugin = Join-Path $game 'BepInEx\plugins\CustomCourseLoader'
    Assert-Test (Test-Path -LiteralPath (Join-Path $game 'winhttp.dll')) 'BepInEx bootstrap was not installed'
    Assert-Test (Test-Path -LiteralPath (Join-Path $game 'BepInEx\core\BepInEx.Unity.IL2CPP.dll')) 'BepInEx IL2CPP was not installed'
    Assert-Test (Test-Path -LiteralPath (Join-Path $plugin 'CustomCourseLoader.dll')) 'map loader was not installed'
    Assert-Test (Test-Path -LiteralPath (Join-Path $plugin 'CourseEditor\runtime\node.exe')) 'portable runtime was not installed'
    $course = Join-Path $plugin 'Courses\player-course.json'
    $map = Join-Path $plugin 'Maps\keep\scene.bundle'
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $map) | Out-Null
    [IO.File]::WriteAllText($course, 'preserve-course')
    [IO.File]::WriteAllText($map, 'preserve-map')
    $oldCore = (Get-FileHash -LiteralPath (Join-Path $game 'BepInEx\core\BepInEx.Core.dll')).Hash
    & $setup -GameRoot $game -PackageRoot $package -AllowDraft -NoShortcut
    Assert-Test ((Get-Content -LiteralPath $course -Raw) -eq 'preserve-course') 'player course was changed'
    Assert-Test ((Get-Content -LiteralPath $map -Raw) -eq 'preserve-map') 'player map was changed'
    Assert-Test ((Get-FileHash -LiteralPath (Join-Path $game 'BepInEx\core\BepInEx.Core.dll')).Hash -eq $oldCore) 'existing BepInEx was changed'
    $badGame = Join-Path $root 'ConflictingGame'
    New-Item -ItemType Directory -Path $badGame | Out-Null
    [IO.File]::WriteAllText((Join-Path $badGame 'Disc Golf Masters.exe'), 'fixture')
    [IO.File]::WriteAllText((Join-Path $badGame 'winhttp.dll'), 'existing')
    $rejected = $false
    try { & $setup -GameRoot $badGame -PackageRoot $package -AllowDraft -NoShortcut -BepInExArchive $BepInExArchive }
    catch { $rejected = $_.Exception.Message -match 'overwritten' }
    Assert-Test $rejected 'setup did not reject an existing winhttp.dll'
    Assert-Test ((Get-Content -LiteralPath (Join-Path $badGame 'winhttp.dll') -Raw) -eq 'existing') 'conflicting file was changed'
    Write-Host 'One-click setup fixture passed: fresh install, portable Node, update preservation, and conflicting-file refusal. No game was run.'
} finally {
    $tempPrefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    $full = [IO.Path]::GetFullPath($root)
    if (-not $full.StartsWith($tempPrefix, [StringComparison]::OrdinalIgnoreCase) -or -not [IO.Path]::GetFileName($full).StartsWith('DgmOneClickTest-')) { throw 'Refusing to clean an unexpected test path.' }
    if (Test-Path -LiteralPath $full) { Remove-Item -LiteralPath $full -Recurse -Force }
}
