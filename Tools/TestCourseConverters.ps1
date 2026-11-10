[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$script:passed = 0
$converterDirectory = $PSScriptRoot
$sampleDirectory = Join-Path $converterDirectory 'Samples'
$layoutConverter = Join-Path $converterDirectory 'ConvertCourseLayoutJson.ps1'
$bonelabConverter = Join-Path $converterDirectory 'ConvertBonelabPallet.ps1'
$unityConverter = Join-Path $converterDirectory 'ConvertUnitySceneBundle.ps1'
$validator = Join-Path $converterDirectory 'ValidateCoursePackage.ps1'
$projectRoot = [IO.Path]::GetFullPath((Split-Path -Parent $converterDirectory))
$tempRoot = Join-Path $projectRoot ('.converter-test-' + [Guid]::NewGuid().ToString('N'))
$tempRootFull = [IO.Path]::GetFullPath($tempRoot)
$projectPrefix = $projectRoot.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (-not $tempRootFull.StartsWith($projectPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Converter fixtures must be created inside the project directory.'
}

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw "Converter check failed: $Message" }
}

function Write-JsonFile([string]$Path, [object]$Value) {
    $json = ConvertTo-Json -InputObject $Value -Depth 24
    [IO.File]::WriteAllText($Path, $json, [Text.UTF8Encoding]::new($false))
}

function Write-UnityBundle([string]$Path, [string]$UnityVersion = '2022.3.1f1') {
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Path) | Out-Null
    [IO.File]::WriteAllBytes($Path, [Text.Encoding]::ASCII.GetBytes("UnityFS 6.0 $UnityVersion`0synthetic-fixture"))
}

function Validate-ConvertedCourse([string]$PluginDirectory, [string]$CourseId) {
    $coursePath = Join-Path $PluginDirectory (Join-Path 'Courses' ($CourseId + '.json'))
    Assert-True (Test-Path -LiteralPath $coursePath -PathType Leaf) "course JSON was not created for '$CourseId'"
    Assert-True (Test-Path -LiteralPath (Join-Path $PluginDirectory (Join-Path 'Courses' ($CourseId + '-card.png'))) -PathType Leaf) "course card was not created for '$CourseId'"
    & $validator -CoursePath $coursePath -PluginDirectory $PluginDirectory
    return (Get-Content -LiteralPath $coursePath -Raw | ConvertFrom-Json)
}

function New-SyntheticBonelabPallet([string]$PalletRoot) {
    $packageName = 'Synthetic.Author.DemoMarsh'
    $packageRoot = Join-Path $PalletRoot $packageName
    New-Item -ItemType Directory -Force -Path $packageRoot | Out-Null
    $catalog = [ordered]@{
        m_InternalIds = @(
            'Assets/_MarrowAssets/Levels/Demo Marsh.unity',
            'Assets/_MarrowAssets/Levels/Demo Marsh Night.unity',
            "PALLET_BARCODE:${packageName}:\demo_marsh_levels_scenes_level/demomarsh.bundle",
            "PALLET_BARCODE:${packageName}:\demo_marsh_levels_scenes_level/demomarshnight.bundle",
            "PALLET_BARCODE:${packageName}:\${packageName}_monoscripts.bundle",
            "PALLET_BARCODE:${packageName}:\${packageName}_unitybuiltinshaders.bundle"
        )
    }
    Write-JsonFile (Join-Path $packageRoot 'catalog_Synthetic.Author.DemoMarsh.json') $catalog
    Write-JsonFile (Join-Path $packageRoot ($packageName + '.pallet.json')) ([ordered]@{ name = 'Synthetic nested PC fixture' })
    Write-UnityBundle (Join-Path $packageRoot 'demo_marsh_levels_scenes_level/demomarsh.bundle') '2021.3.16f1'
    Write-UnityBundle (Join-Path $packageRoot 'demo_marsh_levels_scenes_level/demomarshnight.bundle') '2021.3.16f1'
    Write-UnityBundle (Join-Path $packageRoot ($packageName + '_monoscripts.bundle')) '2021.3.16f1'
    Write-UnityBundle (Join-Path $packageRoot ($packageName + '_unitybuiltinshaders.bundle')) '2021.3.16f1'
}

