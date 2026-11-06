$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'Modules\ReleaseArchivePolicy.psm1') -Force

$blockedPaths = @(
    'Maps/course/scene.bundle',
    'Maps/course/Level.unity',
    'Maps/course/terrain.mat',
    'Maps/course/texture.png',
    'Maps/course/normal.DDS',
    'Maps/course/font.woff2',
    'Maps/course/level.fbx',
    'Maps/course/audio.ogg',
    'Maps/course/intro.mp4',
    'Maps/course/download.zip',
    'Maps/course/catalog_windows.json',
    'Maps/course/author.pallet.json'
)

$allowedPaths = @(
    'README.md',
    'Runtime/Patches/ImportedMapRendering.cs',
    'Tools/Samples/SampleMeadowNine.json',
    'BepInEx/plugins/CustomCourseLoader/Courses/SampleMeadowNine.json',
    'CourseEditor/assets/course-icon.svg',
    'package-manifest.json'
)

foreach ($path in $blockedPaths) {
    $violation = Get-ReleaseAssetBoundaryViolation -EntryPath $path
    if ([string]::IsNullOrWhiteSpace($violation)) {
        throw "Release asset policy allowed a forbidden archive path: $path"
    }
}

foreach ($path in $allowedPaths) {
    $violation = Get-ReleaseAssetBoundaryViolation -EntryPath $path
    if (-not [string]::IsNullOrWhiteSpace($violation)) {
        throw "Release asset policy rejected a source/document path '$path' as $violation."
    }
}

$profilePathPrefix = 'C:\' + 'Users' + '\'
$blockedPrivacyExamples = @(
    ('Local log: ' + $profilePathPrefix + 'fixture-user\AppData\Local\DGM\LogOutput.log'),
    ('Build cache: ' + $profilePathPrefix + 'fixture-user\source\obj\project.assets.json')
)
$allowedPrivacyExamples = @(
    'Example input: C:\Users\you\AppData\LocalLow\Stress Level Zero\BONELAB\MODS\Map',
    'Example input: C:\Users\yourname\Downloads\Map.zip',
    'Example input: C:\Users\<username>\Downloads\Map.zip'
)

foreach ($text in $blockedPrivacyExamples) {
    $violation = Get-ReleaseTextBoundaryViolation -Text $text
    if ([string]::IsNullOrWhiteSpace($violation)) {
        throw 'Release text policy allowed a machine-specific Windows user-profile path.'
    }
}

foreach ($text in $allowedPrivacyExamples) {
    $violation = Get-ReleaseTextBoundaryViolation -Text $text
    if (-not [string]::IsNullOrWhiteSpace($violation)) {
        throw "Release text policy rejected a documented placeholder profile path as $violation."
    }
}

Write-Host "Release archive policy checks passed: $($blockedPaths.Count) forbidden and $($allowedPaths.Count) allowed asset paths; $($blockedPrivacyExamples.Count) private profile-path and $($allowedPrivacyExamples.Count) placeholder text cases."
