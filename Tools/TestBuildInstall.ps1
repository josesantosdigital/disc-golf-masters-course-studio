[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ReferenceGameRoot
)

$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$projectFile = Join-Path $projectRoot 'CustomCourseLoader.csproj'
$buildScript = Join-Path $projectRoot 'build.ps1'
$resolvedReferenceRoot = (Resolve-Path -LiteralPath $ReferenceGameRoot).Path
$referencePaths = @(Select-String -LiteralPath $projectFile -Pattern '<HintPath>\$\(GameRoot\)([^<]+)</HintPath>' -AllMatches | ForEach-Object {
    foreach ($match in $_.Matches) { $match.Groups[1].Value }
} | Sort-Object -Unique)
if ($referencePaths.Count -eq 0) { throw 'No GameRoot reference assemblies were found in CustomCourseLoader.csproj.' }

$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ('Dgm-CourseStudio-install-test-' + [Guid]::NewGuid().ToString('N'))
$freshGameRoot = Join-Path $tempRoot 'FreshGame'
$updateGameRoot = Join-Path $tempRoot 'UpdateGame'
if (Test-Path -LiteralPath $tempRoot) { throw "Refusing to reuse an existing temporary path: $tempRoot" }

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw "Install-script check failed: $Message" }
}

function Get-Hash([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
}

function Copy-BuildReferences([string]$TargetGameRoot) {
    foreach ($relativePath in $referencePaths) {
        $sourcePath = Join-Path $resolvedReferenceRoot $relativePath
        if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
            throw "Build reference is missing from ReferenceGameRoot: $relativePath"
        }
        $destinationPath = Join-Path $TargetGameRoot $relativePath
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destinationPath) | Out-Null
        Copy-Item -LiteralPath $sourcePath -Destination $destinationPath
    }
}

