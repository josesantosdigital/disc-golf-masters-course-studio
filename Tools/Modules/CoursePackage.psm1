Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-UnityBundleInfo([string]$Path) {
    $stream = [IO.File]::OpenRead($Path)
    try {
        $buffer = New-Object byte[] 256
        $count = $stream.Read($buffer, 0, $buffer.Length)
        $header = [Text.Encoding]::ASCII.GetString($buffer, 0, $count)
    }
    finally {
        $stream.Dispose()
    }

    if (-not $header.StartsWith('UnityFS', [StringComparison]::Ordinal)) {
        throw "The scene file '$Path' is not a UnityFS AssetBundle."
    }
    $version = [regex]::Match($header, '20\d{2}\.\d+\.\d+f\d+').Value
    return [pscustomobject]@{ Format = 'UnityFS'; UnityVersion = $version }
}

function New-DgmCourseCard([string]$Path, [string]$Title, [string]$Style) {
    Add-Type -AssemblyName System.Drawing
    if ($Style -eq 'Auto') {
        if ($Title -match '(?i)tree|forest|wood|jungle') { $Style = 'Forest' }
        elseif ($Title -match '(?i)hood|street|city|urban') { $Style = 'Urban' }
        else { $Style = 'Abstract' }
    }

    switch ($Style) {
        'Forest' {
            $top = [Drawing.Color]::FromArgb(16, 67, 66)
            $bottom = [Drawing.Color]::FromArgb(18, 28, 42)
            $accent = [Drawing.Color]::FromArgb(96, 213, 166)
        }
        'Urban' {
            $top = [Drawing.Color]::FromArgb(52, 61, 84)
            $bottom = [Drawing.Color]::FromArgb(24, 26, 39)
            $accent = [Drawing.Color]::FromArgb(255, 151, 89)
        }
        default {
            $top = [Drawing.Color]::FromArgb(70, 53, 113)
            $bottom = [Drawing.Color]::FromArgb(24, 26, 47)
            $accent = [Drawing.Color]::FromArgb(106, 216, 220)
        }
    }

    $bitmap = [Drawing.Bitmap]::new(960, 540)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $gradient = $null
    $shade = $null
    $accentBrush = $null
    $whiteBrush = $null
    $mutedBrush = $null
    $badgeFont = $null
    $titleFont = $null
    $subtitleFont = $null
    try {
        $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.TextRenderingHint = [Drawing.Text.TextRenderingHint]::AntiAliasGridFit
        $gradient = [Drawing.Drawing2D.LinearGradientBrush]::new(
            [Drawing.Rectangle]::new(0, 0, 960, 540), $top, $bottom, 35
        )
        $graphics.FillRectangle($gradient, 0, 0, 960, 540)
        for ($ring = 0; $ring -lt 7; $ring++) {
            $ringPen = [Drawing.Pen]::new([Drawing.Color]::FromArgb(45, $accent.R, $accent.G, $accent.B), 2)
            try { $graphics.DrawEllipse($ringPen, 535 - ($ring * 45), 62 - ($ring * 22), 500 + ($ring * 90), 250 + ($ring * 44)) }
            finally { $ringPen.Dispose() }
        }
        if ($Style -eq 'Forest') {
            $treeBrush = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(95, 4, 23, 30))
            try {
                for ($i = 0; $i -lt 14; $i++) {
                    $x = -10 + ($i * 76)
                    $baseY = 355 + (($i % 3) * 19)
                    $points = [Drawing.Point[]]@(
                        [Drawing.Point]::new($x + 32, $baseY - 178), [Drawing.Point]::new($x - 5, $baseY - 55),
                        [Drawing.Point]::new($x + 13, $baseY - 55), [Drawing.Point]::new($x - 14, $baseY),
                        [Drawing.Point]::new($x + 76, $baseY), [Drawing.Point]::new($x + 47, $baseY - 55),
                        [Drawing.Point]::new($x + 65, $baseY - 55)
                    )
                    $graphics.FillPolygon($treeBrush, $points)
                }
            }
            finally { $treeBrush.Dispose() }
        }
        elseif ($Style -eq 'Urban') {
            $buildingBrush = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(110, 10, 14, 26))
            try {
                for ($i = 0; $i -lt 10; $i++) {
                    $x = -15 + ($i * 102)
                    $height = 150 + (($i * 47) % 125)
                    $graphics.FillRectangle($buildingBrush, $x, 325 - $height, 84, $height + 65)
                }
            }
            finally { $buildingBrush.Dispose() }
        }

        $shade = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(225, 14, 16, 27))
        $accentBrush = [Drawing.SolidBrush]::new($accent)
        $whiteBrush = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(250, 247, 248, 252))
        $mutedBrush = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(210, 188, 194, 210))
        $graphics.FillRectangle($shade, 0, 340, 960, 200)
        $graphics.FillRectangle($accentBrush, 0, 340, 14, 200)
        $badgeFont = [Drawing.Font]::new('Arial', 17, [Drawing.FontStyle]::Bold, [Drawing.GraphicsUnit]::Pixel)
        $titleFont = [Drawing.Font]::new('Arial', 49, [Drawing.FontStyle]::Bold, [Drawing.GraphicsUnit]::Pixel)
        $subtitleFont = [Drawing.Font]::new('Arial', 18, [Drawing.FontStyle]::Regular, [Drawing.GraphicsUnit]::Pixel)
        $graphics.DrawString('CUSTOM COURSE', $badgeFont, $accentBrush, 42, 365)
        $displayTitle = $Title.ToUpperInvariant()
        while ($displayTitle.Length -gt 24) { $displayTitle = $displayTitle.Substring(0, $displayTitle.Length - 1) }
        $graphics.DrawString($displayTitle, $titleFont, $whiteBrush, 40, 398)
        $graphics.DrawString('Course Studio preview graphic', $subtitleFont, $mutedBrush, 43, 472)
        $bitmap.Save($Path, [Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        foreach ($resource in @($badgeFont, $titleFont, $subtitleFont, $gradient, $shade, $accentBrush, $whiteBrush, $mutedBrush, $graphics, $bitmap)) {
            if ($null -ne $resource) { $resource.Dispose() }
        }
    }
}

