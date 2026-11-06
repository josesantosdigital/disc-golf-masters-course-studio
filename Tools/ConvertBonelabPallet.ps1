[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ArchivePath,
    [Parameter(Mandatory = $true)][string]$SceneName,
    [Parameter(Mandatory = $true)][ValidatePattern('^[A-Za-z0-9_-]{1,48}$')][string]$Id,
    [Parameter(Mandatory = $true)][ValidateLength(1, 64)][string]$CourseName,
    [ValidateSet('CanonPark', 'MinokaSprings', 'BaldurDgc', 'MapleHill', 'ChampionsLanding', 'WinthropGold', 'NokiaDiscGolfPark', 'Terminalen')][string]$BaseCourse = 'CanonPark',
    [ValidateRange(1, 18)][int]$HoleCount = 9,
    [string]$SceneBundlePath = '',
    [string[]]$DependencyBundlePaths = @(),
    [ValidateSet('Auto', 'Forest', 'Urban', 'Abstract')][string]$PreviewStyle = 'Auto',
    [Parameter(Mandatory = $true)][string]$PluginDirectory
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
Import-Module (Join-Path $PSScriptRoot 'Modules\CoursePackage.psm1') -Force

function Read-SourceJson([object]$Entry) {
    if ($Entry -is [IO.Compression.ZipArchiveEntry]) {
        $stream = $Entry.Open()
        try {
            $reader = [IO.StreamReader]::new($stream)
            try { return ($reader.ReadToEnd() | ConvertFrom-Json) }
            finally { $reader.Dispose() }
        }
        finally { $stream.Dispose() }
    }

    return (Get-Content -LiteralPath $Entry.FilePath -Raw | ConvertFrom-Json)
}

function Get-RelativeBundlePath([string]$InternalId) {
    $marker = $InternalId.LastIndexOf(':', [StringComparison]::Ordinal)
    if ($marker -lt 0) { throw "Unrecognized pallet bundle reference: $InternalId" }
    $relative = $InternalId.Substring($marker + 2).TrimStart('\', '/').Replace('\', '/')
    if ([string]::IsNullOrWhiteSpace($relative) -or $relative.Split('/') -contains '..') {
        throw "Unsafe pallet bundle reference: $InternalId"
    }
    return $relative
}

function Find-SourceEntry([string]$RelativePath) {
    $normalized = $RelativePath.Replace('\', '/').TrimStart('/')
    $suffix = '/' + $normalized
    $matches = @($sourceEntries | Where-Object {
        $_.FullName.Replace('\', '/').Equals($normalized, [StringComparison]::OrdinalIgnoreCase) -or
        $_.FullName.Replace('\', '/').EndsWith($suffix, [StringComparison]::OrdinalIgnoreCase)
    })
    if ($matches.Count -eq 0) { throw "The selected pallet source doesn't contain bundle '$RelativePath'." }
    if ($matches.Count -gt 1) { throw "Bundle path '$RelativePath' is ambiguous in the selected pallet source." }
    return $matches[0]
}

function Copy-SourceEntry([object]$Entry, [string]$Destination) {
    $parent = Split-Path -Parent $Destination
    New-Item -ItemType Directory -Force -Path $parent | Out-Null
    if ($Entry -isnot [IO.Compression.ZipArchiveEntry]) {
        Copy-Item -LiteralPath $Entry.FilePath -Destination $Destination
        return
    }

    $source = $Entry.Open()
    try {
        $target = [IO.File]::Open($Destination, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
        try { $source.CopyTo($target) }
        finally { $target.Dispose() }
    }
    finally { $source.Dispose() }
}

function Get-CanonicalName([string]$Value) {
    return [regex]::Replace($Value.ToLowerInvariant(), '[^a-z0-9]', '')
}

function Select-One([object[]]$Items, [string]$Description) {
    if ($Items.Count -eq 0) { return $null }
    if ($Items.Count -gt 1) { throw "$Description is ambiguous. Pass -SceneBundlePath to choose the exact bundle." }
    return $Items[0]
}

$sourcePath = (Resolve-Path -LiteralPath $ArchivePath).Path
$archive = $null
if (Test-Path -LiteralPath $sourcePath -PathType Container) {
    $sourceRoot = [IO.Path]::GetFullPath($sourcePath).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    if ((Get-Item -LiteralPath $sourceRoot).Attributes -band [IO.FileAttributes]::ReparsePoint) {
        throw 'A linked or reparse-point pallet folder is not supported.'
    }
    $sourcePrefix = $sourceRoot + [IO.Path]::DirectorySeparatorChar
    $sourceEntries = @(Get-ChildItem -LiteralPath $sourceRoot -File -Recurse | ForEach-Object {
        if ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) {
            throw "A linked or reparse-point file is not supported in the pallet folder: $($_.FullName)"
        }
        $fullPath = [IO.Path]::GetFullPath($_.FullName)
        if (-not $fullPath.StartsWith($sourcePrefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Pallet file escaped its selected folder: $($_.FullName)"
        }
        [pscustomobject]@{
            FullName = $fullPath.Substring($sourcePrefix.Length).Replace('\', '/')
            FilePath = $fullPath
        }
    })
}
else {
    $archive = [IO.Compression.ZipFile]::OpenRead($sourcePath)
    $sourceEntries = @($archive.Entries)
}
$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ('dgm-bonelab-' + [Guid]::NewGuid().ToString('N'))
try {
    $catalogEntries = @($sourceEntries | Where-Object { $_.FullName.Replace('\', '/') -match '(^|/)catalog_.*\.json$' })
    $palletEntries = @($sourceEntries | Where-Object { $_.FullName.Replace('\', '/') -match '(^|/)\.?.+\.pallet\.json$' })
    if ($catalogEntries.Count -ne 1 -or $palletEntries.Count -ne 1) {
        throw 'Expected one BONELAB PC catalog_*.json and one *.pallet.json in the selected pallet source.'
    }

    $catalog = Read-SourceJson $catalogEntries[0]
    $null = Read-SourceJson $palletEntries[0]
    $internalIds = @($catalog.m_InternalIds | Where-Object { $_ -is [string] })
    if ($internalIds.Count -eq 0) { throw 'BONELAB catalog has no m_InternalIds bundle index.' }

    $scenes = @($internalIds | Where-Object { $_.EndsWith('.unity', [StringComparison]::OrdinalIgnoreCase) })
    $sceneMatches = @($scenes | Where-Object {
        [IO.Path]::GetFileNameWithoutExtension($_).Equals($SceneName, [StringComparison]::OrdinalIgnoreCase)
    })
    $scenePath = Select-One $sceneMatches "Scene '$SceneName'"
    if ([string]::IsNullOrWhiteSpace($scenePath)) {
        $available = @($scenes | ForEach-Object { [IO.Path]::GetFileNameWithoutExtension($_) } | Sort-Object -Unique) -join ', '
        throw "Scene '$SceneName' wasn't found. Available scenes: $available"
    }

    $sceneBundles = @($internalIds | Where-Object {
        try { (Get-RelativeBundlePath $_) -match '(?i)_levels_scenes_level/.+\.bundle$' }
        catch { $false }
    })
    $sceneSlug = Get-CanonicalName ([IO.Path]::GetFileNameWithoutExtension($scenePath))
    $selectedBundleId = $null
    if (-not [string]::IsNullOrWhiteSpace($SceneBundlePath)) {
        $requestedBundlePath = $SceneBundlePath.Replace('\', '/').TrimStart('/')
        $bundleMatches = @($sceneBundles | Where-Object {
            $relative = Get-RelativeBundlePath $_
            $relative.Equals($requestedBundlePath, [StringComparison]::OrdinalIgnoreCase) -or
            $relative.EndsWith('/' + $requestedBundlePath, [StringComparison]::OrdinalIgnoreCase) -or
            [IO.Path]::GetFileName($relative).Equals($requestedBundlePath, [StringComparison]::OrdinalIgnoreCase)
        })
        $selectedBundleId = Select-One $bundleMatches "Scene bundle '$SceneBundlePath'"
        if ($null -eq $selectedBundleId) { throw "Scene bundle '$SceneBundlePath' wasn't found in the pallet." }
    }
    else {
        $exactMatches = @($sceneBundles | Where-Object {
            (Get-CanonicalName ([IO.Path]::GetFileNameWithoutExtension((Get-RelativeBundlePath $_)))) -eq $sceneSlug
        })
        $selectedBundleId = Select-One $exactMatches "Scene bundle for '$SceneName'"
        if ($null -eq $selectedBundleId) {
            $baseSlug = $sceneSlug -replace '(day|night|sunset|sunrise|dawn|dusk|evening)$', ''
            $baseMatches = @($sceneBundles | Where-Object {
                (Get-CanonicalName ([IO.Path]::GetFileNameWithoutExtension((Get-RelativeBundlePath $_)))) -eq $baseSlug
            })
            $selectedBundleId = Select-One $baseMatches "Scene bundle for '$SceneName'"
        }
        if ($null -eq $selectedBundleId) { throw "Couldn't associate scene '$SceneName' with a scene bundle. Pass -SceneBundlePath." }
    }

    $selectedEntry = Find-SourceEntry (Get-RelativeBundlePath $selectedBundleId)
    $dependencyIds = [System.Collections.Generic.List[string]]::new()
    foreach ($dependencyId in @($internalIds | Where-Object { $_ -match '(?i)_(monoscripts|unitybuiltinshaders)\.bundle$' })) {
        if (-not $dependencyIds.Contains($dependencyId)) { $dependencyIds.Add($dependencyId) }
    }
    foreach ($requestedDependency in $DependencyBundlePaths) {
        $requestedPath = $requestedDependency.Replace('\', '/').TrimStart('/')
        $dependencyMatches = @($internalIds | Where-Object {
            $_.EndsWith('.bundle', [StringComparison]::OrdinalIgnoreCase) -and
            (Get-RelativeBundlePath $_).EndsWith($requestedPath, [StringComparison]::OrdinalIgnoreCase)
        })
        $dependencyId = Select-One $dependencyMatches "Dependency bundle '$requestedDependency'"
        if ($null -eq $dependencyId) { throw "Dependency bundle '$requestedDependency' wasn't found in the pallet." }
        if (-not $dependencyIds.Contains($dependencyId)) { $dependencyIds.Add($dependencyId) }
    }

    New-Item -ItemType Directory -Force -Path $tempRoot | Out-Null
    $sceneStagePath = Join-Path $tempRoot 'scene.bundle'
    Copy-SourceEntry $selectedEntry $sceneStagePath
    $dependencyFiles = [System.Collections.Generic.List[string]]::new()
    foreach ($dependencyId in $dependencyIds) {
        $relative = Get-RelativeBundlePath $dependencyId
        $dependencyEntry = Find-SourceEntry $relative
        $dependencyStagePath = Join-Path $tempRoot ([IO.Path]::GetFileName($relative))
        Copy-SourceEntry $dependencyEntry $dependencyStagePath
        $dependencyFiles.Add($dependencyStagePath)
    }

    $result = New-DgmCoursePackage `
        -SceneBundlePath $sceneStagePath `
        -ScenePath $scenePath `
        -Id $Id `
        -CourseName $CourseName `
        -BaseCourse $BaseCourse `
        -HoleCount $HoleCount `
        -DependencyBundlePaths $dependencyFiles.ToArray() `
        -PluginDirectory $PluginDirectory `
        -PreviewStyle $PreviewStyle

    Write-Host "Converted scene '$SceneName' to $($result.MapPath)"
    Write-Host "Course definition: $($result.CoursePath)"
    Write-Host "Course preview card: $($result.CourseCardPath)"
    Write-Host "Source Unity version: $($result.UnityVersion)"
    Write-Warning 'This tool extracts and packages the original BONELAB scene bundle; it does not convert its engine version or course layout data.'
}
finally {
    if ($null -ne $archive) { $archive.Dispose() }
    $tempRootFullPath = [IO.Path]::GetFullPath($tempRoot)
    $tempBasePath = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if ($tempRootFullPath.StartsWith($tempBasePath, [StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $tempRootFullPath)) {
        Remove-Item -LiteralPath $tempRootFullPath -Recurse -Force
    }
}
