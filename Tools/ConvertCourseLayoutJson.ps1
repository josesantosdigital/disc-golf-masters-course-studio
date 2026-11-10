[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$LayoutPath,
    [Parameter(Mandatory = $true)][string]$PluginDirectory,
    [switch]$Overwrite
)

$ErrorActionPreference = 'Stop'
$allowedBases = @('CanonPark', 'MinokaSprings', 'BaldurDgc', 'MapleHill', 'ChampionsLanding', 'WinthropGold', 'NokiaDiscGolfPark', 'Terminalen')
$inputPath = (Resolve-Path -LiteralPath $LayoutPath).Path
if ([string]::IsNullOrWhiteSpace($PluginDirectory)) { throw "PluginDirectory must name the target game's CustomCourseLoader folder." }
$pluginRoot = [IO.Path]::GetFullPath($PluginDirectory).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)

function Assert-Text([object]$Value, [string]$Name, [int]$MinimumLength, [int]$MaximumLength) {
    if ($Value -isnot [string]) { throw "$Name must be text." }
    $text = $Value.Trim()
    if ($text.Length -lt $MinimumLength -or $text.Length -gt $MaximumLength) {
        throw "$Name must contain $MinimumLength-$MaximumLength characters."
    }
    return $text
}

function Assert-Number([object]$Value, [string]$Name, [double]$Minimum, [double]$Maximum, [switch]$Integer) {
    if ($null -eq $Value -or $Value -is [bool]) { throw "$Name must be a finite number." }
    $parsed = 0.0
    $text = [Convert]::ToString($Value, [Globalization.CultureInfo]::InvariantCulture)
    if (-not [double]::TryParse($text, [Globalization.NumberStyles]::Float, [Globalization.CultureInfo]::InvariantCulture, [ref]$parsed) -or
        [double]::IsNaN($parsed) -or [double]::IsInfinity($parsed) -or $parsed -lt $Minimum -or $parsed -gt $Maximum) {
        throw "$Name must be between $Minimum and $Maximum."
    }
    if ($Integer -and $parsed -ne [math]::Truncate($parsed)) { throw "$Name must be an integer." }
    if ($Integer) { return [int]$parsed }
    return $parsed
}

