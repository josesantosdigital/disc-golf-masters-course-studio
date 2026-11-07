[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$creator = Join-Path $PSScriptRoot 'New-CourseModPackage.ps1'
$installer = Join-Path $PSScriptRoot 'Install-CourseModPackage.ps1'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('dgm-course-mod-test-' + [Guid]::NewGuid().ToString('N'))
$pluginRoot = Join-Path $testRoot 'AuthorGame\BepInEx\plugins\CustomCourseLoader'
$coursesRoot = Join-Path $pluginRoot 'Courses'
$mapsRoot = Join-Path $pluginRoot 'Maps'
$outputRoot = Join-Path $testRoot 'Packages'

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

function Assert-ExpectedFailure([scriptblock]$Action, [string]$ExpectedText) {
    $failed = $false
    $details = ''
    try { & $Action }
    catch { $failed = $true; $details = $_.Exception.Message }
    if (-not $failed -or $details -notmatch [regex]::Escape($ExpectedText)) {
        throw "Expected failure containing '$ExpectedText'; failed=$failed; details='$details'."
    }
}

function New-TestGameRoot([string]$Path) {
    $coreRoot = Join-Path $Path 'BepInEx\core'
    $pluginFolder = Join-Path $Path 'BepInEx\plugins\CustomCourseLoader'
    New-Item -ItemType Directory -Force -Path $coreRoot, $pluginFolder | Out-Null
    [IO.File]::WriteAllText((Join-Path $coreRoot 'BepInEx.Core.dll'), 'synthetic BepInEx core')
    [IO.File]::WriteAllText((Join-Path $coreRoot 'BepInEx.Unity.IL2CPP.dll'), 'synthetic BepInEx IL2CPP')
}

function Write-CourseJson([string]$Path, [object]$Course) {
    [IO.File]::WriteAllText($Path, ($Course | ConvertTo-Json -Depth 20), [Text.UTF8Encoding]::new($false))
}

try {
    New-Item -ItemType Directory -Force -Path $coursesRoot, $mapsRoot, $outputRoot | Out-Null
    $layoutPath = Join-Path $coursesRoot 'designer-export.json'
    $layout = [ordered]@{
        schemaVersion = 2
        id = 'layout-smoke'
        name = 'Course Package Layout Smoke Test'
        baseCourse = 'CanonPark'
        courseCardImage = 'layout-smoke.png'
        holes = @(@{ par = 3; lengthMeters = 80; cardImage = 'layout-hole.png' })
    }
    Write-CourseJson $layoutPath $layout
    [IO.File]::WriteAllBytes((Join-Path $coursesRoot 'layout-smoke.png'), [byte[]]@(137, 80, 78, 71, 13, 10, 26, 10))
    [IO.File]::WriteAllBytes((Join-Path $coursesRoot 'layout-hole.png'), [byte[]]@(137, 80, 78, 71, 13, 10, 26, 10))

    $layoutPackage = Join-Path $outputRoot 'layout-smoke.zip'
    & $creator -CoursePath $layoutPath -PluginDirectory $pluginRoot -OutputPath $layoutPackage -PackageVersion '1.2.3' -Author 'Fixture Author'
    Assert-True (Test-Path -LiteralPath $layoutPackage -PathType Leaf) 'Layout ZIP was not created.'
    Assert-True (Test-Path -LiteralPath ($layoutPackage + '.sha256') -PathType Leaf) 'Layout ZIP checksum sidecar was not created.'
    $layoutHash = (Get-FileHash -LiteralPath $layoutPackage -Algorithm SHA256).Hash.ToLowerInvariant()
    $sidecar = (Get-Content -LiteralPath ($layoutPackage + '.sha256') -Raw).Trim()
    Assert-True ([string]::Equals($sidecar, "$layoutHash  layout-smoke.zip", [StringComparison]::OrdinalIgnoreCase)) 'Layout ZIP checksum does not match.'
    Assert-ExpectedFailure {
        & $creator -CoursePath $layoutPath -PluginDirectory $pluginRoot -OutputPath $layoutPackage -PackageVersion '1.2.3'
    } 'Output already exists'
    & $creator -CoursePath $layoutPath -PluginDirectory $pluginRoot -OutputPath $layoutPackage -PackageVersion '1.2.3' -Author 'Fixture Author' -Force
    $layoutHash = (Get-FileHash -LiteralPath $layoutPackage -Algorithm SHA256).Hash.ToLowerInvariant()
    $sidecar = (Get-Content -LiteralPath ($layoutPackage + '.sha256') -Raw).Trim()
    Assert-True ([string]::Equals($sidecar, "$layoutHash  layout-smoke.zip", [StringComparison]::OrdinalIgnoreCase)) 'Forced package replacement left a stale checksum.'

    $layoutExtract = Join-Path $testRoot 'ExtractedLayout'
    Expand-Archive -LiteralPath $layoutPackage -DestinationPath $layoutExtract
    $layoutManifest = Get-Content -LiteralPath (Join-Path $layoutExtract 'manifest.json') -Raw | ConvertFrom-Json
    $packageReadme = Get-Content -LiteralPath (Join-Path $layoutExtract 'README.md') -Raw
    Assert-True ($layoutManifest.id -eq 'layout-smoke' -and $layoutManifest.minimumCourseStudioVersion) 'Layout manifest is missing course metadata or loader requirement.'
    Assert-True (@($layoutManifest.files | Where-Object { $_.path -eq 'Courses/layout-smoke.json' }).Count -eq 1) 'Course JSON package path was not normalized to the course ID.'
    Assert-True ($packageReadme.Contains('Install-CourseModPackage.ps1') -and $packageReadme.Contains('redistribution permission')) 'Package README is missing install or imported-asset rights instructions.'
    Assert-True (@($layoutManifest.files | Where-Object { $_.path -like 'Courses/*.png' }).Count -eq 2) 'Layout package did not include both referenced card images.'

    $freshGame = Join-Path $testRoot 'FreshGame'
    New-TestGameRoot $freshGame
    & (Join-Path $layoutExtract 'Install-CourseModPackage.ps1') -GameRoot $freshGame
    $freshCourse = Join-Path $freshGame 'BepInEx\plugins\CustomCourseLoader\Courses\layout-smoke.json'
    Assert-True (Test-Path -LiteralPath $freshCourse -PathType Leaf) 'Fresh install did not add the course JSON.'
    Assert-True (Test-Path -LiteralPath (Join-Path (Split-Path -Parent $freshCourse) 'layout-hole.png') -PathType Leaf) 'Fresh install did not add the hole card image.'

    Assert-ExpectedFailure {
        & (Join-Path $layoutExtract 'Install-CourseModPackage.ps1') -GameRoot $freshGame
    } 'File already exists; nothing was installed'

    [IO.File]::WriteAllText($freshCourse, 'sentinel: prior layout')
    & (Join-Path $layoutExtract 'Install-CourseModPackage.ps1') -GameRoot $freshGame -Overwrite
    Assert-True ((Get-FileHash -LiteralPath $freshCourse -Algorithm SHA256).Hash -eq (Get-FileHash -LiteralPath (Join-Path $layoutExtract 'Payload\Courses\layout-smoke.json') -Algorithm SHA256).Hash) 'Overwrite did not restore the packaged course JSON.'

    $tamperedExtract = Join-Path $testRoot 'TamperedLayout'
    Expand-Archive -LiteralPath $layoutPackage -DestinationPath $tamperedExtract
    [IO.File]::WriteAllText((Join-Path $tamperedExtract 'Payload\Courses\layout-smoke.json'), 'changed after packaging')
    $tamperGame = Join-Path $testRoot 'TamperGame'
    New-TestGameRoot $tamperGame
    Assert-ExpectedFailure {
        & (Join-Path $tamperedExtract 'Install-CourseModPackage.ps1') -GameRoot $tamperGame
    } 'SHA-256 mismatch'
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $tamperGame 'BepInEx\plugins\CustomCourseLoader\Courses\layout-smoke.json'))) 'Tampered package wrote files before hash validation completed.'

    $importedPath = Join-Path $coursesRoot 'imported-smoke.json'
    $sceneBundle = Join-Path $mapsRoot 'imported-smoke\scene.bundle'
    $dependencyBundle = Join-Path $mapsRoot 'imported-smoke\dependencies\shared.bundle'
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $dependencyBundle) | Out-Null
    [IO.File]::WriteAllBytes($sceneBundle, [Text.Encoding]::ASCII.GetBytes('UnityFS synthetic scene'))
    [IO.File]::WriteAllBytes($dependencyBundle, [Text.Encoding]::ASCII.GetBytes('UnityFS synthetic dependency'))
    $imported = [ordered]@{
        schemaVersion = 2
        id = 'imported-smoke'
        name = 'Course Package Imported Map Smoke Test'
        baseCourse = 'CanonPark'
        holes = @(@{ par = 3; lengthMeters = 90 })
        mapScene = [ordered]@{
            scenePath = 'Assets/Scenes/ImportedSmoke.unity'
            bundlePath = 'Maps/imported-smoke/scene.bundle'
            dependencyBundlePaths = @('Maps/imported-smoke/dependencies/shared.bundle')
        }
    }
    Write-CourseJson $importedPath $imported
    Assert-ExpectedFailure {
        & $creator -CoursePath $importedPath -PluginDirectory $pluginRoot -OutputPath (Join-Path $outputRoot 'missing-asset-permission.zip')
    } 'Pass -IncludeImportedAssets'
    Assert-ExpectedFailure {
        & $creator -CoursePath $importedPath -PluginDirectory $pluginRoot -OutputPath (Join-Path $outputRoot 'missing-reference.zip') -IncludeImportedAssets
    } 'require a permission source'

    $importedPackage = Join-Path $outputRoot 'imported-smoke.zip'
    & $creator -CoursePath $importedPath -PluginDirectory $pluginRoot -OutputPath $importedPackage -PackageVersion '1.0.0' -IncludeImportedAssets -AssetPermissionReference 'https://example.invalid/permission-record'
    $importedExtract = Join-Path $testRoot 'ExtractedImported'
    Expand-Archive -LiteralPath $importedPackage -DestinationPath $importedExtract
    $importedManifest = Get-Content -LiteralPath (Join-Path $importedExtract 'manifest.json') -Raw | ConvertFrom-Json
    Assert-True ([bool]$importedManifest.importedMap) 'Imported-map manifest flag is missing.'
    Assert-True ($importedManifest.assetPermissionReference -eq 'https://example.invalid/permission-record') 'Imported-map permission/source reference was not recorded.'
    Assert-True (@($importedManifest.files | Where-Object { $_.path -like 'Maps/*' }).Count -eq 2) 'Imported map package did not include scene and dependency bundles.'

    $sharedGame = Join-Path $testRoot 'SharedBundleGame'
    New-TestGameRoot $sharedGame
    $sharedPlugin = Join-Path $sharedGame 'BepInEx\plugins\CustomCourseLoader'
    $sharedCourses = Join-Path $sharedPlugin 'Courses'
    $sharedScene = Join-Path $sharedPlugin 'Maps\imported-smoke\scene.bundle'
    New-Item -ItemType Directory -Force -Path $sharedCourses, (Split-Path -Parent $sharedScene) | Out-Null
    [IO.File]::WriteAllBytes($sharedScene, [Text.Encoding]::ASCII.GetBytes('UnityFS bundle used by another course'))
    $otherCourse = [ordered]@{
        schemaVersion = 2
        id = 'other-installed-course'
        name = 'Other Installed Course'
        baseCourse = 'CanonPark'
        holes = @(@{ par = 3; lengthMeters = 75 })
        mapScene = [ordered]@{ scenePath = 'Assets/Other.unity'; bundlePath = 'Maps/imported-smoke/scene.bundle' }
    }
    Write-CourseJson (Join-Path $sharedCourses 'other-installed-course.json') $otherCourse
    Assert-ExpectedFailure {
        & (Join-Path $importedExtract 'Install-CourseModPackage.ps1') -GameRoot $sharedGame -Overwrite
    } 'another installed course references it'
    Assert-True (([Text.Encoding]::ASCII.GetString([IO.File]::ReadAllBytes($sharedScene))) -eq 'UnityFS bundle used by another course') 'Shared-bundle refusal changed the existing course asset.'
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $sharedCourses 'imported-smoke.json'))) 'Shared-bundle refusal partially installed the new course.'

    $importedGame = Join-Path $testRoot 'ImportedGame'
    New-TestGameRoot $importedGame
    & (Join-Path $importedExtract 'Install-CourseModPackage.ps1') -GameRoot $importedGame
    $installedMapRoot = Join-Path $importedGame 'BepInEx\plugins\CustomCourseLoader\Maps\imported-smoke'
    Assert-True (Test-Path -LiteralPath (Join-Path $installedMapRoot 'scene.bundle') -PathType Leaf) 'Imported install did not add its scene bundle.'
    Assert-True (Test-Path -LiteralPath (Join-Path $installedMapRoot 'dependencies\shared.bundle') -PathType Leaf) 'Imported install did not add its dependency bundle.'

    Write-Output 'Course mod package checks passed: layout ZIP, card inclusion, checksum, fresh install, collision refusal, overwrite, tamper rejection, imported-map permission gate, shared-asset overwrite refusal, and scene/dependency installation.'
}
finally {
    $fullTarget = [IO.Path]::GetFullPath($testRoot)
    $tempPrefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $fullTarget.StartsWith($tempPrefix, [StringComparison]::OrdinalIgnoreCase) -or
        -not [IO.Path]::GetFileName($fullTarget).StartsWith('dgm-course-mod-test-', [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove an unexpected temporary path: $fullTarget"
    }
    if (Test-Path -LiteralPath $fullTarget) { Remove-Item -LiteralPath $fullTarget -Recurse -Force }
}
