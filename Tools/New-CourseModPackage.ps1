[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$CoursePath,
    [Parameter(Mandatory = $true)][string]$PluginDirectory,
    [Parameter(Mandatory = $true)][string]$OutputPath,
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$PackageVersion = '1.0.0',
    [string]$Author = '',
    [switch]$IncludeImportedAssets,
    [string]$AssetPermissionReference,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$pluginRoot = [IO.Path]::GetFullPath((Resolve-Path -LiteralPath $PluginDirectory).Path).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
$coursesRoot = [IO.Path]::GetFullPath((Join-Path $pluginRoot 'Courses')).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
$courseFullPath = [IO.Path]::GetFullPath((Resolve-Path -LiteralPath $CoursePath).Path)
$coursesPrefix = $coursesRoot + [IO.Path]::DirectorySeparatorChar
if (-not $courseFullPath.StartsWith($coursesPrefix, [StringComparison]::OrdinalIgnoreCase) -or
    [IO.Path]::GetExtension($courseFullPath) -ine '.json') {
    throw 'CoursePath must be a JSON file directly inside the selected plugin Courses folder.'
}
if ((Get-Item -LiteralPath $courseFullPath -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
    throw 'Refusing to package a linked course JSON.'
}

try { $course = Get-Content -LiteralPath $courseFullPath -Raw | ConvertFrom-Json }
catch { throw "Could not parse course JSON: $($_.Exception.Message)" }
if ($course.id -isnot [string] -or $course.id -notmatch '^[A-Za-z0-9_-]{1,48}$') { throw 'Course ID must contain 1-48 ASCII letters, digits, underscores, or hyphens.' }
if ($course.name -isnot [string] -or [string]::IsNullOrWhiteSpace($course.name)) { throw 'Course name is missing.' }
if ($course.holes -isnot [array] -or $course.holes.Count -lt 1 -or $course.holes.Count -gt 18) { throw 'Course must contain between 1 and 18 holes.' }

$imported = $null -ne $course.mapScene
if ($imported -and -not $IncludeImportedAssets) {
    throw 'This course references an imported map. Pass -IncludeImportedAssets and a permission source in -AssetPermissionReference only if you are allowed to redistribute those bundles.'
}
if ($imported -and [string]::IsNullOrWhiteSpace($AssetPermissionReference)) {
    throw 'Imported scene bundles require a permission source. Set -AssetPermissionReference to the source URL or written permission record.'
}
if (-not $imported -and $IncludeImportedAssets) { throw '-IncludeImportedAssets was set, but this course has no imported scene.' }

$packageManifest = Join-Path (Split-Path -Parent $PSScriptRoot) 'package-manifest.json'
$sourcePlugin = Join-Path (Split-Path -Parent $PSScriptRoot) 'Plugin.cs'
$requiredLoaderVersion = ''
if (Test-Path -LiteralPath $packageManifest -PathType Leaf) {
    $packageData = Get-Content -LiteralPath $packageManifest -Raw | ConvertFrom-Json
    $requiredLoaderVersion = [string]$packageData.pluginVersion
}
if (-not $requiredLoaderVersion -and (Test-Path -LiteralPath $sourcePlugin -PathType Leaf)) {
    $pluginVersionMatch = [regex]::Match((Get-Content -LiteralPath $sourcePlugin -Raw), 'PluginVersion\s*=\s*"([^"]+)"')
    if ($pluginVersionMatch.Success) { $requiredLoaderVersion = $pluginVersionMatch.Groups[1].Value }
}
if (-not $requiredLoaderVersion) { throw 'Could not determine the minimum Course Studio version from the source or release package.' }

$files = [System.Collections.Generic.Dictionary[string, object]]::new([StringComparer]::OrdinalIgnoreCase)
function Add-PackageFile([string]$RelativePath, [string]$SourcePath) {
    $normalized = $RelativePath.Replace('\', '/')
    $segments = $normalized.Split('/')
    if ([string]::IsNullOrWhiteSpace($normalized) -or $normalized.StartsWith('/') -or
        $segments -contains '' -or $segments -contains '.' -or $segments -contains '..') {
        throw "Unsafe package path: $RelativePath"
    }
    $fullSource = [IO.Path]::GetFullPath((Resolve-Path -LiteralPath $SourcePath).Path)
    if ((Get-Item -LiteralPath $fullSource -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
        throw "Refusing to package a linked source file: $fullSource"
    }
    if (-not $files.ContainsKey($normalized)) { $files.Add($normalized, $fullSource) }
}

Add-PackageFile ("Courses/" + [string]$course.id + '.json') $courseFullPath
$imageNames = [System.Collections.Generic.List[string]]::new()
foreach ($candidate in @($course.courseCardImage, $course.mapPreviewImage)) {
    if ($candidate -is [string] -and -not [string]::IsNullOrWhiteSpace($candidate)) { $imageNames.Add($candidate) }
}
foreach ($hole in $course.holes) {
    if ($null -ne $hole -and $hole.cardImage -is [string] -and -not [string]::IsNullOrWhiteSpace($hole.cardImage)) { $imageNames.Add($hole.cardImage) }
}
foreach ($imageName in ($imageNames | Sort-Object -Unique)) {
    if ([IO.Path]::GetFileName($imageName) -cne $imageName -or [IO.Path]::GetExtension($imageName) -ine '.png') {
        throw "Course image references must be safe PNG file names: $imageName"
    }
    $imagePath = Join-Path $coursesRoot $imageName
    if (-not (Test-Path -LiteralPath $imagePath -PathType Leaf)) { throw "Course image is missing: $imagePath" }
    Add-PackageFile ("Courses/" + $imageName) $imagePath
}

if ($imported) {
    $scene = $course.mapScene
    $bundlePaths = @($scene.bundlePath) + @($scene.dependencyBundlePaths)
    if ([string]::IsNullOrWhiteSpace([string]$scene.scenePath) -or
        [IO.Path]::IsPathRooted([string]$scene.scenePath) -or
        -not ([string]$scene.scenePath).Replace('\', '/').EndsWith('.unity', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Imported map scenePath must be a relative .unity asset path.'
    }
    foreach ($bundlePath in ($bundlePaths | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) } | Sort-Object -Unique)) {
        $relative = ([string]$bundlePath).Replace('\', '/')
        $segments = $relative.Split('/')
        if (-not $relative.StartsWith('Maps/', [StringComparison]::OrdinalIgnoreCase) -or
            $segments -contains '' -or $segments -contains '.' -or $segments -contains '..' -or
            [IO.Path]::IsPathRooted($relative) -or
            [IO.Path]::GetExtension($relative).ToLowerInvariant() -notin @('.bundle', '.unity3d', '.assetbundle')) {
            throw "Imported bundle path is not a safe supported Maps path: $bundlePath"
        }
        $bundleFile = [IO.Path]::GetFullPath((Join-Path $pluginRoot ($relative.Replace('/', [IO.Path]::DirectorySeparatorChar))))
        $pluginPrefix = $pluginRoot + [IO.Path]::DirectorySeparatorChar
        if (-not $bundleFile.StartsWith($pluginPrefix, [StringComparison]::OrdinalIgnoreCase) -or
            -not (Test-Path -LiteralPath $bundleFile -PathType Leaf)) {
            throw "Imported bundle is missing or outside the plugin folder: $bundlePath"
        }
        $bundleItem = Get-Item -LiteralPath $bundleFile -Force
        if ($bundleItem.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Refusing to package a linked bundle: $bundleFile" }
        $bundleStream = [IO.File]::OpenRead($bundleFile)
        try {
            $header = New-Object byte[] 7
            if ($bundleStream.Read($header, 0, 7) -ne 7 -or [Text.Encoding]::ASCII.GetString($header) -ne 'UnityFS') {
                throw "Imported bundle does not have a UnityFS header: $bundlePath"
            }
        }
        finally { $bundleStream.Dispose() }
        Add-PackageFile $relative $bundleFile
    }
    if (-not $scene.bundlePath -or $bundlePaths.Count -lt 1) { throw 'Imported course does not declare its scene bundle.' }
}

$validator = Join-Path $PSScriptRoot 'ValidateCoursePackage.ps1'
& $validator -CoursePath $courseFullPath -PluginDirectory $pluginRoot

$outputFull = [IO.Path]::GetFullPath($OutputPath)
$outputDirectory = Split-Path -Parent $outputFull
if ([string]::IsNullOrWhiteSpace($outputDirectory)) { throw 'OutputPath must include a parent directory.' }
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
if ((Test-Path -LiteralPath $outputFull) -and -not $Force) { throw "Output already exists: $outputFull (pass -Force to replace it)." }
if ((Test-Path -LiteralPath ($outputFull + '.sha256')) -and -not $Force) { throw "Checksum sidecar already exists: $outputFull.sha256 (pass -Force to replace it)." }
$outputPrefix = $pluginRoot + [IO.Path]::DirectorySeparatorChar
if ($outputFull.StartsWith($outputPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'OutputPath must be outside the live plugin folder.' }

$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) ('dgm-course-mod-' + [Guid]::NewGuid().ToString('N'))
$stagingToken = [Guid]::NewGuid().ToString('N')
$stagedArchive = Join-Path $outputDirectory ('.' + [IO.Path]::GetFileName($outputFull) + '.' + $stagingToken + '.staging.zip')
$stagedSidecar = $stagedArchive + '.sha256'
$payloadRoot = Join-Path $temporaryRoot 'Payload'
$manifestPath = Join-Path $temporaryRoot 'manifest.json'
$installerSource = Join-Path $PSScriptRoot 'Install-CourseModPackage.ps1'
if (-not (Test-Path -LiteralPath $installerSource -PathType Leaf)) { throw 'Install-CourseModPackage.ps1 is missing from Tools.' }
try {
    New-Item -ItemType Directory -Force -Path $payloadRoot | Out-Null
    $manifestFiles = [System.Collections.Generic.List[object]]::new()
    foreach ($relativePath in ($files.Keys | Sort-Object)) {
        $sourcePath = [string]$files[$relativePath]
        $targetPath = Join-Path $payloadRoot ($relativePath.Replace('/', [IO.Path]::DirectorySeparatorChar))
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $targetPath) | Out-Null
        Copy-Item -LiteralPath $sourcePath -Destination $targetPath
        $manifestFiles.Add([ordered]@{ path = $relativePath; sha256 = (Get-FileHash -LiteralPath $targetPath -Algorithm SHA256).Hash.ToLowerInvariant() })
    }

    $manifest = [ordered]@{
        schemaVersion = 1
        packageType = 'DiscGolfMastersCourse'
        id = [string]$course.id
        name = [string]$course.name
        version = $PackageVersion
        minimumCourseStudioVersion = $requiredLoaderVersion
        importedMap = [bool]$imported
        assetPermissionReference = if ($imported) { $AssetPermissionReference.Trim() } else { '' }
        files = $manifestFiles.ToArray()
    }
    [IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 12), [Text.UTF8Encoding]::new($false))
    Copy-Item -LiteralPath $installerSource -Destination (Join-Path $temporaryRoot 'Install-CourseModPackage.ps1')
    $readme = @"
# $($course.name)

Disc Golf Masters course package **$($course.id) $PackageVersion**.
Required Course Studio version: $requiredLoaderVersion.
Author: $Author

Close Disc Golf Masters and extract this archive. Install Course Studio $requiredLoaderVersion or newer first. In PowerShell, run:

~~~powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Install-CourseModPackage.ps1 -GameRoot 'C:\Games\Disc Golf Masters'
~~~

Back up custom course data before replacing an existing layout. Existing files are kept unless `-Overwrite` is passed.

Imported map bundles are included only when the package author has recorded a redistribution permission/source reference in the manifest. That reference is an author-provided record, not a legal determination by Course Studio.
"@
    [IO.File]::WriteAllText((Join-Path $temporaryRoot 'README.md'), $readme, [Text.UTF8Encoding]::new($false))

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::CreateFromDirectory($temporaryRoot, $stagedArchive, [IO.Compression.CompressionLevel]::Optimal, $false)
    $hash = (Get-FileHash -LiteralPath $stagedArchive -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText($stagedSidecar, "$hash  $([IO.Path]::GetFileName($outputFull))`n", [Text.UTF8Encoding]::new($false))
    if ((Test-Path -LiteralPath $outputFull) -and -not $Force) { throw "Output appeared while the package was being built: $outputFull (pass -Force to replace it)." }
    Copy-Item -LiteralPath $stagedArchive -Destination $outputFull -Force
    Copy-Item -LiteralPath $stagedSidecar -Destination ($outputFull + '.sha256') -Force
    Write-Host "Created course mod package: $outputFull"
    Write-Host "SHA-256: $hash"
    Write-Host "Files: $($manifestFiles.Count); imported map assets included: $imported"
}
finally {
    $tempBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    $temporaryFull = [IO.Path]::GetFullPath($temporaryRoot)
    if (-not $temporaryFull.StartsWith($tempBase, [StringComparison]::OrdinalIgnoreCase)) { throw "Refusing to clean a temporary path outside the temp directory: $temporaryFull" }
    if (Test-Path -LiteralPath $temporaryFull) { Remove-Item -LiteralPath $temporaryFull -Recurse -Force }
    $outputDirectoryFull = [IO.Path]::GetFullPath($outputDirectory).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    $outputDirectoryPrefix = $outputDirectoryFull + [IO.Path]::DirectorySeparatorChar
    foreach ($stagingFile in @($stagedArchive, $stagedSidecar)) {
        $stagingFull = [IO.Path]::GetFullPath($stagingFile)
        if (-not $stagingFull.StartsWith($outputDirectoryPrefix, [StringComparison]::OrdinalIgnoreCase) -or
            [IO.Path]::GetFileName($stagingFull) -notlike ('.' + [IO.Path]::GetFileName($outputFull) + '.*.staging.zip*')) {
            throw "Refusing to clean an unexpected staging file: $stagingFull"
        }
        if (Test-Path -LiteralPath $stagingFull) { Remove-Item -LiteralPath $stagingFull -Force }
    }
}
