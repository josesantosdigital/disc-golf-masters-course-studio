[CmdletBinding()]
param(
    [string]$GameRoot,
    [string]$OutputDirectory,
    [switch]$Draft
)

$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath($PSScriptRoot)
if ([string]::IsNullOrWhiteSpace($GameRoot)) { $GameRoot = (Resolve-Path -LiteralPath (Join-Path $projectRoot '..')).Path }
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { $OutputDirectory = Join-Path $projectRoot 'dist' }
$gameRootFull = [IO.Path]::GetFullPath($GameRoot).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
$outputFull = [IO.Path]::GetFullPath($OutputDirectory)
$pluginVersionSource = Get-ChildItem -LiteralPath $projectRoot -Recurse -Filter 'Plugin.cs' -File | Select-Object -First 1
if ($null -eq $pluginVersionSource) { throw 'Could not find Plugin.cs to read the plugin version.' }
$versionMatch = Select-String -Path $pluginVersionSource.FullName -Pattern 'PluginVersion\s*=\s*"([^"]+)"' | Select-Object -First 1
if ($null -eq $versionMatch) { throw 'Could not read PluginVersion from Plugin.cs.' }
$version = $versionMatch.Matches[0].Groups[1].Value
$suffix = if ($Draft) { '-DRAFT' } else { '' }
$packageName = "DiscGolfMasters-CourseStudio-$version$suffix"
$archivePath = Join-Path $outputFull ($packageName + '.zip')

if (-not $Draft -and -not (Test-Path -LiteralPath (Join-Path $projectRoot 'LICENSE') -PathType Leaf)) {
    throw 'A public package requires a project LICENSE file. Use -Draft to create a local review archive during release validation.'
}
if (-not $Draft) {
    $checklistPath = Join-Path $projectRoot 'docs\RELEASE_CHECKLIST.md'
    if (-not (Test-Path -LiteralPath $checklistPath -PathType Leaf)) {
        throw 'A public package requires docs/RELEASE_CHECKLIST.md with all release checks complete.'
    }
    $openItems = @(Select-String -LiteralPath $checklistPath -Pattern '^\s*-\s*\[\s\]')
    if ($openItems.Count -gt 0) {
        throw "A public package requires every release checklist item to be complete; $($openItems.Count) item(s) remain open. Use -Draft for a local review archive."
    }
}
if (Test-Path -LiteralPath $archivePath) { throw "Release archive already exists: $archivePath" }
New-Item -ItemType Directory -Force -Path $outputFull | Out-Null

$localDotnet = Join-Path $projectRoot '.tools\dotnet\dotnet.exe'
$pathDotnet = Get-Command dotnet.exe -ErrorAction SilentlyContinue
$pathHasCurrentSdk = $false
if ($null -ne $pathDotnet) {
    $pathSdkList = @(& $pathDotnet.Source --list-sdks)
    $pathHasCurrentSdk = @($pathSdkList | Where-Object { $_ -match '^(10|[1-9]\d)\.\d+\.\d+\s' }).Count -gt 0
}
$dotnetCommand = if ($pathHasCurrentSdk) { $pathDotnet.Source } elseif (Test-Path -LiteralPath $localDotnet) { $localDotnet } elseif ($null -ne $pathDotnet) { $pathDotnet.Source } else { throw 'Install the .NET 10 SDK or place the legacy .NET 6 SDK under .tools\dotnet.' }
$taskDotnetCliHome = Join-Path $projectRoot '.tools\cli-home'
New-Item -ItemType Directory -Force -Path $taskDotnetCliHome | Out-Null
$previousDotnetCliHome = $env:DOTNET_CLI_HOME
$previousSkipFirstTime = $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE
$previousTelemetryOptOut = $env:DOTNET_CLI_TELEMETRY_OPTOUT
try {
    $env:DOTNET_CLI_HOME = $taskDotnetCliHome
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    & $dotnetCommand build (Join-Path $projectRoot 'CustomCourseLoader.csproj') --configuration Release "-p:GameRoot=$gameRootFull" '-p:CheckEolTargetFramework=false'
    if ($LASTEXITCODE -ne 0) { throw "Release build failed with exit code $LASTEXITCODE." }
}
finally {
    $env:DOTNET_CLI_HOME = $previousDotnetCliHome
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = $previousSkipFirstTime
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = $previousTelemetryOptOut
}
$builtDll = Join-Path $projectRoot 'bin\Release\net6.0\CustomCourseLoader.dll'
if (-not (Test-Path -LiteralPath $builtDll -PathType Leaf)) { throw "Build output DLL is missing: $builtDll" }

