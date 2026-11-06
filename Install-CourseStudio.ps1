[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$GameRoot,

    [switch]$AllowDraft
)

$ErrorActionPreference = 'Stop'

function Resolve-ChildPath([string]$Root, [string]$RelativePath, [string]$Label) {
    $rootFull = [IO.Path]::GetFullPath($Root).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    $full = [IO.Path]::GetFullPath((Join-Path $Root $RelativePath))
    if (-not $full.StartsWith($rootFull, [StringComparison]::OrdinalIgnoreCase)) {
        throw "$Label path escapes its allowed root: $RelativePath"
    }
    return $full
}

function Get-RelativePathUnderRoot([string]$Root, [string]$Target, [string]$Label) {
    $rootFull = [IO.Path]::GetFullPath($Root).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    $rootPrefix = $rootFull + [IO.Path]::DirectorySeparatorChar
    $targetFull = [IO.Path]::GetFullPath($Target)
    if (-not $targetFull.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "$Label path is outside its allowed root: $Target"
    }
    return $targetFull.Substring($rootPrefix.Length)
}

$packageRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$gameRootFull = (Resolve-Path -LiteralPath $GameRoot).Path
$packagePrefix = $packageRoot.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
$gamePrefix = $gameRootFull.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if ($packageRoot.Equals($gameRootFull, [StringComparison]::OrdinalIgnoreCase) -or
    $packageRoot.StartsWith($gamePrefix, [StringComparison]::OrdinalIgnoreCase) -or
    $gameRootFull.StartsWith($packagePrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Extract the release archive to a separate folder outside the game installation before running this installer.'
}

$manifestPath = Resolve-ChildPath $packageRoot 'package-manifest.json' 'Package manifest'
$manifest = if (Test-Path -LiteralPath $manifestPath -PathType Leaf) {
    Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
} else {
    throw 'package-manifest.json is missing. Run this installer from the extracted Course Studio release folder.'
}

if ($manifest.draft -and -not $AllowDraft) {
    throw "This Course Studio archive is marked DRAFT. Pass -AllowDraft only when intentionally installing a local review build."
}
if ([string]::IsNullOrWhiteSpace([string]$manifest.pluginVersion)) {
    throw 'The package manifest does not contain a plugin version.'
}

$requiredGameFiles = @(
    'BepInEx\core\BepInEx.Core.dll',
    'BepInEx\core\BepInEx.Unity.IL2CPP.dll'
)
foreach ($relativePath in $requiredGameFiles) {
    $path = Resolve-ChildPath $gameRootFull $relativePath 'Game dependency'
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "BepInEx 6 IL2CPP was not found at '$path'. Install BepInEx for this game before installing Course Studio."
    }
}

$activeGame = @(Get-Process -Name 'Disc Golf Masters' -ErrorAction SilentlyContinue)
if ($activeGame.Count -gt 0) {
    throw 'Close Disc Golf Masters before installing or updating Course Studio.'
}

$sourcePluginRoot = Resolve-ChildPath $packageRoot 'BepInEx\plugins\CustomCourseLoader' 'Package plugin'
$sourceDll = Resolve-ChildPath $sourcePluginRoot 'CustomCourseLoader.dll' 'Plugin DLL'
$sourceEditorRoot = Resolve-ChildPath $sourcePluginRoot 'CourseEditor' 'Course Editor'
$sourceSampleCourse = Resolve-ChildPath $sourcePluginRoot 'Courses\SampleMeadowNine.json' 'Sample course'
$requiredPackageFiles = @(
    $sourceDll,
    (Resolve-ChildPath $sourceEditorRoot 'index.html' 'Course Editor page'),
    (Resolve-ChildPath $sourceEditorRoot 'Launch-Course-Editor.ps1' 'Course Editor launcher'),
    (Resolve-ChildPath $sourceEditorRoot 'catalog.js' 'Catalog placeholder'),
    $sourceSampleCourse,
    (Resolve-ChildPath $packageRoot 'package-manifest.json' 'Package manifest')
)
foreach ($path in $requiredPackageFiles) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "The release payload is incomplete; required file is missing: $path"
    }
}