function Assert-RelativePath([object]$Value, [string]$Name) {
    if ($Value -isnot [string] -or [string]::IsNullOrWhiteSpace($Value)) { throw "$Name must be a relative path." }
    $path = $Value.Replace('\', '/')
    if ($path.StartsWith('/') -or [IO.Path]::IsPathRooted($Value) -or $path.Split('/') -contains '' -or
        $path.Split('/') -contains '.' -or $path.Split('/') -contains '..') {
        throw "$Name must not be rooted or contain empty, '.' or '..' path segments."
    }
    return $path
}

function Convert-Offset([object]$Position, [string]$Name) {
    if ($null -eq $Position) { throw "$Name is required for courseOffsets coordinates." }
    return [ordered]@{
        right = Assert-Number $Position.right "$Name.right" -2000 2000
        up = Assert-Number $Position.up "$Name.up" -200 200
        forward = Assert-Number $Position.forward "$Name.forward" -2000 2000
    }
}

function Convert-WorldPosition([object]$Position, [string]$Name) {
    if ($null -eq $Position) { throw "$Name is required for unityWorld coordinates." }
    return [ordered]@{
        x = Assert-Number $Position.x "$Name.x" -10000 10000
        y = Assert-Number $Position.y "$Name.y" -10000 10000
        z = Assert-Number $Position.z "$Name.z" -10000 10000
    }
}

function Convert-WorldBounds([object]$Bounds) {
    if ($Bounds -isnot [System.Management.Automation.PSCustomObject]) { throw 'mapWorldBounds must be an object.' }
    $converted = [ordered]@{
        minX = Assert-Number $Bounds.minX 'mapWorldBounds.minX' -10000 10000
        maxX = Assert-Number $Bounds.maxX 'mapWorldBounds.maxX' -10000 10000
        minZ = Assert-Number $Bounds.minZ 'mapWorldBounds.minZ' -10000 10000
        maxZ = Assert-Number $Bounds.maxZ 'mapWorldBounds.maxZ' -10000 10000
        groundY = Assert-Number $Bounds.groundY 'mapWorldBounds.groundY' -10000 10000
    }
    if ($converted.maxX -le $converted.minX -or $converted.maxZ -le $converted.minZ) {
        throw 'mapWorldBounds must have maxX > minX and maxZ > minZ.'
    }
    return $converted
}

function Convert-MapColor([object]$Color, [string]$Name) {
    if ($Color -isnot [System.Management.Automation.PSCustomObject]) { throw "$Name must be an object with r, g, and b." }
    return [ordered]@{
        r = Assert-Number $Color.r "$Name.r" 0 1
        g = Assert-Number $Color.g "$Name.g" 0 1
        b = Assert-Number $Color.b "$Name.b" 0 1
    }
}

function Convert-MapLighting([object]$Lighting) {
    if ($Lighting -isnot [System.Management.Automation.PSCustomObject]) { throw 'mapLighting must be an object.' }
    # Keep this contract aligned with MapLightingDefinition and the interchange schema.
    $booleanProperties = @(
        'preserveLightmaps', 'supplementLighting', 'forceFallbackMaterials', 'forceUnlitMaterials',
        'ignoreSourceTint', 'repairTerrainMaterials', 'repairTerrainTextureDetails', 'useBaseSkybox', 'disableCameraOcclusionCulling',
        'hideHoloWalls', 'forceTreeMeshLods', 'exportFoliageDiagnostics', 'useLitFoliageMaterials',
        'realtimeMapShadows', 'hideImportedParticles', 'generateTreeCanopyColliders',
        'useGameFoliageShader', 'preferBaseCourseSun', 'experimentalFoliageInteractions'
    )
    $numberRanges = @{
        localLightIntensityMultiplier = @(0.0, 5.0)
        albedoBrightness = @(0.5, 3.0)
        ambientIntensity = @(0.0, 2.5)
        keyLightIntensity = @(0.0, 5.0)
        keyLightYawDegrees = @(-360000.0, 360000.0)
        keyLightPitchDegrees = @(-89.0, 89.0)
    }
    $knownProperties = @($booleanProperties) + @($numberRanges.Keys) + @('ambientColor', 'keyLightColor', 'suppressedEmissionTextures', 'hiddenLightFixtureTextures', 'suppressedLightRoots')
    foreach ($property in $Lighting.PSObject.Properties.Name) {
        if ($property -notin $knownProperties) { throw "mapLighting contains unsupported property '$property'." }
    }
    $converted = [ordered]@{}
    foreach ($property in $booleanProperties) {
        $value = $Lighting.$property
        if ($null -ne $value) {
            if ($value -isnot [bool]) { throw "mapLighting.$property must be true or false." }
            $converted[$property] = $value
        }
    }
    foreach ($property in $numberRanges.Keys) {
        $value = $Lighting.$property
        if ($null -ne $value) { $converted[$property] = Assert-Number $value "mapLighting.$property" $numberRanges[$property][0] $numberRanges[$property][1] }
    }
    foreach ($property in @('ambientColor', 'keyLightColor')) {
        $value = $Lighting.$property
        if ($null -ne $value) { $converted[$property] = Convert-MapColor $value "mapLighting.$property" }
    }
    $textures = $Lighting.suppressedEmissionTextures
    if ($null -ne $textures) {
        if ($textures -isnot [array] -or $textures.Count -gt 32) { throw 'mapLighting.suppressedEmissionTextures must be an array of at most 32 names.' }
        foreach ($name in $textures) {
            if ($name -isnot [string] -or [string]::IsNullOrWhiteSpace($name) -or $name.Length -gt 128) { throw 'Invalid suppressed emission texture name.' }
        }
        $converted['suppressedEmissionTextures'] = @($textures)
    }
    $fixtures = $Lighting.hiddenLightFixtureTextures
    if ($null -ne $fixtures) {
        if ($fixtures -isnot [array] -or $fixtures.Count -gt 32) { throw 'mapLighting.hiddenLightFixtureTextures must be an array of at most 32 names.' }
        foreach ($name in $fixtures) {
            if ($name -isnot [string] -or [string]::IsNullOrWhiteSpace($name) -or $name.Length -gt 128) { throw 'Invalid light fixture texture name.' }
        }
        $converted['hiddenLightFixtureTextures'] = @($fixtures)
    }
    $roots = $Lighting.suppressedLightRoots
    if ($null -ne $roots) {
        if ($roots -isnot [array] -or $roots.Count -gt 32) { throw 'mapLighting.suppressedLightRoots must be an array of at most 32 paths.' }
        foreach ($root in $roots) {
            if ($root -isnot [string] -or [string]::IsNullOrWhiteSpace($root) -or $root.Length -gt 256) { throw 'Invalid suppressed light root path.' }
        }
        $converted['suppressedLightRoots'] = @($roots)
    }
    return $converted
}

try {
    $layout = Get-Content -LiteralPath $inputPath -Raw | ConvertFrom-Json
}
catch {
    throw "Could not parse layout JSON '$inputPath': $($_.Exception.Message)"
}
if ($null -eq $layout -or $layout.schemaVersion -ne 1) { throw 'Layout interchange schemaVersion must be 1.' }

$id = Assert-Text $layout.id 'id' 1 48
if ($id -notmatch '^[A-Za-z0-9_-]+$') { throw 'id may contain only ASCII letters, digits, underscores, and hyphens.' }
$name = Assert-Text $layout.name 'name' 1 64
$baseCourse = Assert-Text $layout.baseCourse 'baseCourse' 1 64
if ($baseCourse -notin $allowedBases) { throw "baseCourse '$baseCourse' is not a known DGM built-in course ID." }
$coordinateSpace = [string]$layout.coordinateSpace
if ($coordinateSpace -notin @('courseOffsets', 'unityWorld')) { throw "coordinateSpace must be 'courseOffsets' or 'unityWorld'." }
$sourceGame = ''
if ($null -ne $layout.sourceGame -and -not [string]::IsNullOrWhiteSpace([string]$layout.sourceGame)) {
    $sourceGame = Assert-Text $layout.sourceGame 'sourceGame' 1 80
}
$description = ''
if ($null -ne $layout.description) { $description = Assert-Text $layout.description 'description' 0 160 }
if ($sourceGame -and -not $description) { $description = "Layout converted from $sourceGame." }
if ($sourceGame -and $description -and $description -notmatch [regex]::Escape($sourceGame)) { $description = "$description (ported from $sourceGame)." }
if ($description.Length -gt 180) { $description = $description.Substring(0, 180).TrimEnd() }

$holes = @()
if ($null -ne $layout.holes) { $holes = @($layout.holes) }
if ($holes.Count -lt 1 -or $holes.Count -gt 18) { throw 'holes must contain 1-18 entries.' }
if ($coordinateSpace -eq 'courseOffsets' -and $null -ne $layout.mapScene) {
    throw 'mapScene is only valid with coordinateSpace unityWorld.'
}
if ($coordinateSpace -eq 'unityWorld' -and $null -eq $layout.mapScene) {
    throw 'mapScene is required with coordinateSpace unityWorld.'
}
if (($null -ne $layout.mapLighting -or $null -ne $layout.mapWorldBounds) -and $coordinateSpace -ne 'unityWorld') {
    throw 'mapLighting and mapWorldBounds are only valid with coordinateSpace unityWorld.'
}

$convertedHoles = [System.Collections.Generic.List[object]]::new()
for ($index = 0; $index -lt $holes.Count; $index++) {
    $hole = $holes[$index]
    if ($null -eq $hole) { throw "holes[$index] cannot be null." }
    if ($coordinateSpace -eq 'courseOffsets' -and ($null -ne $hole.teePosition -or $null -ne $hole.basketPosition)) {
        throw "holes[$index] must not mix unityWorld positions with courseOffsets coordinates."
    }
    if ($coordinateSpace -eq 'unityWorld' -and ($null -ne $hole.teeOffset -or $null -ne $hole.basketOffset)) {
        throw "holes[$index] must not mix courseOffsets with unityWorld positions."
    }
    $par = 3
    if ($null -ne $hole.par) { $par = Assert-Number $hole.par "holes[$index].par" 1 8 -Integer }
    $lengthMeters = 60
    if ($null -ne $hole.lengthMeters) { $lengthMeters = Assert-Number $hole.lengthMeters "holes[$index].lengthMeters" 1 2000 -Integer }
    $shotLimit = 0
    if ($null -ne $hole.shotLimit) { $shotLimit = Assert-Number $hole.shotLimit "holes[$index].shotLimit" 0 20 -Integer }

    $pinVariant = 'Default'
    if ($null -ne $hole.pinVariant) { $pinVariant = Assert-Text $hole.pinVariant "holes[$index].pinVariant" 1 64 }
    $teeVariant = 'Default'
    if ($null -ne $hole.teeVariant) { $teeVariant = Assert-Text $hole.teeVariant "holes[$index].teeVariant" 1 64 }
    $converted = [ordered]@{
        par = $par
        lengthMeters = $lengthMeters
        shotLimit = $shotLimit
        pinVariant = $pinVariant
        teeVariant = $teeVariant
        teeYawDegrees = 0
        basketYawDegrees = 0
        teeEnabled = $false
        cardEnabled = $false
        basketEnabled = $true
        obZones = @()
    }
    if ($null -ne $hole.teeYawDegrees) { $converted.teeYawDegrees = Assert-Number $hole.teeYawDegrees "holes[$index].teeYawDegrees" -360000 360000 }
    if ($null -ne $hole.basketYawDegrees) { $converted.basketYawDegrees = Assert-Number $hole.basketYawDegrees "holes[$index].basketYawDegrees" -360000 360000 }
    if ($null -ne $hole.teeEnabled) {
        if ($hole.teeEnabled -isnot [bool]) { throw "holes[$index].teeEnabled must be true or false." }
        $converted.teeEnabled = $hole.teeEnabled
    }
    if ($null -ne $hole.cardEnabled) {
        if ($hole.cardEnabled -isnot [bool]) { throw "holes[$index].cardEnabled must be true or false." }
        $converted.cardEnabled = $hole.cardEnabled
    }
    if ($null -ne $hole.basketEnabled) {
        if ($hole.basketEnabled -isnot [bool]) { throw "holes[$index].basketEnabled must be true or false." }
        $converted.basketEnabled = $hole.basketEnabled
    }

    if ($coordinateSpace -eq 'courseOffsets') {
        $converted.teeOffset = Convert-Offset $hole.teeOffset "holes[$index].teeOffset"
        $converted.basketOffset = Convert-Offset $hole.basketOffset "holes[$index].basketOffset"
    }
    else {
        $converted.mapTeePosition = Convert-WorldPosition $hole.teePosition "holes[$index].teePosition"
        $converted.mapBasketPosition = Convert-WorldPosition $hole.basketPosition "holes[$index].basketPosition"
    }

    $zones = @()
    if ($null -ne $hole.obZones) { $zones = @($hole.obZones) }
    if ($zones.Count -gt 12) { throw "holes[$index].obZones may contain at most 12 zones." }
    $convertedZones = [System.Collections.Generic.List[object]]::new()
    for ($zoneIndex = 0; $zoneIndex -lt $zones.Count; $zoneIndex++) {
        $zone = $zones[$zoneIndex]
        $points = @()
        if ($null -ne $zone.points) { $points = @($zone.points) }
        if ($points.Count -lt 3 -or $points.Count -gt 64) { throw "holes[$index].obZones[$zoneIndex].points must contain 3-64 points." }
        $convertedPoints = [System.Collections.Generic.List[object]]::new()
        foreach ($point in $points) {
            $convertedPoints.Add([ordered]@{
                right = Assert-Number $point.right "holes[$index].obZones[$zoneIndex].points.right" -1000 1000
                forward = Assert-Number $point.forward "holes[$index].obZones[$zoneIndex].points.forward" -1000 1000
            })
        }
        $zoneName = ''
        if ($null -ne $zone.name) { $zoneName = Assert-Text $zone.name "holes[$index].obZones[$zoneIndex].name" 0 64 }
        $convertedZones.Add([ordered]@{ name = $zoneName; points = @($convertedPoints.ToArray()) })
    }
    $converted.obZones = @($convertedZones.ToArray())
    $convertedHoles.Add($converted)
}

$mapScene = $null
if ($coordinateSpace -eq 'unityWorld') {
    $scenePath = Assert-RelativePath $layout.mapScene.scenePath 'mapScene.scenePath'
    if (-not $scenePath.EndsWith('.unity', [StringComparison]::OrdinalIgnoreCase)) { throw 'mapScene.scenePath must end in .unity.' }
    $bundlePath = Assert-RelativePath $layout.mapScene.bundlePath 'mapScene.bundlePath'
    $dependencies = @()
    if ($null -ne $layout.mapScene.dependencyBundlePaths) { $dependencies = @($layout.mapScene.dependencyBundlePaths) }
    $mapScene = [ordered]@{
        scenePath = $scenePath
        bundlePath = $bundlePath
        dependencyBundlePaths = @($dependencies | ForEach-Object { Assert-RelativePath $_ 'mapScene.dependencyBundlePaths' })
    }
}

$courseDirectory = Join-Path $pluginRoot 'Courses'
$coursePath = Join-Path $courseDirectory ($id + '.json')
$cardName = $id + '-card.png'
$cardPath = Join-Path $courseDirectory $cardName
$coursePathExists = Test-Path -LiteralPath $coursePath
$cardPathExists = Test-Path -LiteralPath $cardPath
if (($coursePathExists -and -not (Test-Path -LiteralPath $coursePath -PathType Leaf)) -or
    ($cardPathExists -and -not (Test-Path -LiteralPath $cardPath -PathType Leaf))) {
    throw "A course output path for '$id' exists but is not a file. Remove or rename that directory before converting."
}
$courseExists = $coursePathExists
$cardExists = $cardPathExists
if (($courseExists -or $cardExists) -and -not $Overwrite) {
    throw "Course output already exists for '$id'. Choose a new ID or move the existing files first."
}
New-Item -ItemType Directory -Force -Path $courseDirectory | Out-Null
$suffix = '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
$courseTemp = $coursePath + $suffix
$cardTemp = $cardPath + $suffix
$courseBackup = $coursePath + $suffix + '.backup'
$cardBackup = $cardPath + $suffix + '.backup'
$cardInstalled = $false
$courseInstalled = $false
try {
    if ($Overwrite -and $courseExists) { Copy-Item -LiteralPath $coursePath -Destination $courseBackup }
    if ($Overwrite -and $cardExists) { Copy-Item -LiteralPath $cardPath -Destination $cardBackup }
    $course = [ordered]@{
        schemaVersion = 2
        id = $id
        name = $name
        baseCourse = $baseCourse
        baseLayout = if ($null -ne $layout.baseLayout) { Assert-Text $layout.baseLayout 'baseLayout' 0 120 } else { '' }
        description = $description
        courseCardImage = $cardName
        holes = @($convertedHoles.ToArray())
    }
    if ($null -ne $mapScene) { $course.mapScene = $mapScene }
    if ($null -ne $layout.mapWorldBounds) { $course.mapWorldBounds = Convert-WorldBounds $layout.mapWorldBounds }
    if ($null -ne $layout.mapLighting) { $course.mapLighting = Convert-MapLighting $layout.mapLighting }

    [IO.File]::WriteAllText($courseTemp, ($course | ConvertTo-Json -Depth 24), [Text.UTF8Encoding]::new($false))
    Import-Module (Join-Path $PSScriptRoot 'Modules\CoursePackage.psm1') -Force
    New-DgmCourseCard -Path $cardTemp -Title $name -Style Auto
    Move-Item -LiteralPath $cardTemp -Destination $cardPath -Force
    $cardInstalled = $true
    Move-Item -LiteralPath $courseTemp -Destination $coursePath -Force
    $courseInstalled = $true

    & (Join-Path $PSScriptRoot 'ValidateCoursePackage.ps1') -CoursePath $coursePath -PluginDirectory $pluginRoot
    Write-Host "Converted $coordinateSpace layout to $coursePath"
    Write-Host "Course card: $cardPath"
}
catch {
    if ($courseInstalled -and (Test-Path -LiteralPath $courseBackup -PathType Leaf)) { Copy-Item -LiteralPath $courseBackup -Destination $coursePath -Force }
    elseif ($courseInstalled -and (Test-Path -LiteralPath $coursePath)) { Remove-Item -LiteralPath $coursePath -Force }
    if ($cardInstalled -and (Test-Path -LiteralPath $cardBackup -PathType Leaf)) { Copy-Item -LiteralPath $cardBackup -Destination $cardPath -Force }
    elseif ($cardInstalled -and (Test-Path -LiteralPath $cardPath)) { Remove-Item -LiteralPath $cardPath -Force }
    throw
}
finally {
    if (Test-Path -LiteralPath $courseTemp) { Remove-Item -LiteralPath $courseTemp -Force }
    if (Test-Path -LiteralPath $cardTemp) { Remove-Item -LiteralPath $cardTemp -Force }
    if (Test-Path -LiteralPath $courseBackup) { Remove-Item -LiteralPath $courseBackup -Force }
    if (Test-Path -LiteralPath $cardBackup) { Remove-Item -LiteralPath $cardBackup -Force }
}
