[CmdletBinding()]
param([string]$ArchivePath)

$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$buildDll = Join-Path $projectRoot 'bin\Release\net6.0\CustomCourseLoader.dll'
$installerSource = Join-Path $projectRoot 'Install-CourseStudio.ps1'
$sampleSource = Join-Path $projectRoot 'Tools\Samples\SampleMeadowNine.json'
$editorSource = Join-Path $projectRoot 'CourseEditor'
$editorPrefix = [IO.Path]::GetFullPath($editorSource).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (-not (Test-Path -LiteralPath $buildDll -PathType Leaf)) { throw "Build output is missing: $buildDll" }

$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ('Dgm-CourseStudio-installer-test-' + [Guid]::NewGuid().ToString('N'))
if (Test-Path -LiteralPath $tempRoot) { throw "Refusing to reuse an existing temporary path: $tempRoot" }
$packageRoot = Join-Path $tempRoot 'Package'
$freshGameRoot = Join-Path $tempRoot 'FreshGame'
$updateGameRoot = Join-Path $tempRoot 'UpdateGame'

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw "Course Studio installer check failed: $Message" }
}

function Get-Hash([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
}

function New-FakeGameRoot([string]$Path) {
    $core = Join-Path $Path 'BepInEx\core'
    New-Item -ItemType Directory -Force -Path $core | Out-Null
    [IO.File]::WriteAllText((Join-Path $core 'BepInEx.Core.dll'), 'fake BepInEx core')
    [IO.File]::WriteAllText((Join-Path $core 'BepInEx.Unity.IL2CPP.dll'), 'fake BepInEx IL2CPP')
}

try {
    $packagePluginRoot = Join-Path $packageRoot 'BepInEx\plugins\CustomCourseLoader'
    $packageEditorRoot = Join-Path $packagePluginRoot 'CourseEditor'
    New-Item -ItemType Directory -Force -Path $packageEditorRoot, (Join-Path $packagePluginRoot 'Courses') | Out-Null
    Copy-Item -LiteralPath $installerSource -Destination (Join-Path $packageRoot 'Install-CourseStudio.ps1')
    Copy-Item -LiteralPath $buildDll -Destination (Join-Path $packagePluginRoot 'CustomCourseLoader.dll')
    Copy-Item -LiteralPath $sampleSource -Destination (Join-Path $packagePluginRoot 'Courses\SampleMeadowNine.json')

    foreach ($file in Get-ChildItem -LiteralPath $editorSource -File -Recurse) {
        $relative = [IO.Path]::GetFullPath($file.FullName).Substring($editorPrefix.Length)
        if ($relative -ieq 'catalog.js' -or $relative -match '^assets[\\/]maps([\\/]|$)') { continue }
        $destination = Join-Path $packageEditorRoot $relative
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination) | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $destination
    }
    [IO.File]::WriteAllText((Join-Path $packageEditorRoot 'catalog.js'), 'window.DGM_BUILTIN_COURSES = { version: 1, courses: [] };')

    $versionLine = Select-String -LiteralPath (Join-Path $projectRoot 'Plugin.cs') -Pattern 'PluginVersion\s*=\s*"([^"]+)"' | Select-Object -First 1
    $version = $versionLine.Matches[0].Groups[1].Value
    $manifest = [ordered]@{ pluginVersion = $version; draft = $true }
    [IO.File]::WriteAllText((Join-Path $packageRoot 'package-manifest.json'), ($manifest | ConvertTo-Json), [Text.UTF8Encoding]::new($false))

    New-FakeGameRoot $freshGameRoot
    & (Join-Path $packageRoot 'Install-CourseStudio.ps1') -GameRoot $freshGameRoot -AllowDraft
    $freshPluginRoot = Join-Path $freshGameRoot 'BepInEx\plugins\CustomCourseLoader'
    Assert-True ((Get-Hash (Join-Path $freshPluginRoot 'CustomCourseLoader.dll')) -eq (Get-Hash $buildDll)) 'fresh install did not copy the plugin DLL.'
    Assert-True ((Get-Hash (Join-Path $freshPluginRoot 'Courses\SampleMeadowNine.json')) -eq (Get-Hash $sampleSource)) 'fresh install did not copy the sample course.'
    Assert-True (Test-Path -LiteralPath (Join-Path $freshPluginRoot 'CourseEditor\catalog.js') -PathType Leaf) 'fresh install did not create the generated-catalog placeholder.'
    Assert-True (Test-Path -LiteralPath (Join-Path $freshPluginRoot 'CourseEditor\assets\maps') -PathType Container) 'fresh install did not create the map-preview folder.'
    Assert-True (Test-Path -LiteralPath (Join-Path $freshPluginRoot 'Maps') -PathType Container) 'fresh install did not create the Maps folder.'

    New-FakeGameRoot $updateGameRoot
    $updatePluginRoot = Join-Path $updateGameRoot 'BepInEx\plugins\CustomCourseLoader'
    $updateCoursesRoot = Join-Path $updatePluginRoot 'Courses'
    $updateMapsRoot = Join-Path $updatePluginRoot 'Maps\ExistingMap'
    $updateEditorRoot = Join-Path $updatePluginRoot 'CourseEditor'
    $updateMapPreview = Join-Path $updateEditorRoot 'assets\maps\player-preview.png'
    $personalBestPath = Join-Path $updateGameRoot 'BepInEx\config\CustomCourseLoader.personal-bests.json'
    New-Item -ItemType Directory -Force -Path $updateCoursesRoot, $updateMapsRoot, (Split-Path -Parent $updateMapPreview), (Split-Path -Parent $personalBestPath) | Out-Null

    $existingSample = Join-Path $updateCoursesRoot 'SampleMeadowNine.json'
    $existingCourse = Join-Path $updateCoursesRoot 'player-course.json'
    $existingCard = Join-Path $updateCoursesRoot 'player-course-card.png'
    $existingMap = Join-Path $updateMapsRoot 'scene.bundle'
    $existingCatalog = Join-Path $updateEditorRoot 'catalog.js'
    $existingEditorIndex = Join-Path $updateEditorRoot 'index.html'
    $existingEditorCore = Join-Path $updateEditorRoot 'app-core.js'
    $oldPlugin = Join-Path $updatePluginRoot 'CustomCourseLoader.dll'
    [IO.File]::WriteAllText($existingSample, '{"user":"customized sample"}')
    [IO.File]::WriteAllText($existingCourse, '{"user":"course"}')
    [IO.File]::WriteAllBytes($existingCard, [byte[]]@(67, 65, 82, 68, 1))
    [IO.File]::WriteAllBytes($existingMap, [byte[]]@(77, 65, 80, 1))
    [IO.File]::WriteAllText($existingCatalog, 'window.DGM_BUILTIN_COURSES = { sentinel: "keep" };')
    [IO.File]::WriteAllBytes($updateMapPreview, [byte[]]@(80, 82, 69, 86, 1))
    [IO.File]::WriteAllText($personalBestPath, '{"player-layout":4}')
    [IO.File]::WriteAllBytes($oldPlugin, [byte[]]@(79, 76, 68, 1))
    [IO.File]::WriteAllText($existingEditorIndex, '<html>old</html>')
    [IO.File]::WriteAllText($existingEditorCore, '/* old editor */')

    $preservedPaths = @($existingSample, $existingCourse, $existingCard, $existingMap, $existingCatalog, $updateMapPreview, $personalBestPath)
    $preserved = @($preservedPaths | ForEach-Object { Get-Hash $_ })
    & (Join-Path $packageRoot 'Install-CourseStudio.ps1') -GameRoot $updateGameRoot -AllowDraft

    Assert-True ((Get-Hash $oldPlugin) -eq (Get-Hash $buildDll)) 'update did not replace the plugin DLL.'
    Assert-True ((Get-Hash $existingEditorIndex) -eq (Get-Hash (Join-Path $editorSource 'index.html'))) 'update did not refresh the editor page.'
    Assert-True ((Get-Hash $existingEditorCore) -eq (Get-Hash (Join-Path $editorSource 'app-core.js'))) 'update did not refresh the editor scripts.'
    for ($i = 0; $i -lt $preservedPaths.Count; $i++) {
        Assert-True ((Get-Hash $preservedPaths[$i]) -eq $preserved[$i]) "update changed player data: $($preservedPaths[$i])"
    }

    $releaseGameRoot = Join-Path $tempRoot 'BlockedDraftGame'
    New-FakeGameRoot $releaseGameRoot
    $blocked = $false
    try { & (Join-Path $packageRoot 'Install-CourseStudio.ps1') -GameRoot $releaseGameRoot }
    catch { $blocked = $_.Exception.Message -match 'DRAFT' }
    Assert-True $blocked 'the installer must refuse a DRAFT archive unless -AllowDraft is passed.'

    if (-not [string]::IsNullOrWhiteSpace($ArchivePath)) {
        $archiveFullPath = (Resolve-Path -LiteralPath $ArchivePath).Path
        if ([IO.Path]::GetExtension($archiveFullPath) -ine '.zip') { throw 'ArchivePath must point to a Course Studio .zip archive.' }

        $extractedPackageRoot = Join-Path $tempRoot 'ExtractedArchive'
        $archiveGameRoot = Join-Path $tempRoot 'ArchiveInstallGame'
        Expand-Archive -LiteralPath $archiveFullPath -DestinationPath $extractedPackageRoot
        New-FakeGameRoot $archiveGameRoot

        $archivePluginRoot = Join-Path $archiveGameRoot 'BepInEx\plugins\CustomCourseLoader'
        $archiveEditorRoot = Join-Path $archivePluginRoot 'CourseEditor'
        $archiveData = @(
            @{ Path = (Join-Path $archivePluginRoot 'Courses\player-course.json'); Content = '{"preserve":"course"}' },
            @{ Path = (Join-Path $archivePluginRoot 'Maps\existing-map\scene.bundle'); Content = 'map-data' },
            @{ Path = (Join-Path $archivePluginRoot 'Courses\player-course-card.png'); Content = 'card-data' },
            @{ Path = (Join-Path $archiveEditorRoot 'catalog.js'); Content = 'window.DGM_BUILTIN_COURSES = { sentinel: "preserve" };' },
            @{ Path = (Join-Path $archiveEditorRoot 'assets\maps\player-preview.png'); Content = 'preview-data' },
            @{ Path = (Join-Path $archiveGameRoot 'BepInEx\config\CustomCourseLoader.personal-bests.json'); Content = '{"preserve":"best"}' }
        )
        foreach ($item in $archiveData) {
            New-Item -ItemType Directory -Force -Path (Split-Path -Parent $item.Path) | Out-Null
            [IO.File]::WriteAllText($item.Path, $item.Content)
        }
        $archiveDataHashes = @($archiveData | ForEach-Object { Get-Hash $_.Path })

        $extractedManifest = Get-Content -LiteralPath (Join-Path $extractedPackageRoot 'package-manifest.json') -Raw | ConvertFrom-Json
        $extractedInstaller = Join-Path $extractedPackageRoot 'Install-CourseStudio.ps1'
        if ($extractedManifest.draft) {
            & $extractedInstaller -GameRoot $archiveGameRoot -AllowDraft
        }
        else {
            & $extractedInstaller -GameRoot $archiveGameRoot
        }

        $archiveInstalledDll = Join-Path $archivePluginRoot 'CustomCourseLoader.dll'
        $archivePackagedDll = Join-Path $extractedPackageRoot 'BepInEx\plugins\CustomCourseLoader\CustomCourseLoader.dll'
        Assert-True ((Get-Hash $archiveInstalledDll) -eq (Get-Hash $archivePackagedDll)) 'the archive installer did not copy its packaged plugin DLL.'
        Assert-True (Test-Path -LiteralPath (Join-Path $archivePluginRoot 'Courses\SampleMeadowNine.json') -PathType Leaf) 'the archive installer did not add the bundled sample course.'
        for ($i = 0; $i -lt $archiveData.Count; $i++) {
            Assert-True ((Get-Hash $archiveData[$i].Path) -eq $archiveDataHashes[$i]) "the archive installer changed existing player data: $($archiveData[$i].Path)"
        }
    }

    $result = 'fresh install, update preservation, and default DRAFT refusal'
    if (-not [string]::IsNullOrWhiteSpace($ArchivePath)) { $result += ', plus extracted archive install and preservation' }
    Write-Host "Course Studio package installer checks passed: $result."
}
finally {
    $fullTarget = [IO.Path]::GetFullPath($tempRoot)
    $tempBasePrefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $fullTarget.StartsWith($tempBasePrefix, [StringComparison]::OrdinalIgnoreCase) -or
        -not [IO.Path]::GetFileName($fullTarget).StartsWith('Dgm-CourseStudio-installer-test-', [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove an unexpected temporary path: $fullTarget"
    }
    if ((Test-Path -LiteralPath $fullTarget) -and ((Get-Item -LiteralPath $fullTarget -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw "Refusing to remove a linked temporary path: $fullTarget"
    }
    if (Test-Path -LiteralPath $fullTarget) { Remove-Item -LiteralPath $fullTarget -Recurse -Force }
    if (Test-Path -LiteralPath $fullTarget) { throw "Temporary installer test files remain: $fullTarget" }
}
