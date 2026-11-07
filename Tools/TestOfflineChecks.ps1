[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$node = (Get-Command node.exe -ErrorAction Stop).Source
$localDotnet = Join-Path $projectRoot '.tools\dotnet\dotnet.exe'
$dotnetCandidates = @()
$pathDotnet = Get-Command dotnet.exe -ErrorAction SilentlyContinue
if ($null -ne $pathDotnet) { $dotnetCandidates += $pathDotnet.Source }
if (Test-Path -LiteralPath $localDotnet -PathType Leaf) { $dotnetCandidates += $localDotnet }
$dotnet = $null
$dotnetSdkMajor = 0
$legacyDotnet = $null
foreach ($candidate in ($dotnetCandidates | Select-Object -Unique)) {
    $candidateSdks = @(& $candidate --list-sdks)
    if ($LASTEXITCODE -ne 0) { continue }
    $stableMajorVersions = @($candidateSdks | ForEach-Object {
        if ($_ -match '^(\d+)\.\d+\.\d+\s') { [int]$Matches[1] }
    })
    $currentSdk = $stableMajorVersions | Where-Object { $_ -ge 10 } | Sort-Object -Descending | Select-Object -First 1
    if ($null -ne $currentSdk) {
        $dotnet = $candidate
        $dotnetSdkMajor = [int]$currentSdk
        break
    }
    if ($null -eq $legacyDotnet -and $stableMajorVersions -contains 6) { $legacyDotnet = $candidate }
}
if ($null -eq $dotnet -and $null -ne $legacyDotnet) {
    $dotnet = $legacyDotnet
    $dotnetSdkMajor = 6
}
if ($null -eq $dotnet) {
    throw 'Offline checks require Node.js and a stable .NET 10-or-newer SDK (recommended) or .NET 6 SDK (legacy fallback).'
}

$taskTempRoot = Join-Path ([IO.Path]::GetTempPath()) ('dgm-course-studio-offline-' + [Guid]::NewGuid().ToString('N'))
$tempPrefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
$taskTempFull = [IO.Path]::GetFullPath($taskTempRoot)
if (-not $taskTempFull.StartsWith($tempPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Offline check scratch folder escaped the system temporary directory.'
}

$previousCliHome = $env:DOTNET_CLI_HOME
$previousSkipFirstTime = $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE
$previousTelemetryOptOut = $env:DOTNET_CLI_TELEMETRY_OPTOUT
$previousNoLogo = $env:DOTNET_NOLOGO
$previousNugetPackages = $env:NUGET_PACKAGES
$previousCheckEolTargetFramework = $env:CheckEolTargetFramework
$previousRollForward = $env:DOTNET_ROLL_FORWARD
$env:DOTNET_CLI_HOME = Join-Path $taskTempFull 'dotnet-home'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:NUGET_PACKAGES = Join-Path $taskTempFull 'nuget-packages'
if ($dotnetSdkMajor -ge 10) {
    # Keep these checks on the plugin's net6.0 API surface. On clean CI, build
    # against its down-level reference pack and use the current runtime only
    # when a .NET 6 runtime is unavailable.
    $env:CheckEolTargetFramework = 'false'
    $env:DOTNET_ROLL_FORWARD = 'Major'
}

function Invoke-ProjectScript([string]$RelativePath, [hashtable]$Parameters = @{}) {
    $path = Join-Path $projectRoot $RelativePath
    Write-Host "`n=== $RelativePath ==="
    $global:LASTEXITCODE = 0
    & $path @Parameters
    if ($LASTEXITCODE -ne 0) { throw "$RelativePath failed with exit code $LASTEXITCODE." }
}

function Invoke-Native([string]$Label, [string]$Executable, [string[]]$Arguments) {
    Write-Host "`n=== $Label ==="
    & $Executable @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Label failed with exit code $LASTEXITCODE." }
}

try {
    New-Item -ItemType Directory -Force -Path $taskTempFull | Out-Null
    Push-Location $projectRoot
    try {
        Invoke-ProjectScript 'Tools\TestCourseConverters.ps1'
        Invoke-ProjectScript 'Tools\TestCoursePackageValidation.ps1'
        Invoke-ProjectScript 'Tools\TestCourseModPackage.ps1'
        Invoke-ProjectScript 'Tools\TestReleasePackagePolicy.ps1'
        Invoke-ProjectScript 'Tools\TestGitIgnore.ps1'

        $samplePluginRoot = Join-Path $taskTempFull 'sample-plugin'
        New-Item -ItemType Directory -Force -Path $samplePluginRoot | Out-Null
        Invoke-ProjectScript 'Tools\ValidateCoursePackage.ps1' @{
            CoursePath = (Join-Path $projectRoot 'Tools\Samples\SampleMeadowNine.json')
            PluginDirectory = $samplePluginRoot
        }

        Invoke-Native 'Course Editor and map-catalog tests' $node @('--test', '.\Tools\TestCourseEditorExport.cjs', '.\Tools\TestMapCatalog.cjs', '.\Tools\TestMapLightingSettings.cjs')
        Write-Host "`n=== CourseEditor JavaScript syntax ==="
        foreach ($script in (Get-ChildItem -LiteralPath (Join-Path $projectRoot 'CourseEditor') -Filter '*.js' -File -Recurse)) {
            Invoke-Native $script.FullName $node @('--check', $script.FullName)
        }

        Write-Host "`n=== PowerShell source syntax ==="
        $powerShellFiles = @(
            (Get-ChildItem -LiteralPath (Join-Path $projectRoot 'Tools') -File -Recurse | Where-Object { $_.Extension -in '.ps1', '.psm1' })
            (Get-ChildItem -LiteralPath (Join-Path $projectRoot 'CourseEditor') -File -Recurse | Where-Object { $_.Extension -in '.ps1', '.psm1' })
            (Get-ChildItem -LiteralPath $projectRoot -File | Where-Object { $_.Extension -in '.ps1', '.psm1' })
        )
        foreach ($file in $powerShellFiles) {
            $tokens = $null
            $parseErrors = $null
            [System.Management.Automation.Language.Parser]::ParseFile($file.FullName, [ref]$tokens, [ref]$parseErrors) | Out-Null
            if ($parseErrors.Count -gt 0) {
                throw "PowerShell syntax failed in $($file.FullName): $($parseErrors[0].Message)"
            }
        }
        Write-Host "Parsed $($powerShellFiles.Count) PowerShell source files."

        $checkProjects = @(
            'Tools\CourseLoadingChecks\CourseLoadingChecks.csproj',
            'Tools\ImportedCoursePlannerChecks\ImportedCoursePlannerChecks.csproj',
            'Tools\CoursePreviewChecks\CoursePreviewChecks.csproj',
            'Tools\PersonalBestChecks\PersonalBestChecks.csproj',
            'Tools\FoliageGeometryChecks\FoliageGeometryChecks.csproj',
            'Tools\ImportedTerrainIsolationChecks\ImportedTerrainIsolationChecks.csproj',
            'Tools\ImportedLitMaterialChecks\ImportedLitMaterialChecks.csproj'
        )
        foreach ($checkProject in $checkProjects) {
            Invoke-Native "Pure C# checks: $checkProject" $dotnet @('run', '--project', $checkProject, '--configuration', 'Release')
        }
    }
    finally {
        Pop-Location
    }
}
finally {
    $env:DOTNET_CLI_HOME = $previousCliHome
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = $previousSkipFirstTime
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = $previousTelemetryOptOut
    $env:DOTNET_NOLOGO = $previousNoLogo
    $env:NUGET_PACKAGES = $previousNugetPackages
    $env:CheckEolTargetFramework = $previousCheckEolTargetFramework
    $env:DOTNET_ROLL_FORWARD = $previousRollForward
    if (Test-Path -LiteralPath $taskTempFull) {
        $resolvedTemp = [IO.Path]::GetFullPath($taskTempFull)
        if (-not $resolvedTemp.StartsWith($tempPrefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to clean a path outside the system temporary directory: $resolvedTemp"
        }
        Remove-Item -LiteralPath $resolvedTemp -Recurse -Force
    }
}

Write-Host "`nOffline contributor checks passed. No game assemblies were required and DGM was not launched. Use TestReleasePreflight.ps1 with a local game reference for full release validation."
