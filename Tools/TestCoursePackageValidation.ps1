[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$validator = Join-Path $PSScriptRoot 'ValidateCoursePackage.ps1'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('dgm-course-validation-' + [Guid]::NewGuid().ToString('N'))
$pluginRoot = Join-Path $testRoot 'plugin'
$courseDirectory = Join-Path $pluginRoot 'Courses'
$mapDirectory = Join-Path $pluginRoot 'Maps\validation-smoke'
$coursePath = Join-Path $courseDirectory 'validation-smoke.json'
$bundlePath = Join-Path $mapDirectory 'scene.bundle'
$dependencyPath = Join-Path $mapDirectory 'dependencies\dependency.bundle'

function Write-ValidFixture {
    $document = [ordered]@{
        schemaVersion = 2
        id = 'validation-smoke'
        name = 'Validation Smoke Course'
        baseCourse = 'CanonPark'
        courseCardImage = 'course-card.png'
        holes = @(@{
            par = 3
            lengthMeters = 60
            cardImage = 'hole-card.png'
        })
        mapScene = [ordered]@{
            scenePath = 'Assets/Scenes/Validation.unity'
            bundlePath = 'Maps/validation-smoke/scene.bundle'
            dependencyBundlePaths = @('Maps/validation-smoke/dependencies/dependency.bundle')
        }
    }
    [IO.File]::WriteAllText($coursePath, ($document | ConvertTo-Json -Depth 12), [Text.UTF8Encoding]::new($false))
    $document
}

function Assert-ValidationFails([string]$ExpectedText) {
    $failed = $false
    $output = @()
    try {
        $output = @(& $validator -CoursePath $coursePath -PluginDirectory $pluginRoot 2>&1)
    }
    catch {
        $failed = $true
        $output += $_.Exception.Message
    }
    if (-not $failed) { throw "Expected validation to fail with '$ExpectedText'." }
    $message = ($output | Out-String)
    if ($message -notmatch [regex]::Escape($ExpectedText)) {
        throw "Validation failed for an unexpected reason. Expected '$ExpectedText'; got: $message"
    }
}

try {
    New-Item -ItemType Directory -Force -Path $courseDirectory, (Join-Path $mapDirectory 'dependencies') | Out-Null
    [IO.File]::WriteAllText($bundlePath, 'UnityFS 2021.3.16f1 synthetic test bundle', [Text.Encoding]::ASCII)
    [IO.File]::WriteAllText($dependencyPath, 'UnityFS 2021.3.16f1 synthetic dependency', [Text.Encoding]::ASCII)
    [IO.File]::WriteAllBytes((Join-Path $courseDirectory 'course-card.png'), [byte[]]@(1, 2, 3))
    [IO.File]::WriteAllBytes((Join-Path $courseDirectory 'hole-card.png'), [byte[]]@(1, 2, 3))
    $valid = Write-ValidFixture

    & $validator -CoursePath $coursePath -PluginDirectory $pluginRoot | Out-Null

    $valid.autoLayout = 'long'
    [IO.File]::WriteAllText($coursePath, ($valid | ConvertTo-Json -Depth 12), [Text.UTF8Encoding]::new($false))
    & $validator -CoursePath $coursePath -PluginDirectory $pluginRoot | Out-Null
    $valid.holes[0].mapTeePosition = @{ x = 1; y = 2; z = 3 }
    [IO.File]::WriteAllText($coursePath, ($valid | ConvertTo-Json -Depth 12), [Text.UTF8Encoding]::new($false))
    Assert-ValidationFails 'autoLayout cannot be combined with explicit map tee or basket positions'
    $valid = Write-ValidFixture

    [IO.File]::WriteAllText($coursePath, '{', [Text.UTF8Encoding]::new($false))
    Assert-ValidationFails 'Could not parse course JSON'
    $valid = Write-ValidFixture

    Remove-Item -LiteralPath $bundlePath
    Assert-ValidationFails 'mapScene.bundlePath file is missing'
    [IO.File]::WriteAllText($bundlePath, 'UnityFS 2021.3.16f1 synthetic test bundle', [Text.Encoding]::ASCII)

    [IO.File]::WriteAllText($bundlePath, 'not a UnityFS scene bundle', [Text.Encoding]::ASCII)
    Assert-ValidationFails 'mapScene bundle does not have a UnityFS header'
    [IO.File]::WriteAllText($bundlePath, 'UnityFS 2021.3.16f1 synthetic test bundle', [Text.Encoding]::ASCII)

    Remove-Item -LiteralPath $dependencyPath
    Assert-ValidationFails 'mapScene.dependencyBundlePaths file is missing'
    [IO.File]::WriteAllText($dependencyPath, 'UnityFS 2021.3.16f1 synthetic dependency', [Text.Encoding]::ASCII)
    [IO.File]::WriteAllText($dependencyPath, 'not a UnityFS dependency bundle', [Text.Encoding]::ASCII)
    Assert-ValidationFails 'mapScene.dependencyBundlePaths bundle does not have a UnityFS header'
    [IO.File]::WriteAllText($dependencyPath, 'UnityFS 2021.3.16f1 synthetic dependency', [Text.Encoding]::ASCII)

    Remove-Item -LiteralPath (Join-Path $courseDirectory 'hole-card.png')
    Assert-ValidationFails 'hole-card.png'
    [IO.File]::WriteAllBytes((Join-Path $courseDirectory 'hole-card.png'), [byte[]]@(1, 2, 3))

    $valid.mapScene.scenePath = '../outside.unity'
    $valid.mapScene.bundlePath = '../outside.bundle'
    [IO.File]::WriteAllText($coursePath, ($valid | ConvertTo-Json -Depth 12), [Text.UTF8Encoding]::new($false))
    Assert-ValidationFails 'safe relative .unity asset path'
    $valid = Write-ValidFixture

    $duplicate = $valid | ConvertTo-Json -Depth 12 | ConvertFrom-Json
    $duplicate.id = 'VALIDATION-SMOKE'
    $duplicatePath = Join-Path $courseDirectory 'duplicate.json'
    [IO.File]::WriteAllText($duplicatePath, ($duplicate | ConvertTo-Json -Depth 12), [Text.UTF8Encoding]::new($false))
    Assert-ValidationFails 'Duplicate course id'

    Write-Output 'Course package validation smoke checks passed: valid, malformed JSON, missing bundle/dependency, invalid scene/dependency headers, missing image, unsafe paths, duplicate ID.'
}
finally {
    $testFullPath = [IO.Path]::GetFullPath($testRoot)
    $tempPrefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if ($testFullPath.StartsWith($tempPrefix, [StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $testFullPath)) {
        Remove-Item -LiteralPath $testFullPath -Recurse -Force
    }
}
