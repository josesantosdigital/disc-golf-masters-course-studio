[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ReferenceGameRoot,

    [string]$DraftArchivePath
)

$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$referenceRootFull = (Resolve-Path -LiteralPath $ReferenceGameRoot).Path
$requiredReferencePaths = @(
    'BepInEx\core\BepInEx.Core.dll',
    'BepInEx\core\BepInEx.Unity.IL2CPP.dll',
    'BepInEx\interop\Assembly-CSharp.dll'
)
foreach ($relativePath in $requiredReferencePaths) {
    if (-not (Test-Path -LiteralPath (Join-Path $referenceRootFull $relativePath) -PathType Leaf)) {
        throw "Reference game folder is missing a required build assembly: $relativePath"
    }
}

$nodeCommand = Get-Command node.exe -ErrorAction Stop
$nodeVersion = & $nodeCommand.Source --version
if ($LASTEXITCODE -ne 0) { throw 'Could not query the Node.js version.' }
$localDotnet = Join-Path $projectRoot '.tools\dotnet\dotnet.exe'
$dotnetCommand = if (Test-Path -LiteralPath $localDotnet -PathType Leaf) {
    $localDotnet
} else {
    (Get-Command dotnet.exe -ErrorAction Stop).Source
}
$sdkList = @(& $dotnetCommand --list-sdks)
if ($LASTEXITCODE -ne 0) { throw '.NET SDK discovery failed.' }
if (-not ($sdkList | Where-Object { $_ -match '^6\.' })) {
    throw 'The release preflight requires the .NET 6 SDK. Install it or place it under .tools\dotnet.'
}

$previousCliHome = $env:DOTNET_CLI_HOME
$previousSkipFirstTime = $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE
$taskCliHome = Join-Path $projectRoot '.tools\cli-home'
New-Item -ItemType Directory -Force -Path $taskCliHome | Out-Null
$env:DOTNET_CLI_HOME = $taskCliHome
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'

function Invoke-PowerShellCheck([string]$Label, [string]$Path, [hashtable]$Parameters) {
    Write-Host "`n=== $Label ==="
    & $Path @Parameters
}

function Invoke-ExternalCheck([string]$Label, [string]$Executable, [string[]]$Arguments) {
    Write-Host "`n=== $Label ==="
    & $Executable @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Label failed with exit code $LASTEXITCODE." }
}

try {
    Push-Location $projectRoot
    try {
        Invoke-PowerShellCheck 'Build/install preservation fixtures' (Join-Path $projectRoot 'Tools\TestBuildInstall.ps1') @{ ReferenceGameRoot = $referenceRootFull }
        Invoke-PowerShellCheck 'BONELAB and generic converter fixtures' (Join-Path $projectRoot 'Tools\TestCourseConverters.ps1') @{}
        Invoke-PowerShellCheck 'Course package validation fixtures' (Join-Path $projectRoot 'Tools\TestCoursePackageValidation.ps1') @{}
        Invoke-PowerShellCheck 'Course mod package build/install fixtures' (Join-Path $projectRoot 'Tools\TestCourseModPackage.ps1') @{}
        Invoke-PowerShellCheck 'Bundled sample course' (Join-Path $projectRoot 'Tools\ValidateCoursePackage.ps1') @{
            CoursePath = (Join-Path $projectRoot 'Tools\Samples\SampleMeadowNine.json')
            PluginDirectory = (Join-Path $referenceRootFull 'BepInEx\plugins\CustomCourseLoader')
        }
        Invoke-PowerShellCheck 'Release archive asset-boundary policy' (Join-Path $projectRoot 'Tools\TestReleasePackagePolicy.ps1') @{}
        Invoke-PowerShellCheck 'Git ignore boundaries' (Join-Path $projectRoot 'Tools\TestGitIgnore.ps1') @{}
        Invoke-ExternalCheck 'Course Editor, map catalog, and settings checks' $nodeCommand.Source @('--test', '.\Tools\TestCourseEditorExport.cjs', '.\Tools\TestMapCatalog.cjs', '.\Tools\TestMapLightingSettings.cjs')
        Write-Host "`n=== CourseEditor JavaScript syntax ==="
        foreach ($editorScript in (Get-ChildItem -LiteralPath (Join-Path $projectRoot 'CourseEditor') -Filter '*.js' -File -Recurse)) {
            & $nodeCommand.Source --check $editorScript.FullName
            if ($LASTEXITCODE -ne 0) { throw "JavaScript syntax check failed: $($editorScript.FullName)" }
        }

        $checkProjects = @(
            'Tools\CourseLoadingChecks\CourseLoadingChecks.csproj',
            'Tools\ImportedCoursePlannerChecks\ImportedCoursePlannerChecks.csproj',
            'Tools\CoursePreviewChecks\CoursePreviewChecks.csproj',
            'Tools\PersonalBestChecks\PersonalBestChecks.csproj',
            'Tools\FoliageGeometryChecks\FoliageGeometryChecks.csproj'
        )
        foreach ($checkProject in $checkProjects) {
            Invoke-ExternalCheck "Pure C# checks: $checkProject" $dotnetCommand @('run', '--project', $checkProject, '--configuration', 'Release')
        }

        if (-not [string]::IsNullOrWhiteSpace($DraftArchivePath)) {
            $archiveFullPath = (Resolve-Path -LiteralPath $DraftArchivePath).Path
            $versionMatch = [regex]::Match((Get-Content -LiteralPath (Join-Path $projectRoot 'Plugin.cs') -Raw), 'PluginVersion\s*=\s*"([^"]+)"')
            if (-not $versionMatch.Success) { throw 'Could not read PluginVersion from Plugin.cs.' }
            Invoke-PowerShellCheck 'DRAFT archive audit' (Join-Path $projectRoot 'Tools\TestReleasePackage.ps1') @{
                ArchivePath = $archiveFullPath
                ExpectedVersion = $versionMatch.Groups[1].Value
                Draft = $true
            }
            Invoke-PowerShellCheck 'Exact DRAFT archive install fixtures' (Join-Path $projectRoot 'Tools\TestCourseStudioInstaller.ps1') @{ ArchivePath = $archiveFullPath }
        }
    }
    finally {
        Pop-Location
    }
}
finally {
    $env:DOTNET_CLI_HOME = $previousCliHome
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = $previousSkipFirstTime
}

Write-Host "`nRelease preflight passed. Node.js: $nodeVersion. This checks offline behavior and disposable install fixtures; it does not start DGM or verify a clean real-game installation."