$destinationPluginRoot = Resolve-ChildPath $gameRootFull 'BepInEx\plugins\CustomCourseLoader' 'Installed plugin'
$destinationEditorRoot = Resolve-ChildPath $destinationPluginRoot 'CourseEditor' 'Installed editor'
$destinationCoursesRoot = Resolve-ChildPath $destinationPluginRoot 'Courses' 'Installed courses'
$destinationMapsRoot = Resolve-ChildPath $destinationPluginRoot 'Maps' 'Installed maps'
$destinationCatalog = Resolve-ChildPath $destinationEditorRoot 'catalog.js' 'Generated course catalog'
$destinationMapPreviews = Resolve-ChildPath $destinationEditorRoot 'assets\maps' 'Generated map previews'

function Assert-NoReparsePoints([string]$Root, [string]$Target) {
    $rootFull = [IO.Path]::GetFullPath($Root).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    $relative = Get-RelativePathUnderRoot $rootFull $Target 'Install target'
    $current = $rootFull
    foreach ($part in $relative.Split([char[]]@([IO.Path]::DirectorySeparatorChar), [StringSplitOptions]::RemoveEmptyEntries)) {
        $current = Join-Path $current $part
        if (Test-Path -LiteralPath $current) {
            $item = Get-Item -LiteralPath $current -Force
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Refusing to install through a linked game-data path: $current"
            }
        }
    }
}

foreach ($path in @($destinationPluginRoot, $destinationEditorRoot, $destinationCoursesRoot, $destinationMapsRoot, $destinationCatalog, $destinationMapPreviews)) {
    Assert-NoReparsePoints $gameRootFull $path
}

New-Item -ItemType Directory -Force -Path $destinationPluginRoot, $destinationEditorRoot, $destinationCoursesRoot, $destinationMapsRoot, $destinationMapPreviews | Out-Null

Copy-Item -LiteralPath $sourceDll -Destination (Resolve-ChildPath $destinationPluginRoot 'CustomCourseLoader.dll' 'Installed plugin DLL') -Force
$editorPrefix = [IO.Path]::GetFullPath($sourceEditorRoot).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
$editorFilesCopied = 0
foreach ($file in Get-ChildItem -LiteralPath $sourceEditorRoot -File -Recurse) {
    $relative = Get-RelativePathUnderRoot $sourceEditorRoot $file.FullName 'Course Editor source'
    if ($relative -ieq 'catalog.js' -or $relative -match '^assets[\\/]maps([\\/]|$)') { continue }

    $sourceFile = Resolve-ChildPath $sourceEditorRoot $relative 'Course Editor source'
    $destinationFile = Resolve-ChildPath $destinationEditorRoot $relative 'Course Editor destination'
    if (-not $sourceFile.StartsWith($editorPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Course Editor source escaped its package directory: $relative"
    }
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destinationFile) | Out-Null
    Copy-Item -LiteralPath $sourceFile -Destination $destinationFile -Force
    $editorFilesCopied++
}

if (-not (Test-Path -LiteralPath $destinationCatalog -PathType Leaf)) {
    $sourceCatalog = Resolve-ChildPath $sourceEditorRoot 'catalog.js' 'Catalog placeholder'
    Copy-Item -LiteralPath $sourceCatalog -Destination $destinationCatalog
}

$destinationSampleCourse = Resolve-ChildPath $destinationCoursesRoot 'SampleMeadowNine.json' 'Installed sample course'
if (-not (Test-Path -LiteralPath $destinationSampleCourse -PathType Leaf)) {
    Copy-Item -LiteralPath $sourceSampleCourse -Destination $destinationSampleCourse
}

Write-Host "Installed Course Studio $($manifest.pluginVersion) to $gameRootFull."
Write-Host "Updated $editorFilesCopied editor files; preserved existing course, map, card, generated catalog, map preview, and personal-best data."
if ($manifest.draft) { Write-Warning 'This is a local DRAFT build; do not redistribute it as a release.' }