try {
    $builtPlugin = Join-Path $projectRoot 'bin\Release\net6.0\CustomCourseLoader.dll'
    $sourceSampleCourse = Join-Path $projectRoot 'Courses\SampleMeadowNine.json'
    $sourceEditorIndex = Join-Path $projectRoot 'CourseEditor\index.html'
    $sourceEditorCore = Join-Path $projectRoot 'CourseEditor\app-core.js'

    Copy-BuildReferences $freshGameRoot
    & $buildScript -GameRoot $freshGameRoot -Configuration Release
    $freshPluginRoot = Join-Path $freshGameRoot 'BepInEx\plugins\CustomCourseLoader'
    $freshInstalledPlugin = Join-Path $freshPluginRoot 'CustomCourseLoader.dll'
    $freshSampleCourse = Join-Path $freshPluginRoot 'Courses\SampleMeadowNine.json'
    Assert-True (Test-Path -LiteralPath $freshInstalledPlugin -PathType Leaf) 'the plugin DLL was not installed into the fresh fake game root'
    Assert-True ((Get-Hash $freshInstalledPlugin) -eq (Get-Hash $builtPlugin)) 'the fresh-install plugin DLL does not match the build output'
    Assert-True (Test-Path -LiteralPath $freshSampleCourse -PathType Leaf) 'the bundled sample course was not copied to the fresh game root'
    Assert-True ((Get-Hash $freshSampleCourse) -eq (Get-Hash $sourceSampleCourse)) 'the fresh-install sample course does not match the bundled sample'
    Assert-True ((Get-Hash (Join-Path $freshPluginRoot 'CourseEditor\index.html')) -eq (Get-Hash $sourceEditorIndex)) 'the Course Editor was not copied to the fresh game root'

    Copy-BuildReferences $updateGameRoot
    $updatePluginRoot = Join-Path $updateGameRoot 'BepInEx\plugins\CustomCourseLoader'
    $updateCoursesDirectory = Join-Path $updatePluginRoot 'Courses'
    $updateMapBundlePath = Join-Path $updatePluginRoot 'Maps\ExistingMap\scene.bundle'
    $updateCatalogPath = Join-Path $updatePluginRoot 'CourseEditor\catalog.js'
    $updatePreviewPath = Join-Path $updatePluginRoot 'CourseEditor\assets\maps\preserved-preview.png'
    New-Item -ItemType Directory -Force -Path $updateCoursesDirectory, (Split-Path -Parent $updateMapBundlePath), (Split-Path -Parent $updatePreviewPath) | Out-Null

    $existingSampleCourse = Join-Path $updateCoursesDirectory 'SampleMeadowNine.json'
    $existingPlayerCourse = Join-Path $updateCoursesDirectory 'existing-player-course.json'
    $existingCourseCard = Join-Path $updateCoursesDirectory 'existing-player-course-card.png'
    $updateInstalledPlugin = Join-Path $updatePluginRoot 'CustomCourseLoader.dll'
    $updateEditorIndex = Join-Path $updatePluginRoot 'CourseEditor\index.html'

    [IO.File]::WriteAllText($existingSampleCourse, '{"playerData":"existing-sample-course"}', [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText($existingPlayerCourse, '{"playerData":"preserve-me"}', [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllBytes($existingCourseCard, [byte[]]@(67, 65, 82, 68, 1, 2, 3))
    [IO.File]::WriteAllBytes($updateMapBundlePath, [byte[]]@(77, 65, 80, 1, 2, 3))
    [IO.File]::WriteAllText($updateCatalogPath, 'window.DGM_BUILTIN_COURSES = { sentinel: "preserve-me" };', [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllBytes($updatePreviewPath, [byte[]]@(80, 82, 69, 86, 4, 5, 6))
    [IO.File]::WriteAllBytes($updateInstalledPlugin, [byte[]]@(79, 76, 68, 1, 2, 3))
    [IO.File]::WriteAllText($updateEditorIndex, '<html>stale editor</html>', [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText((Join-Path $updatePluginRoot 'CourseEditor\app-core.js'), '/* stale editor core */', [Text.UTF8Encoding]::new($false))

    $sentinelHashes = @{
        SampleCourse = Get-Hash $existingSampleCourse
        PlayerCourse = Get-Hash $existingPlayerCourse
        CourseCard = Get-Hash $existingCourseCard
        MapBundle = Get-Hash $updateMapBundlePath
        Catalog = Get-Hash $updateCatalogPath
        Preview = Get-Hash $updatePreviewPath
    }

    & $buildScript -GameRoot $updateGameRoot -Configuration Release

    Assert-True (Test-Path -LiteralPath $updateInstalledPlugin -PathType Leaf) 'the plugin DLL was not installed into the update fake game root'
    Assert-True ((Get-Hash $updateInstalledPlugin) -eq (Get-Hash $builtPlugin)) 'the update plugin DLL does not match the build output'
    Assert-True ((Get-Hash $existingSampleCourse) -eq $sentinelHashes.SampleCourse) 'an existing sample course was overwritten during update'
    Assert-True ((Get-Hash $existingPlayerCourse) -eq $sentinelHashes.PlayerCourse) 'an existing player course was changed'
    Assert-True ((Get-Hash $existingCourseCard) -eq $sentinelHashes.CourseCard) 'an existing player course card was changed'
    Assert-True ((Get-Hash $updateMapBundlePath) -eq $sentinelHashes.MapBundle) 'an existing map bundle was changed'
    Assert-True ((Get-Hash $updateCatalogPath) -eq $sentinelHashes.Catalog) 'the generated course catalog was changed'
    Assert-True ((Get-Hash $updatePreviewPath) -eq $sentinelHashes.Preview) 'an existing map preview was changed'
    Assert-True ((Get-Hash $updateEditorIndex) -eq (Get-Hash $sourceEditorIndex)) 'the Course Editor page was not updated'
    Assert-True ((Get-Hash (Join-Path $updatePluginRoot 'CourseEditor\app-core.js')) -eq (Get-Hash $sourceEditorCore)) 'the Course Editor core script was not updated'

    Write-Host 'Disposable build/install checks passed for fresh install and update: plugin hash, sample course, editor update, and preservation of existing course/card/map/catalog/preview data.'
    Write-Host 'This check does not start DGM or verify BepInEx startup on a clean game installation.'
}
finally {
    $fullTarget = [IO.Path]::GetFullPath($tempRoot)
    $tempBasePrefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $fullTarget.StartsWith($tempBasePrefix, [StringComparison]::OrdinalIgnoreCase) -or
        -not [IO.Path]::GetFileName($fullTarget).StartsWith('Dgm-CourseStudio-install-test-', [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove an unexpected temporary path: $fullTarget"
    }
    if ((Test-Path -LiteralPath $fullTarget) -and ((Get-Item -LiteralPath $fullTarget -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw "Refusing to remove a linked temporary path: $fullTarget"
    }
    if (Test-Path -LiteralPath $fullTarget) { Remove-Item -LiteralPath $fullTarget -Recurse -Force }
    if (Test-Path -LiteralPath $fullTarget) { throw "Temporary test game files remain: $fullTarget" }
}
