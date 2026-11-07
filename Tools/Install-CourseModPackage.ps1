[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$GameRoot,
    [switch]$Overwrite
)

$ErrorActionPreference = 'Stop'
$packageRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$gameRootFull = [IO.Path]::GetFullPath((Resolve-Path -LiteralPath $GameRoot).Path).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
$pluginRoot = [IO.Path]::GetFullPath((Join-Path $gameRootFull 'BepInEx\plugins\CustomCourseLoader')).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
$manifestPath = Join-Path $packageRoot 'manifest.json'
$payloadRoot = Join-Path $packageRoot 'Payload'
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf) -or -not (Test-Path -LiteralPath $payloadRoot -PathType Container)) {
    throw 'Extract the course ZIP first, then run its Install-CourseModPackage.ps1 from the extracted folder.'
}
if (-not (Test-Path -LiteralPath $pluginRoot -PathType Container)) {
    throw 'Course Studio is not installed in this game folder. Install Course Studio before installing a course package.'
}
foreach ($managedRoot in @(
    (Join-Path $gameRootFull 'BepInEx'),
    (Join-Path $gameRootFull 'BepInEx\plugins'),
    $pluginRoot
)) {
    if ((Test-Path -LiteralPath $managedRoot) -and ((Get-Item -LiteralPath $managedRoot -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw "Refusing to install through a linked game-data directory: $managedRoot"
    }
}
foreach ($dependency in @('BepInEx\core\BepInEx.Core.dll', 'BepInEx\core\BepInEx.Unity.IL2CPP.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $gameRootFull $dependency) -PathType Leaf)) {
        throw "BepInEx 6 IL2CPP is missing from the game folder: $dependency"
    }
}
if (@(Get-Process -Name 'Disc Golf Masters' -ErrorAction SilentlyContinue).Count -gt 0) {
    throw 'Close Disc Golf Masters before installing a course package.'
}

try { $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json }
catch { throw "Could not read course package manifest: $($_.Exception.Message)" }
if ($manifest.schemaVersion -ne 1 -or $manifest.packageType -cne 'DiscGolfMastersCourse') { throw 'Unsupported course package manifest.' }
if ($manifest.id -isnot [string] -or $manifest.id -notmatch '^[A-Za-z0-9_-]{1,48}$') { throw 'Course package ID is invalid.' }
if ($manifest.version -isnot [string] -or $manifest.version -notmatch '^\d+\.\d+\.\d+$') { throw 'Course package version is invalid.' }
if ($manifest.files -isnot [array] -or $manifest.files.Count -lt 1) { throw 'Course package contains no payload files.' }
if ($manifest.importedMap -and [string]::IsNullOrWhiteSpace([string]$manifest.assetPermissionReference)) {
    throw 'Imported map package has no recorded permission reference; installation was refused.'
}

function Get-SafeCourseRelativePath([string]$Value) {
    if ([string]::IsNullOrWhiteSpace($Value) -or $Value.Contains('\') -or $Value.StartsWith('/') -or
        [IO.Path]::IsPathRooted($Value) -or $Value.Split('/') -contains '' -or
        $Value.Split('/') -contains '.' -or $Value.Split('/') -contains '..') {
        throw "Unsafe course package path: $Value"
    }
    $allowedCourse = $Value.StartsWith('Courses/', [StringComparison]::OrdinalIgnoreCase) -and
        [IO.Path]::GetExtension($Value).ToLowerInvariant() -in @('.json', '.png')
    $allowedMap = $Value.StartsWith('Maps/', [StringComparison]::OrdinalIgnoreCase) -and
        [IO.Path]::GetExtension($Value).ToLowerInvariant() -in @('.bundle', '.unity3d', '.assetbundle')
    if (-not $allowedCourse -and -not $allowedMap) { throw "Package contains a file type or folder the loader does not accept: $Value" }
    return $Value
}

$manifestFiles = [System.Collections.Generic.Dictionary[string, object]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($item in $manifest.files) {
    $relative = Get-SafeCourseRelativePath ([string]$item.path)
    if ($item.sha256 -isnot [string] -or $item.sha256 -notmatch '^[A-Fa-f0-9]{64}$') { throw "Invalid SHA-256 value for $relative." }
    if ($manifestFiles.ContainsKey($relative)) { throw "Duplicate case-insensitive package path: $relative" }
    $manifestFiles.Add($relative, [string]$item.sha256)
}
$courseJsonPaths = @($manifestFiles.Keys | Where-Object { $_.StartsWith('Courses/', [StringComparison]::OrdinalIgnoreCase) -and [IO.Path]::GetExtension($_) -ieq '.json' })
if ($courseJsonPaths.Count -ne 1) { throw 'A course package must include exactly one course JSON file.' }
if ([IO.Path]::GetFileNameWithoutExtension($courseJsonPaths[0]) -ine $manifest.id) { throw 'The course JSON file name must match the package ID.' }
if ([bool]$manifest.importedMap -ne (@($manifestFiles.Keys | Where-Object { $_.StartsWith('Maps/', [StringComparison]::OrdinalIgnoreCase) }).Count -gt 0)) {
    throw 'The manifest imported-map flag does not match its bundle files.'
}

$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) ('dgm-course-install-' + [Guid]::NewGuid().ToString('N'))
$stagedPluginRoot = Join-Path $temporaryRoot 'CustomCourseLoader'
$backupRoot = Join-Path $temporaryRoot 'Backup'
$installedTargets = [System.Collections.Generic.List[string]]::new()
$backups = [System.Collections.Generic.Dictionary[string, string]]::new([StringComparer]::OrdinalIgnoreCase)
try {
    New-Item -ItemType Directory -Force -Path $stagedPluginRoot | Out-Null
    $payloadBase = [IO.Path]::GetFullPath($payloadRoot).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    $payloadPrefix = $payloadBase + [IO.Path]::DirectorySeparatorChar
    foreach ($relative in ($manifestFiles.Keys | Sort-Object)) {
        $source = [IO.Path]::GetFullPath((Join-Path $payloadRoot ($relative.Replace('/', [IO.Path]::DirectorySeparatorChar))))
        if (-not $source.StartsWith($payloadPrefix, [StringComparison]::OrdinalIgnoreCase) -or -not (Test-Path -LiteralPath $source -PathType Leaf)) {
            throw "Package payload file is missing or escapes its folder: $relative"
        }
        if ((Get-Item -LiteralPath $source -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Linked files are not allowed in course packages: $relative" }
        $actualHash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
        if (-not [string]::Equals($actualHash, [string]$manifestFiles[$relative], [StringComparison]::OrdinalIgnoreCase)) {
            throw "SHA-256 mismatch for course package file: $relative"
        }
        $staged = Join-Path $stagedPluginRoot ($relative.Replace('/', [IO.Path]::DirectorySeparatorChar))
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $staged) | Out-Null
        Copy-Item -LiteralPath $source -Destination $staged
    }

    $coursePath = Join-Path $stagedPluginRoot ($courseJsonPaths[0].Replace('/', [IO.Path]::DirectorySeparatorChar))
    $course = Get-Content -LiteralPath $coursePath -Raw | ConvertFrom-Json
    if ($course.id -isnot [string] -or $course.id -ine $manifest.id) { throw 'Course JSON ID does not match the package manifest.' }
    if ($course.name -isnot [string] -or $course.name -cne [string]$manifest.name) { throw 'Course JSON name does not match the package manifest.' }
    if ($course.holes -isnot [array] -or $course.holes.Count -lt 1 -or $course.holes.Count -gt 18) { throw 'Course JSON must have between 1 and 18 holes.' }

    $listedPaths = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($relative in $manifestFiles.Keys) { [void]$listedPaths.Add($relative) }
    $imageNames = @($course.courseCardImage, $course.mapPreviewImage) + @($course.holes | ForEach-Object { $_.cardImage })
    foreach ($imageName in ($imageNames | Where-Object { $_ -is [string] -and -not [string]::IsNullOrWhiteSpace($_) } | Sort-Object -Unique)) {
        if ([IO.Path]::GetFileName([string]$imageName) -cne [string]$imageName -or -not $listedPaths.Contains('Courses/' + [string]$imageName)) {
            throw "Course image is missing or unsafe in this package: $imageName"
        }
    }
    if ($course.mapScene) {
        $bundleRefs = @($course.mapScene.bundlePath) + @($course.mapScene.dependencyBundlePaths)
        foreach ($bundlePath in ($bundleRefs | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) } | Sort-Object -Unique)) {
            $relative = ([string]$bundlePath).Replace('\', '/')
            if (-not $listedPaths.Contains($relative)) { throw "Imported scene dependency is missing from the package: $relative" }
            $bundlePathOnDisk = Join-Path $stagedPluginRoot ($relative.Replace('/', [IO.Path]::DirectorySeparatorChar))
            $stream = [IO.File]::OpenRead($bundlePathOnDisk)
            try {
                $header = New-Object byte[] 7
                if ($stream.Read($header, 0, 7) -ne 7 -or [Text.Encoding]::ASCII.GetString($header) -ne 'UnityFS') {
                    throw "Imported dependency does not have a UnityFS header: $relative"
                }
            }
            finally { $stream.Dispose() }
        }
    }

    $existingCourseFiles = Get-ChildItem -LiteralPath (Join-Path $pluginRoot 'Courses') -Filter '*.json' -File -ErrorAction SilentlyContinue
    $otherCourseReferences = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($existingCourseFile in $existingCourseFiles) {
        if ([string]::Equals($existingCourseFile.Name, [IO.Path]::GetFileName($courseJsonPaths[0]), [StringComparison]::OrdinalIgnoreCase)) { continue }
        try { $existingCourse = Get-Content -LiteralPath $existingCourseFile.FullName -Raw | ConvertFrom-Json }
        catch { throw "Could not check existing course IDs because '$($existingCourseFile.Name)' is malformed." }
        if ($existingCourse.id -is [string] -and [string]::Equals($existingCourse.id, $manifest.id, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Course ID '$($manifest.id)' is already used by '$($existingCourseFile.Name)'. Rename one course before installing."
        }
        foreach ($imageName in @($existingCourse.courseCardImage, $existingCourse.mapPreviewImage) + @($existingCourse.holes | ForEach-Object { if ($_) { $_.cardImage } })) {
            if ($imageName -is [string] -and [IO.Path]::GetFileName($imageName) -ceq $imageName) {
                [void]$otherCourseReferences.Add('Courses/' + $imageName)
            }
        }
        if ($existingCourse.mapScene) {
            foreach ($bundleReference in @($existingCourse.mapScene.bundlePath) + @($existingCourse.mapScene.dependencyBundlePaths)) {
                if ($bundleReference -is [string] -and -not [string]::IsNullOrWhiteSpace($bundleReference)) {
                    [void]$otherCourseReferences.Add($bundleReference.Replace('\', '/'))
                }
            }
        }
    }

    $targets = [System.Collections.Generic.List[object]]::new()
    foreach ($relative in ($manifestFiles.Keys | Sort-Object)) {
        $target = [IO.Path]::GetFullPath((Join-Path $pluginRoot ($relative.Replace('/', [IO.Path]::DirectorySeparatorChar))))
        $pluginPrefix = $pluginRoot + [IO.Path]::DirectorySeparatorChar
        if (-not $target.StartsWith($pluginPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw "Install path escapes the plugin folder: $relative" }
        if ((Test-Path -LiteralPath $target) -and -not $Overwrite) { throw "File already exists; nothing was installed: $target (pass -Overwrite to replace package files)." }
        if ((Test-Path -LiteralPath $target) -and $Overwrite -and $otherCourseReferences.Contains($relative)) {
            $existingHash = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash
            $packageHash = (Get-FileHash -LiteralPath (Join-Path $stagedPluginRoot ($relative.Replace('/', [IO.Path]::DirectorySeparatorChar))) -Algorithm SHA256).Hash
            if (-not [string]::Equals($existingHash, $packageHash, [StringComparison]::OrdinalIgnoreCase)) {
                throw "Refusing to replace '$relative' because another installed course references it. Choose a unique bundle or image path."
            }
        }
        $targets.Add([pscustomobject]@{ Relative = $relative; Target = $target; Staged = (Join-Path $stagedPluginRoot ($relative.Replace('/', [IO.Path]::DirectorySeparatorChar))) })
    }

    foreach ($targetInfo in $targets) {
        $current = $pluginRoot
        $relativeTarget = $targetInfo.Target.Substring($pluginRoot.Length).TrimStart([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
        foreach ($part in $relativeTarget.Split([char[]]@([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar), [StringSplitOptions]::RemoveEmptyEntries)) {
            $current = Join-Path $current $part
            if (Test-Path -LiteralPath $current) {
                if ((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Refusing to write through a linked game-data path: $current" }
            }
        }
    }

    New-Item -ItemType Directory -Force -Path $backupRoot | Out-Null
    $backupIndex = 0
    foreach ($targetInfo in $targets) {
        $parent = Split-Path -Parent $targetInfo.Target
        New-Item -ItemType Directory -Force -Path $parent | Out-Null
        if (Test-Path -LiteralPath $targetInfo.Target) {
            $backupPath = Join-Path $backupRoot ([string]$backupIndex)
            Copy-Item -LiteralPath $targetInfo.Target -Destination $backupPath
            $backups.Add($targetInfo.Target, $backupPath)
            $backupIndex++
        }
        $installedTargets.Add($targetInfo.Target)
        Copy-Item -LiteralPath $targetInfo.Staged -Destination $targetInfo.Target -Force
    }
    Write-Host "Installed '$($manifest.name)' ($($manifest.id) $($manifest.version)) into $pluginRoot."
    Write-Host "Required Course Studio version: $($manifest.minimumCourseStudioVersion). Imported map files included: $($manifest.importedMap)."
}
catch {
    for ($index = $installedTargets.Count - 1; $index -ge 0; $index--) {
        $target = $installedTargets[$index]
        if ($backups.ContainsKey($target)) { Copy-Item -LiteralPath $backups[$target] -Destination $target -Force }
        elseif (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Force }
    }
    throw
}
finally {
    $tempBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    $temporaryFull = [IO.Path]::GetFullPath($temporaryRoot)
    if (-not $temporaryFull.StartsWith($tempBase, [StringComparison]::OrdinalIgnoreCase)) { throw "Refusing to clean a temporary path outside the temp directory: $temporaryFull" }
    if (Test-Path -LiteralPath $temporaryFull) { Remove-Item -LiteralPath $temporaryFull -Recurse -Force }
}