$stagingRoot = Join-Path $outputFull ('.' + $packageName + '.' + [Guid]::NewGuid().ToString('N') + '.staging')
$stagingFull = [IO.Path]::GetFullPath($stagingRoot)
$outputPrefix = $outputFull.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (-not $stagingFull.StartsWith($outputPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Staging path escaped the release output directory.' }

function Copy-FileIntoPackage([string]$Source, [string]$RelativeDestination) {
    $destination = Join-Path $stagingRoot $RelativeDestination
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination) | Out-Null
    Copy-Item -LiteralPath $Source -Destination $destination -Force
}
function Get-RelativeFilePath([string]$Root, [string]$Path) {
    $prefix = [IO.Path]::GetFullPath($Root).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    $fullPath = [IO.Path]::GetFullPath($Path)
    if (-not $fullPath.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { throw "File is outside source root: $Path" }
    return $fullPath.Substring($prefix.Length)
}

try {
    New-Item -ItemType Directory -Force -Path (Join-Path $stagingRoot 'BepInEx\plugins\CustomCourseLoader'), (Join-Path $stagingRoot 'Source\CustomCourseLoader') | Out-Null
    Copy-FileIntoPackage $builtDll 'BepInEx\plugins\CustomCourseLoader\CustomCourseLoader.dll'

    $editorSource = Join-Path $projectRoot 'CourseEditor'
    foreach ($file in Get-ChildItem -LiteralPath $editorSource -File -Recurse) {
        $relative = Get-RelativeFilePath $editorSource $file.FullName
        if ($relative -ieq 'catalog.js' -or $relative -match '^assets[\\/]maps[\\/]') { continue }
        Copy-FileIntoPackage $file.FullName ('BepInEx\plugins\CustomCourseLoader\CourseEditor\' + $relative)
    }
    $catalogPlaceholder = 'window.DGM_BUILTIN_COURSES = { version: 1, courses: [] };' + [Environment]::NewLine
    [IO.File]::WriteAllText((Join-Path $stagingRoot 'BepInEx\plugins\CustomCourseLoader\CourseEditor\catalog.js'), $catalogPlaceholder, [Text.UTF8Encoding]::new($false))
    Copy-FileIntoPackage (Join-Path $projectRoot 'Tools\Samples\SampleMeadowNine.json') 'BepInEx\plugins\CustomCourseLoader\Courses\SampleMeadowNine.json'

    foreach ($item in @('.gitignore', '.gitattributes', 'README.md', 'CHANGELOG.md', 'THIRD_PARTY_NOTICES.md', 'RENDERING.md', 'AGENTS.md', 'CustomCourseLoader.csproj', 'Install-CourseStudio.ps1', 'build.ps1', 'InstallUpdateAfterExit.ps1', 'New-ReleasePackage.ps1')) {
        $sourcePath = Join-Path $projectRoot $item
        if (Test-Path -LiteralPath $sourcePath -PathType Leaf) {
            Copy-FileIntoPackage $sourcePath ('Source\CustomCourseLoader\' + $item)
            if ($item -in @('README.md', 'CHANGELOG.md', 'THIRD_PARTY_NOTICES.md')) { Copy-FileIntoPackage $sourcePath $item }
        }
    }
    Copy-FileIntoPackage (Join-Path $projectRoot 'AGENTS.md') 'AGENTS.md'
    Copy-FileIntoPackage (Join-Path $projectRoot 'Install-CourseStudio.ps1') 'Install-CourseStudio.ps1'
    foreach ($file in Get-ChildItem -LiteralPath (Join-Path $projectRoot 'CourseEditor') -File -Recurse) {
        $relative = Get-RelativeFilePath (Join-Path $projectRoot 'CourseEditor') $file.FullName
        if ($relative -ieq 'catalog.js' -or $relative -match '^assets[\\/]maps[\\/]') { continue }
        Copy-FileIntoPackage $file.FullName ('Source\CustomCourseLoader\CourseEditor\' + $relative)
    }
    [IO.File]::WriteAllText((Join-Path $stagingRoot 'Source\CustomCourseLoader\CourseEditor\catalog.js'), $catalogPlaceholder, [Text.UTF8Encoding]::new($false))
    foreach ($file in Get-ChildItem -LiteralPath $projectRoot -Filter '*.cs' -File -Recurse) {
        $relative = Get-RelativeFilePath $projectRoot $file.FullName
        if ($relative -match '(^|[\\/])(bin|obj|\.tools)[\\/]') { continue }
        Copy-FileIntoPackage $file.FullName ('Source\CustomCourseLoader\' + $relative)
    }
    foreach ($directory in @('docs', 'Tools')) {
        $sourceDirectory = Join-Path $projectRoot $directory
        foreach ($file in Get-ChildItem -LiteralPath $sourceDirectory -File -Recurse) {
            $relative = Get-RelativeFilePath $projectRoot $file.FullName
            if ($relative -match '(^|[\\/])(bin|obj)[\\/]') { continue }
            Copy-FileIntoPackage $file.FullName ('Source\CustomCourseLoader\' + $relative)
            Copy-FileIntoPackage $file.FullName $relative
        }
    }
    if (Test-Path -LiteralPath (Join-Path $projectRoot 'LICENSE') -PathType Leaf) {
        Copy-FileIntoPackage (Join-Path $projectRoot 'LICENSE') 'LICENSE'
        Copy-FileIntoPackage (Join-Path $projectRoot 'LICENSE') 'Source\CustomCourseLoader\LICENSE'
    }
    $thirdPartyLicenses = Join-Path $projectRoot 'ThirdPartyLicenses'
    foreach ($file in Get-ChildItem -LiteralPath $thirdPartyLicenses -File -Recurse) {
        $relative = Get-RelativeFilePath $thirdPartyLicenses $file.FullName
        Copy-FileIntoPackage $file.FullName ('ThirdPartyLicenses\' + $relative)
        Copy-FileIntoPackage $file.FullName ('Source\CustomCourseLoader\ThirdPartyLicenses\' + $relative)
    }
    if ($Draft) {
        $draftStatus = @(
            'LOCAL REVIEW ARCHIVE — DO NOT PUBLISH',
            "Plugin version: $version",
            'Complete docs/RELEASE_CHECKLIST.md and review THIRD_PARTY_NOTICES.md before public distribution.'
        ) -join [Environment]::NewLine
        [IO.File]::WriteAllText((Join-Path $stagingRoot 'DRAFT-RELEASE-STATUS.txt'), $draftStatus, [Text.UTF8Encoding]::new($false))
    }

    $manifest = [ordered]@{
        package = $packageName
        license = 'MIT-0'
        licenseScope = 'Original Course Studio code and documentation; see THIRD_PARTY_NOTICES.md and ThirdPartyLicenses/ for dependency terms.'
        pluginVersion = $version
        courseEditorVersion = $version
        draft = [bool]$Draft
        generatedUtc = [DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ')
        target = 'Windows PC · Disc Golf Masters · BepInEx 6 IL2CPP'
        bundledUserMapAssets = $false
        bundledOriginalGameAssets = $false
    }
    [IO.File]::WriteAllText((Join-Path $stagingRoot 'package-manifest.json'), ($manifest | ConvertTo-Json -Depth 5), [Text.UTF8Encoding]::new($false))
    Compress-Archive -Path (Join-Path $stagingRoot '*') -DestinationPath $archivePath -CompressionLevel Optimal
    $hash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash
    [IO.File]::WriteAllText(($archivePath + '.sha256'), "$hash  $([IO.Path]::GetFileName($archivePath))", [Text.UTF8Encoding]::new($false))
    try {
        & (Join-Path $projectRoot 'Tools\TestReleasePackage.ps1') -ArchivePath $archivePath -ExpectedVersion $version -Draft:$Draft
    }
    catch {
        Write-Host "Archive audit error: $($_.Exception.ToString())"
        if ($_.ScriptStackTrace) { Write-Host "Archive audit stack: $($_.ScriptStackTrace)" }
        if (Test-Path -LiteralPath $archivePath) { Remove-Item -LiteralPath $archivePath -Force }
        if (Test-Path -LiteralPath ($archivePath + '.sha256')) { Remove-Item -LiteralPath ($archivePath + '.sha256') -Force }
        throw "Release archive audit failed; the generated archive and checksum were removed. $($_.Exception.Message)"
    }
    Write-Host "Created package: $archivePath"
    Write-Host "SHA256: $hash"
}
finally {
    if (Test-Path -LiteralPath $stagingFull) {
        Remove-Item -LiteralPath $stagingFull -Recurse -Force
    }
}