New-Item -ItemType Directory -Force -Path $tempRootFull | Out-Null
try {
    $offsetsPlugin = Join-Path $tempRootFull 'offsets-plugin'
    & $layoutConverter -LayoutPath (Join-Path $sampleDirectory 'course-layout-offsets.example.json') -PluginDirectory $offsetsPlugin
    $offsetsCourse = Validate-ConvertedCourse $offsetsPlugin 'ported-forest-nine'
    Assert-True ($offsetsCourse.holes.Count -eq 2) 'offset-layout hole count changed'
    Assert-True ($offsetsCourse.holes[0].obZones.Count -eq 1) 'offset-layout OB polygon was not preserved'
    Assert-True ($offsetsCourse.holes[0].basketYawDegrees -eq 15) 'offset-layout basket yaw was not preserved'
    Assert-True ($offsetsCourse.holes[0].teeEnabled -eq $false -and $offsetsCourse.holes[0].cardEnabled -eq $false) 'omitted tee/card options must default off'
    Assert-True ($offsetsCourse.holes[1].teeEnabled -eq $true -and $offsetsCourse.holes[1].cardEnabled -eq $false) 'tee-only visibility was not preserved'
    $script:passed++

    $unityPlugin = Join-Path $tempRootFull 'unity-layout-plugin'
    $unityBundle = Join-Path $unityPlugin 'Maps/bonelab-hoodcorner/scene.bundle'
    $unityDependency = Join-Path $unityPlugin 'Maps/bonelab-hoodcorner/dependencies/MonoScripts.bundle'
    Write-UnityBundle $unityBundle
    Write-UnityBundle $unityDependency
    & $layoutConverter -LayoutPath (Join-Path $sampleDirectory 'course-layout-unityworld.example.json') -PluginDirectory $unityPlugin
    $unityCourse = Validate-ConvertedCourse $unityPlugin 'bonelab-hoodcorner'
    Assert-True ($unityCourse.holes.Count -eq 2) 'Unity-world layout hole count changed'
    Assert-True ($unityCourse.holes[0].teeEnabled -eq $false -and $unityCourse.holes[0].cardEnabled -eq $true) 'card-only visibility was not preserved'
    Assert-True ($unityCourse.holes[1].teeEnabled -eq $false -and $unityCourse.holes[1].cardEnabled -eq $false) 'imported tee/card defaults must be off'
    Assert-True ($unityCourse.mapLighting.realtimeMapShadows -eq $true) 'realtimeMapShadows was dropped'
    Assert-True ($unityCourse.mapLighting.hideImportedParticles -eq $false) 'hideImportedParticles was dropped'
    Assert-True ($unityCourse.mapLighting.generateTreeCanopyColliders -eq $false) 'generateTreeCanopyColliders was dropped'
    Assert-True ($unityCourse.mapLighting.experimentalFoliageInteractions -eq $true) 'experimentalFoliageInteractions was dropped'
    Assert-True ($unityCourse.mapLighting.useGameFoliageShader -eq $false) 'useGameFoliageShader was dropped'
    Assert-True ($unityCourse.mapLighting.preferBaseCourseSun -eq $false) 'preferBaseCourseSun was dropped'
      Assert-True ($unityCourse.mapLighting.localLightIntensityMultiplier -eq 1) 'localLightIntensityMultiplier was dropped'
      Assert-True ($unityCourse.mapLighting.suppressedEmissionTextures.Count -eq 1 -and $unityCourse.mapLighting.suppressedEmissionTextures[0] -eq 'ExampleBrightEmission') 'suppressedEmissionTextures was dropped'
      Assert-True ($unityCourse.mapLighting.hiddenLightFixtureTextures.Count -eq 1 -and $unityCourse.mapLighting.hiddenLightFixtureTextures[0] -eq 'ExampleFixtureEmission') 'hiddenLightFixtureTextures was dropped'
      Assert-True ($unityCourse.mapLighting.suppressedLightRoots.Count -eq 1 -and $unityCourse.mapLighting.suppressedLightRoots[0] -eq 'ExampleRoot/ExampleBuilding') 'suppressedLightRoots was dropped'
    Assert-True ($unityCourse.mapLighting.ambientIntensity -eq 1.2) 'ambientIntensity was dropped'
    Assert-True ($unityCourse.mapLighting.keyLightIntensity -eq 2.5) 'keyLightIntensity was dropped'
    $script:passed++

    $sourceDirectory = Join-Path $tempRootFull 'generic-source'
    $genericBundle = Join-Path $sourceDirectory 'scene.bundle'
    $genericDependency = Join-Path $sourceDirectory 'shared.bundle'
    Write-UnityBundle $genericBundle
    Write-UnityBundle $genericDependency
    $genericPlugin = Join-Path $tempRootFull 'generic-plugin'
    & $unityConverter -SceneBundlePath $genericBundle -ScenePath 'Assets/Levels/Demo.unity' -Id 'generic-scene' -CourseName 'Generic Scene Fixture' -DependencyBundlePaths @($genericDependency) -PluginDirectory $genericPlugin
    $genericCourse = Validate-ConvertedCourse $genericPlugin 'generic-scene'
    Assert-True ($genericCourse.mapScene.scenePath -eq 'Assets/Levels/Demo.unity') 'generic Unity scene path changed'
    Assert-True ($genericCourse.mapScene.dependencyBundlePaths.Count -eq 1) 'generic Unity dependency was not recorded'
    $script:passed++

    $invalidDependency = Join-Path $sourceDirectory 'invalid-dependency.bundle'
    [IO.File]::WriteAllText($invalidDependency, 'not a UnityFS dependency bundle', [Text.Encoding]::ASCII)
    $invalidDependencyPlugin = Join-Path $tempRootFull 'invalid-dependency-plugin'
    $invalidDependencyFailed = $false
    try {
        & $unityConverter -SceneBundlePath $genericBundle -ScenePath 'Assets/Levels/InvalidDependency.unity' -Id 'invalid-dependency' -CourseName 'Invalid Dependency Fixture' -DependencyBundlePaths @($invalidDependency) -PluginDirectory $invalidDependencyPlugin
    }
    catch { $invalidDependencyFailed = $true }
    Assert-True $invalidDependencyFailed 'generic Unity converter accepted a dependency without a UnityFS header'
    $invalidDependencyMaps = Join-Path $invalidDependencyPlugin 'Maps'
    $invalidDependencyCourses = Join-Path $invalidDependencyPlugin 'Courses'
    $invalidDependencyStages = if (Test-Path -LiteralPath $invalidDependencyMaps) { @(Get-ChildItem -LiteralPath $invalidDependencyMaps -Force | Where-Object { $_.Name -like '.invalid-dependency.*.tmp' }) } else { @() }
    Assert-True ($invalidDependencyStages.Count -eq 0) 'invalid dependency left a partial map staging directory'
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $invalidDependencyMaps 'invalid-dependency'))) 'invalid dependency installed a partial map folder'
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $invalidDependencyCourses 'invalid-dependency.json'))) 'invalid dependency left a course JSON file'
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $invalidDependencyCourses 'invalid-dependency-card.png'))) 'invalid dependency left a course card'
    $script:passed++

    $interruptedPlugin = Join-Path $tempRootFull 'interrupted-copy-plugin'
    $interruptedScene = Join-Path $tempRootFull 'interrupted-scene.bundle'
    $lockedDependency = Join-Path $tempRootFull 'locked-dependency.bundle'
    Write-UnityBundle $interruptedScene
    Write-UnityBundle $lockedDependency
    $dependencyLock = [IO.File]::Open($lockedDependency, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    $copyFailed = $false
    try {
        try {
            & $unityConverter -SceneBundlePath $interruptedScene -ScenePath 'Assets/Levels/Interrupted.unity' -Id 'interrupted-copy' -CourseName 'Interrupted Copy Fixture' -DependencyBundlePaths @($lockedDependency) -PluginDirectory $interruptedPlugin *>&1 | Out-Null
        }
        catch { $copyFailed = $true }
    }
    finally { $dependencyLock.Dispose() }
    Assert-True $copyFailed 'locked dependency fixture unexpectedly completed its package copy'
    $interruptedMaps = Join-Path $interruptedPlugin 'Maps'
    $interruptedCourses = Join-Path $interruptedPlugin 'Courses'
    $stageLeftovers = if (Test-Path -LiteralPath $interruptedMaps) { @(Get-ChildItem -LiteralPath $interruptedMaps -Force | Where-Object { $_.Name -like '.interrupted-copy.*.tmp' }) } else { @() }
    Assert-True ($stageLeftovers.Count -eq 0) 'failed scene/dependency copy left a partial map staging directory'
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $interruptedMaps 'interrupted-copy'))) 'failed scene/dependency copy installed a partial map folder'
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $interruptedCourses 'interrupted-copy.json'))) 'failed scene/dependency copy left a course JSON file'
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $interruptedCourses 'interrupted-copy-card.png'))) 'failed scene/dependency copy left a course card'
    $script:passed++

    $palletRoot = Join-Path $tempRootFull 'bonelab-pallet'
    New-SyntheticBonelabPallet $palletRoot
    $bonelabFolderPlugin = Join-Path $tempRootFull 'bonelab-folder-plugin'
    & $bonelabConverter -ArchivePath $palletRoot -SceneName 'Demo Marsh' -Id 'bonelab-folder' -CourseName 'BONELAB Folder Fixture' -PluginDirectory $bonelabFolderPlugin
    $folderCourse = Validate-ConvertedCourse $bonelabFolderPlugin 'bonelab-folder'
    Assert-True ($folderCourse.mapScene.dependencyBundlePaths.Count -eq 2) 'BONELAB folder adapter did not include both common dependencies'
    Assert-True ($folderCourse.mapScene.scenePath -eq 'Assets/_MarrowAssets/Levels/Demo Marsh.unity') 'BONELAB folder adapter did not resolve the nested Marrow scene path'
    $folderBundlePath = Join-Path $bonelabFolderPlugin ($folderCourse.mapScene.bundlePath.Replace('/', '\'))
    $folderBundleHeader = [Text.Encoding]::ASCII.GetString([IO.File]::ReadAllBytes($folderBundlePath))
    Assert-True ($folderBundleHeader.StartsWith('UnityFS 6.0 2021.3.16f1', [StringComparison]::Ordinal)) 'BONELAB folder adapter did not preserve the source Unity version header'

    $palletZip = Join-Path $tempRootFull 'bonelab-pallet.zip'
    Compress-Archive -Path (Join-Path $palletRoot '*') -DestinationPath $palletZip -CompressionLevel Fastest
    $bonelabZipPlugin = Join-Path $tempRootFull 'bonelab-zip-plugin'
    & $bonelabConverter -ArchivePath $palletZip -SceneName 'Demo Marsh Night' -Id 'bonelab-zip' -CourseName 'BONELAB ZIP Fixture' -PluginDirectory $bonelabZipPlugin
    $zipCourse = Validate-ConvertedCourse $bonelabZipPlugin 'bonelab-zip'
    Assert-True ($zipCourse.mapScene.dependencyBundlePaths.Count -eq 2) 'BONELAB ZIP adapter did not include both common dependencies'
    Assert-True ($zipCourse.mapScene.scenePath -eq 'Assets/_MarrowAssets/Levels/Demo Marsh Night.unity') 'BONELAB ZIP adapter did not resolve the selected scene variant'
    $zipBundlePath = Join-Path $bonelabZipPlugin ($zipCourse.mapScene.bundlePath.Replace('/', '\'))
    $zipBundleHeader = [Text.Encoding]::ASCII.GetString([IO.File]::ReadAllBytes($zipBundlePath))
    Assert-True ($zipBundleHeader.StartsWith('UnityFS 6.0 2021.3.16f1', [StringComparison]::Ordinal)) 'BONELAB ZIP adapter did not preserve the source Unity version header'
    $script:passed++

    $rollbackPlugin = Join-Path $tempRootFull 'rollback-plugin'
    $rollbackCourses = Join-Path $rollbackPlugin 'Courses'
    New-Item -ItemType Directory -Force -Path $rollbackCourses | Out-Null
    $rollbackCoursePath = Join-Path $rollbackCourses 'bonelab-hoodcorner.json'
    $rollbackCardPath = Join-Path $rollbackCourses 'bonelab-hoodcorner-card.png'
    $originalCourse = [ordered]@{
        schemaVersion = 2
        id = 'bonelab-hoodcorner'
        name = 'Existing course must survive'
        baseCourse = 'CanonPark'
        courseCardImage = 'bonelab-hoodcorner-card.png'
        holes = @([ordered]@{ par = 3; lengthMeters = 60 })
    }
    Write-JsonFile $rollbackCoursePath $originalCourse
    [IO.File]::WriteAllBytes($rollbackCardPath, [byte[]]@(9, 8, 7, 6, 5))
    $originalCourseHash = (Get-FileHash -LiteralPath $rollbackCoursePath -Algorithm SHA256).Hash
    $originalCardHash = (Get-FileHash -LiteralPath $rollbackCardPath -Algorithm SHA256).Hash

    $brokenLayout = Get-Content -LiteralPath (Join-Path $sampleDirectory 'course-layout-unityworld.example.json') -Raw | ConvertFrom-Json
    $brokenLayout.mapScene.bundlePath = 'Maps/missing-scene/scene.bundle'
    $brokenLayout.mapScene.dependencyBundlePaths = @()
    $brokenLayoutPath = Join-Path $tempRootFull 'layout-with-missing-bundle.json'
    Write-JsonFile $brokenLayoutPath $brokenLayout
    $overwriteFailed = $false
    try {
        & $layoutConverter -LayoutPath $brokenLayoutPath -PluginDirectory $rollbackPlugin -Overwrite *>&1 | Out-Null
    }
    catch { $overwriteFailed = $true }
    Assert-True $overwriteFailed 'invalid overwrite fixture unexpectedly succeeded'
    Assert-True ((Get-FileHash -LiteralPath $rollbackCoursePath -Algorithm SHA256).Hash -eq $originalCourseHash) 'failed overwrite did not restore the original course JSON'
    Assert-True ((Get-FileHash -LiteralPath $rollbackCardPath -Algorithm SHA256).Hash -eq $originalCardHash) 'failed overwrite did not restore the original card'
    $leftovers = @(Get-ChildItem -LiteralPath $rollbackCourses -File | Where-Object { $_.Name -match '\.(tmp|backup)$' })
    Assert-True ($leftovers.Count -eq 0) 'failed overwrite left temporary or backup files behind'
    $script:passed++

    Write-Host "Course converter smoke checks passed: $script:passed scenarios (offset layout, Unity-world settings, generic Unity scene, invalid dependency rejection, interrupted-copy cleanup, nested Marrow catalog folder and ZIP with scene variants, overwrite rollback)."
}
finally {
    $currentRoot = [IO.Path]::GetFullPath($tempRootFull)
    if ($currentRoot.StartsWith($projectPrefix, [StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $currentRoot)) {
        Remove-Item -LiteralPath $currentRoot -Recurse -Force
    }
}