function New-DgmCoursePackage {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$SceneBundlePath,
        [Parameter(Mandatory = $true)][string]$ScenePath,
        [Parameter(Mandatory = $true)][ValidatePattern('^[A-Za-z0-9_-]{1,48}$')][string]$Id,
        [Parameter(Mandatory = $true)][ValidateLength(1, 64)][string]$CourseName,
        [ValidateSet('CanonPark', 'MinokaSprings', 'BaldurDgc', 'MapleHill', 'ChampionsLanding', 'WinthropGold', 'NokiaDiscGolfPark', 'Terminalen')][string]$BaseCourse = 'CanonPark',
        [ValidateRange(1, 18)][int]$HoleCount = 9,
        [string[]]$DependencyBundlePaths = @(),
        [Parameter(Mandatory = $true)][string]$PluginDirectory,
        [ValidateSet('Auto', 'Forest', 'Urban', 'Abstract')][string]$PreviewStyle = 'Auto'
    )

    $sceneSource = (Resolve-Path -LiteralPath $SceneBundlePath).Path
    if (-not (Test-Path -LiteralPath $sceneSource -PathType Leaf)) { throw "Scene bundle not found: $SceneBundlePath" }
    $sceneAssetPath = $ScenePath.Trim().Replace('\', '/')
    if ([string]::IsNullOrWhiteSpace($sceneAssetPath) -or $sceneAssetPath.StartsWith('/') -or
        [IO.Path]::IsPathRooted($sceneAssetPath) -or
        $sceneAssetPath.Split('/') -contains '..' -or $sceneAssetPath.Split('/') -contains '.' -or
        -not $sceneAssetPath.EndsWith('.unity', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'ScenePath must be a relative .unity asset path inside the scene bundle.'
    }
    $bundleInfo = Get-UnityBundleInfo $sceneSource

    $dependencySources = [System.Collections.Generic.List[object]]::new()
    $dependencyNames = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($dependencyPath in $DependencyBundlePaths) {
        if ([string]::IsNullOrWhiteSpace($dependencyPath)) { continue }
        $dependencyFullPath = (Resolve-Path -LiteralPath $dependencyPath).Path
        if (-not (Test-Path -LiteralPath $dependencyFullPath -PathType Leaf)) { throw "Dependency bundle not found: $dependencyPath" }
        if ([string]::Equals($dependencyFullPath, $sceneSource, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'The scene bundle cannot also be listed as a dependency.'
        }
        $fileName = [IO.Path]::GetFileName($dependencyFullPath)
        if (-not $dependencyNames.Add($fileName)) { throw "Dependency bundle filenames must be unique: $fileName" }
        $dependencySources.Add([pscustomobject]@{ Path = $dependencyFullPath; Name = $fileName })
    }

    $pluginRoot = [IO.Path]::GetFullPath($PluginDirectory).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    $mapsRoot = Join-Path $pluginRoot 'Maps'
    $coursesRoot = Join-Path $pluginRoot 'Courses'
    $mapOutput = Join-Path $mapsRoot $Id
    $courseOutput = Join-Path $coursesRoot ($Id + '.json')
    $cardName = $Id + '-card.png'
    $cardOutput = Join-Path $coursesRoot $cardName
    if ((Test-Path -LiteralPath $mapOutput) -or (Test-Path -LiteralPath $courseOutput) -or (Test-Path -LiteralPath $cardOutput)) {
        throw "Package output already exists for '$Id'. Choose a new ID or move the existing package first."
    }

    New-Item -ItemType Directory -Force -Path $mapsRoot, $coursesRoot | Out-Null
    $stagingRoot = Join-Path $mapsRoot ('.' + $Id + '.' + [Guid]::NewGuid().ToString('N') + '.tmp')
    $tempSuffix = '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
    $courseJsonTemp = $courseOutput + $tempSuffix
    $courseCardTemp = $cardOutput + $tempSuffix
    $mapInstalled = $false
    $jsonInstalled = $false
    $cardInstalled = $false
    try {
        New-Item -ItemType Directory -Force -Path $stagingRoot | Out-Null
        Copy-Item -LiteralPath $sceneSource -Destination (Join-Path $stagingRoot 'scene.bundle')
        $dependencyOutputPaths = [System.Collections.Generic.List[string]]::new()
        if ($dependencySources.Count -gt 0) {
            $dependencyStage = Join-Path $stagingRoot 'dependencies'
            New-Item -ItemType Directory -Force -Path $dependencyStage | Out-Null
            foreach ($dependency in $dependencySources) {
                $null = Get-UnityBundleInfo $dependency.Path
                Copy-Item -LiteralPath $dependency.Path -Destination (Join-Path $dependencyStage $dependency.Name)
                $dependencyOutputPaths.Add("Maps/$Id/dependencies/$($dependency.Name)")
            }
        }

        $holes = @((1..$HoleCount | ForEach-Object { [ordered]@{ par = 3; lengthMeters = 60 } }))
        $course = [ordered]@{
            schemaVersion = 2
            id = $Id
            name = $CourseName.Trim()
            baseCourse = $BaseCourse
            description = "Imported Unity scene course from $($CourseName.Trim()). Set tee and basket positions in Course Studio."
            courseCardImage = $cardName
            holes = $holes
            mapScene = [ordered]@{
                scenePath = $sceneAssetPath
                bundlePath = "Maps/$Id/scene.bundle"
                dependencyBundlePaths = @($dependencyOutputPaths.ToArray())
            }
        }
        [IO.File]::WriteAllText($courseJsonTemp, ($course | ConvertTo-Json -Depth 12), [Text.UTF8Encoding]::new($false))
        New-DgmCourseCard -Path $courseCardTemp -Title $CourseName.Trim() -Style $PreviewStyle

        Move-Item -LiteralPath $stagingRoot -Destination $mapOutput
        $mapInstalled = $true
        Move-Item -LiteralPath $courseCardTemp -Destination $cardOutput
        $cardInstalled = $true
        Move-Item -LiteralPath $courseJsonTemp -Destination $courseOutput
        $jsonInstalled = $true

        return [pscustomobject]@{
            Id = $Id
            CoursePath = $courseOutput
            MapPath = $mapOutput
            CourseCardPath = $cardOutput
            ScenePath = $sceneAssetPath
            UnityVersion = $bundleInfo.UnityVersion
            HoleCount = $HoleCount
            DependencyCount = $dependencySources.Count
        }
    }
    catch {
        if ($jsonInstalled -and (Test-Path -LiteralPath $courseOutput)) { Remove-Item -LiteralPath $courseOutput -Force }
        if ($cardInstalled -and (Test-Path -LiteralPath $cardOutput)) { Remove-Item -LiteralPath $cardOutput -Force }
        if ($mapInstalled -and (Test-Path -LiteralPath $mapOutput)) { Remove-Item -LiteralPath $mapOutput -Recurse -Force }
        throw
    }
    finally {
        if (Test-Path -LiteralPath $stagingRoot) { Remove-Item -LiteralPath $stagingRoot -Recurse -Force }
        if (Test-Path -LiteralPath $courseJsonTemp) { Remove-Item -LiteralPath $courseJsonTemp -Force }
        if (Test-Path -LiteralPath $courseCardTemp) { Remove-Item -LiteralPath $courseCardTemp -Force }
    }
}

Export-ModuleMember -Function New-DgmCoursePackage, New-DgmCourseCard
