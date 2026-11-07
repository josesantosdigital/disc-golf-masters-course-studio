[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$node = (Get-Command node.exe -ErrorAction Stop).Source
$localDotnet = Join-Path $projectRoot '.tools\dotnet\dotnet.exe'
$dotnet = if (Test-Path -LiteralPath $localDotnet -PathType Leaf) {
    $localDotnet
}
else {
    (Get-Command dotnet.exe -ErrorAction Stop).Source
}
$sdks = @(& $dotnet --list-sdks)
if ($LASTEXITCODE -ne 0 -or -not ($sdks | Where-Object { $_ -match '^6\.' })) {
    throw 'Offline checks require Node.js and the .NET 6 SDK.'
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
$env:DOTNET_CLI_HOME = Join-Path $taskTempFull 'dotnet-home'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:NUGET_PACKAGES = Join-Path $taskTempFull 'nuget-packages'

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
            'Tools\FoliageGeometryChecks\FoliageGeometryChecks.csproj'
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
    if (Test-Path -LiteralPath $taskTempFull) {
        $resolvedTemp = [IO.Path]::GetFullPath($taskTempFull)
        if (-not $resolvedTemp.StartsWith($tempPrefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to clean a path outside the system temporary directory: $resolvedTemp"
        }
        Remove-Item -LiteralPath $resolvedTemp -Recurse -Force
    }
}

Write-Host "`nOffline contributor checks passed. No game assemblies were required and DGM was not launched. Use TestReleasePreflight.ps1 with a local game reference for full release validation."
