[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$CoursePath,
    [Parameter(Mandatory = $true)][string]$PluginDirectory
)

$ErrorActionPreference = 'Stop'
$allowedBases = @('CanonPark', 'MinokaSprings', 'BaldurDgc', 'MapleHill', 'ChampionsLanding', 'WinthropGold', 'NokiaDiscGolfPark', 'Terminalen')
$errors = [System.Collections.Generic.List[string]]::new()
$warnings = [System.Collections.Generic.List[string]]::new()
$courseFullPath = (Resolve-Path -LiteralPath $CoursePath).Path
$courseDirectory = Split-Path -Parent $courseFullPath
$pluginRoot = [IO.Path]::GetFullPath($PluginDirectory).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)

function Add-Error([string]$Message) { $script:errors.Add($Message) }
function Add-Warning([string]$Message) { $script:warnings.Add($Message) }
function Test-Number($Value) {
    if ($null -eq $Value -or $Value -is [bool]) { return $false }
    [double]$parsed = 0
    $invariant = [Globalization.CultureInfo]::InvariantCulture
    $text = [System.Convert]::ToString($Value, $invariant)
    return ([double]::TryParse($text, [Globalization.NumberStyles]::Float, $invariant, [ref]$parsed) -and
        -not [double]::IsNaN($parsed) -and -not [double]::IsInfinity($parsed))
}
function Test-RelativePackagePath([string]$Value) {
    if ([string]::IsNullOrWhiteSpace($Value) -or [IO.Path]::IsPathRooted($Value) -or $Value.StartsWith('/') -or $Value.StartsWith('\')) { return $false }
    $segments = $Value.Replace('\', '/').Split('/')
    return -not ($segments -contains '' -or $segments -contains '.' -or $segments -contains '..')
}
function Resolve-PackageFile([string]$RelativePath, [string]$Label) {
    if (-not (Test-RelativePackagePath $RelativePath)) {
        Add-Error "$Label must be a safe relative path: $RelativePath"
        return $null
    }
    $candidate = [IO.Path]::GetFullPath((Join-Path $pluginRoot ($RelativePath.Replace('/', [IO.Path]::DirectorySeparatorChar))))
    $prefix = $pluginRoot + [IO.Path]::DirectorySeparatorChar
    if (-not $candidate.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        Add-Error "$Label escapes the plugin folder: $RelativePath"
        return $null
    }
    if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
        Add-Error "$Label file is missing: $candidate"
        return $null
    }
    return $candidate
}
function Test-UnityFsBundle([string]$Path) {
    $stream = [IO.File]::OpenRead($Path)
    try {
        $header = New-Object byte[] 7
        $read = $stream.Read($header, 0, $header.Length)
        return $read -eq 7 -and [Text.Encoding]::ASCII.GetString($header, 0, $read) -eq 'UnityFS'
    }
    finally { $stream.Dispose() }
}

try {
    $course = Get-Content -LiteralPath $courseFullPath -Raw | ConvertFrom-Json
}
catch {
    throw "Could not parse course JSON '$courseFullPath': $($_.Exception.Message)"
}

if ($course.schemaVersion -notin @(1, 2)) { Add-Error 'schemaVersion must be 1 or 2.' }
if ($null -ne $course.mapLighting -and $null -ne $course.mapLighting.suppressedEmissionTextures) {
    $names = $course.mapLighting.suppressedEmissionTextures
    if ($names -isnot [array] -or $names.Count -gt 32) { Add-Error 'mapLighting.suppressedEmissionTextures must be an array of at most 32 texture names.' }
    else {
        foreach ($name in $names) {
            if ($name -isnot [string] -or [string]::IsNullOrWhiteSpace($name) -or $name.Length -gt 128) { Add-Error 'Invalid suppressed emission texture name.' }
        }
    }
}
if ($null -ne $course.mapLighting -and $null -ne $course.mapLighting.hiddenLightFixtureTextures) {
    $names = $course.mapLighting.hiddenLightFixtureTextures
    if ($names -isnot [array] -or $names.Count -gt 32) { Add-Error 'mapLighting.hiddenLightFixtureTextures must be an array of at most 32 texture names.' }
    else {
        foreach ($name in $names) {
            if ($name -isnot [string] -or [string]::IsNullOrWhiteSpace($name) -or $name.Length -gt 128) { Add-Error 'Invalid light fixture texture name.' }
        }
    }
}
if ($null -ne $course.mapLighting -and $null -ne $course.mapLighting.suppressedLightRoots) {
    $roots = $course.mapLighting.suppressedLightRoots
    if ($roots -isnot [array] -or $roots.Count -gt 32) { Add-Error 'mapLighting.suppressedLightRoots must be an array of at most 32 hierarchy paths.' }
    else {
        foreach ($root in $roots) {
            if ($root -isnot [string] -or [string]::IsNullOrWhiteSpace($root) -or $root.Length -gt 256) { Add-Error 'Invalid suppressed light root path.' }
        }
    }
}
if ($course.id -isnot [string] -or $course.id -notmatch '^[A-Za-z0-9_-]{1,48}$') {
    Add-Error 'id must contain 1-48 ASCII letters, digits, underscores, or hyphens.'
}
else {
    foreach ($otherCourseFile in Get-ChildItem -LiteralPath $courseDirectory -Filter '*.json' -File) {
        if ([string]::Equals($otherCourseFile.FullName, $courseFullPath, [StringComparison]::OrdinalIgnoreCase)) { continue }
        try {
            $otherCourse = Get-Content -LiteralPath $otherCourseFile.FullName -Raw | ConvertFrom-Json
            if ($otherCourse.id -is [string] -and
                [string]::Equals($otherCourse.id, $course.id, [StringComparison]::OrdinalIgnoreCase)) {
                Add-Error "Duplicate course id '$($course.id)' also appears in '$($otherCourseFile.Name)'."
            }
        }
        catch {
            # A malformed unrelated file is reported when that course is validated directly.
        }
    }
}
if ([string]::IsNullOrWhiteSpace([string]$course.name) -or $course.name.Length -gt 64) { Add-Error 'name must contain 1-64 characters.' }
if ($course.baseCourse -notin $allowedBases) { Add-Error "baseCourse '$($course.baseCourse)' is not a known DGM built-in ID." }
$holes = @()
if ($null -ne $course.holes) { $holes = @($course.holes) }
if ($holes.Count -lt 1 -or $holes.Count -gt 18) { Add-Error 'holes must contain 1-18 entries.' }
if (-not [string]::IsNullOrWhiteSpace([string]$course.autoLayout)) {
    if ($course.autoLayout -ne 'long') { Add-Error "autoLayout must be empty or 'long'." }
    if ($null -eq $course.mapScene) { Add-Error 'autoLayout requires an imported mapScene.' }
    if (@($holes | Where-Object { $null -ne $_.mapTeePosition -or $null -ne $_.mapBasketPosition }).Count -gt 0) {
        Add-Error 'autoLayout cannot be combined with explicit map tee or basket positions.'
    }
}

if (-not [string]::IsNullOrWhiteSpace([string]$course.courseCardImage)) {
    if ([IO.Path]::GetFileName([string]$course.courseCardImage) -cne [string]$course.courseCardImage -or $course.courseCardImage -notmatch '\.png$') {
        Add-Error 'courseCardImage must be a PNG filename beside the course JSON.'
    }
    elseif (-not (Test-Path -LiteralPath (Join-Path $courseDirectory $course.courseCardImage) -PathType Leaf)) {
        Add-Error "Course card image is missing: $($course.courseCardImage)"
    }
}

$holeIndex = 0
foreach ($hole in $holes) {
    if ($null -eq $hole) { Add-Error "holes[$holeIndex] is null."; $holeIndex++; continue }
    if (-not (Test-Number $hole.par) -or [double]$hole.par -lt 1 -or [double]$hole.par -gt 8 -or [double]$hole.par -ne [math]::Truncate([double]$hole.par)) {
        Add-Error "holes[$holeIndex].par must be an integer from 1 to 8."
    }
    if (-not (Test-Number $hole.lengthMeters) -or [double]$hole.lengthMeters -lt 1 -or [double]$hole.lengthMeters -gt 2000 -or [double]$hole.lengthMeters -ne [math]::Truncate([double]$hole.lengthMeters)) {
        Add-Error "holes[$holeIndex].lengthMeters must be an integer from 1 to 2000."
    }
    if ($null -ne $hole.shotLimit -and (-not (Test-Number $hole.shotLimit) -or [double]$hole.shotLimit -lt 0 -or [double]$hole.shotLimit -gt 20 -or [double]$hole.shotLimit -ne [math]::Truncate([double]$hole.shotLimit))) {
        Add-Error "holes[$holeIndex].shotLimit must be an integer from 0 to 20."
    }
    foreach ($visibility in @('teeEnabled', 'cardEnabled', 'basketEnabled')) {
        $value = $hole.$visibility
        if ($null -ne $value -and $value -isnot [bool]) { Add-Error "holes[$holeIndex].$visibility must be true or false." }
    }
    $zones = @()
    if ($null -ne $hole.obZones) { $zones = @($hole.obZones) }
    if ($zones.Count -gt 12) { Add-Error "holes[$holeIndex].obZones may contain at most 12 zones." }
    $zoneIndex = 0
    foreach ($zone in $zones) {
        $points = @()
        if ($null -ne $zone.points) { $points = @($zone.points) }
        if ($points.Count -lt 3 -or $points.Count -gt 64) { Add-Error "holes[$holeIndex].obZones[$zoneIndex].points must contain 3-64 points." }
        foreach ($point in $points) {
            if (-not (Test-Number $point.right) -or -not (Test-Number $point.forward) -or [math]::Abs([double]$point.right) -gt 1000 -or [math]::Abs([double]$point.forward) -gt 1000) {
                Add-Error "holes[$holeIndex].obZones[$zoneIndex] has invalid right/forward coordinates."
            }
        }
        $zoneIndex++
    }
    if (-not [string]::IsNullOrWhiteSpace([string]$hole.cardImage)) {
        if ([IO.Path]::GetFileName([string]$hole.cardImage) -cne [string]$hole.cardImage -or $hole.cardImage -notmatch '\.png$') {
            Add-Error "holes[$holeIndex].cardImage must be a PNG filename beside the course JSON."
        }
        elseif (-not (Test-Path -LiteralPath (Join-Path $courseDirectory $hole.cardImage) -PathType Leaf)) {
            Add-Error "holes[$holeIndex].cardImage is missing: $($hole.cardImage)"
        }
    }
    $hasTee = $null -ne $hole.mapTeePosition
    $hasBasket = $null -ne $hole.mapBasketPosition
    if ($hasTee -ne $hasBasket) { Add-Error "holes[$holeIndex] must include both mapTeePosition and mapBasketPosition, or neither." }
    foreach ($positionName in @('mapTeePosition', 'mapBasketPosition')) {
        $position = $hole.$positionName
        if ($null -ne $position) {
            foreach ($axis in @('x', 'y', 'z')) {
                if (-not (Test-Number $position.$axis) -or [math]::Abs([double]$position.$axis) -gt 10000) {
                    Add-Error "holes[$holeIndex].$positionName.$axis is outside the supported range."
                }
            }
        }
    }
    $holeIndex++
}

if ($null -ne $course.mapScene) {
    if ([string]::IsNullOrWhiteSpace([string]$course.mapScene.scenePath) -or $course.mapScene.scenePath -notmatch '\.unity$' -or
        -not (Test-RelativePackagePath ([string]$course.mapScene.scenePath))) {
        Add-Error 'mapScene.scenePath must be a safe relative .unity asset path inside the bundle.'
    }
    $sceneBundle = Resolve-PackageFile ([string]$course.mapScene.bundlePath) 'mapScene.bundlePath'
    if ($sceneBundle -and -not (Test-UnityFsBundle $sceneBundle)) {
        Add-Error 'mapScene bundle does not have a UnityFS header.'
    }
    $dependencies = @()
    if ($null -ne $course.mapScene.dependencyBundlePaths) { $dependencies = @($course.mapScene.dependencyBundlePaths) }
    foreach ($dependency in $dependencies) {
        $dependencyBundle = Resolve-PackageFile ([string]$dependency) 'mapScene.dependencyBundlePaths'
        if ($dependencyBundle -and -not (Test-UnityFsBundle $dependencyBundle)) {
            Add-Error "mapScene.dependencyBundlePaths bundle does not have a UnityFS header: $dependency"
        }
    }
    $hasMapPositions = @($holes | Where-Object { $null -ne $_.mapTeePosition }).Count
    if ($hasMapPositions -gt 0 -and $hasMapPositions -ne $holes.Count) { Add-Error 'Supply imported-map positions for every hole or omit them from every hole.' }
    if ($null -ne $course.mapWorldBounds) {
        if (-not (Test-Number $course.mapWorldBounds.minX) -or -not (Test-Number $course.mapWorldBounds.maxX) -or
            -not (Test-Number $course.mapWorldBounds.minZ) -or -not (Test-Number $course.mapWorldBounds.maxZ) -or
            -not (Test-Number $course.mapWorldBounds.groundY) -or
            [double]$course.mapWorldBounds.maxX -le [double]$course.mapWorldBounds.minX -or
            [double]$course.mapWorldBounds.maxZ -le [double]$course.mapWorldBounds.minZ) {
            Add-Error 'mapWorldBounds must contain finite values with maxX > minX and maxZ > minZ.'
        }
    }
}
elseif (@($holes | Where-Object { $null -ne $_.mapTeePosition -or $null -ne $_.mapBasketPosition }).Count -gt 0) {
    Add-Error 'Imported map positions require mapScene.'
}

Write-Host "Course: $($course.name) [$($course.id)]"
Write-Host "Holes: $($holes.Count)"
foreach ($warning in $warnings) { Write-Warning $warning }
foreach ($message in $errors) { Write-Error $message -ErrorAction Continue }
if ($errors.Count -gt 0) { throw "Course validation failed with $($errors.Count) error(s): $($errors -join ' | ')" }
Write-Host 'Course package validation passed.'
