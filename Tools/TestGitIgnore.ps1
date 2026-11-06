$ErrorActionPreference = 'Stop'

$gitCommand = Get-Command git.exe -ErrorAction Stop
$fixtureContainer = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '.gitignore-fixtures'))
$fixtureContainerPrefix = $fixtureContainer.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
$fixtureTaskRoot = [IO.Path]::GetFullPath((Join-Path $fixtureContainer ('run-' + [Guid]::NewGuid().ToString('N'))))
$fixtureRoot = Join-Path $fixtureTaskRoot 'worktree'
$fixtureGitDir = Join-Path $fixtureTaskRoot 'metadata'
if (-not $fixtureTaskRoot.StartsWith($fixtureContainerPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Git ignore fixture path escaped its project-local disposable container.'
}
$previousGitConfigGlobal = $env:GIT_CONFIG_GLOBAL
$previousGitConfigNoSystem = $env:GIT_CONFIG_NOSYSTEM

function New-FixtureFile([string]$RelativePath) {
    $path = Join-Path $fixtureRoot ($RelativePath.Replace('/', [IO.Path]::DirectorySeparatorChar))
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $path) | Out-Null
    [IO.File]::WriteAllText($path, 'synthetic gitignore fixture')
}

function Invoke-GitCheckIgnore([string[]]$Arguments, [int]$ExpectedExitCode, [string]$Label) {
    & $gitCommand.Source "--git-dir=$fixtureGitDir" "--work-tree=$fixtureRoot" @Arguments
    $actualExitCode = $LASTEXITCODE
    if ($actualExitCode -ne $ExpectedExitCode) {
        throw "Git ignore fixture expected exit code $ExpectedExitCode for '$Label', got $actualExitCode."
    }
}

try {
    New-Item -ItemType Directory -Force -Path $fixtureRoot | Out-Null
    $emptyGlobalConfig = Join-Path $fixtureTaskRoot 'empty-global-config'
    New-Item -ItemType Directory -Force -Path $fixtureTaskRoot | Out-Null
    [IO.File]::WriteAllText($emptyGlobalConfig, '')
    $env:GIT_CONFIG_GLOBAL = $emptyGlobalConfig
    $env:GIT_CONFIG_NOSYSTEM = '1'
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot '..\.gitignore') -Destination (Join-Path $fixtureRoot '.gitignore')
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot '..\.gitattributes') -Destination (Join-Path $fixtureRoot '.gitattributes')
    & $gitCommand.Source init --bare --quiet $fixtureGitDir
    if ($LASTEXITCODE -ne 0) { throw 'Could not initialize the disposable Git ignore fixture.' }

    $ignoredPaths = @(
        'bin/Release/plugin.dll',
        'obj/project.assets.json',
        'Tools/CourseLoadingChecks/bin/Release/check.dll',
        'Tools/CourseLoadingChecks/obj/project.assets.json',
        '.tools/dotnet/dotnet.exe',
        'dist/review.zip',
        'backups/course.json',
        'Tools/.gitignore-fixtures/run-sample/worktree/map.bundle',
        'Maps/TreeLand/scene.bundle',
        'Courses/PlayerCourse.json',
        'CourseEditor/catalog.js',
        'CourseEditor/assets/maps/TreeLand/scene.bundle',
        'deferred-install.log',
        'notes.user'
    )
    $includedPaths = @(
        'README.md',
        'docs/PORTING_GUIDE.md',
        'Runtime/Patches/CourseScenePatch.cs',
        'Tools/TestGitIgnore.ps1',
        'Courses/SampleMeadowNine.json'
    )

    foreach ($path in @($ignoredPaths + $includedPaths)) { New-FixtureFile $path }

    foreach ($path in $ignoredPaths) {
        Invoke-GitCheckIgnore @('check-ignore', '--quiet', '--', $path) 0 $path
    }
    foreach ($path in $includedPaths) {
        Invoke-GitCheckIgnore @('check-ignore', '--quiet', '--no-index', '--', $path) 1 $path
    }

    $sourceAttributes = @(& $gitCommand.Source "--git-dir=$fixtureGitDir" "--work-tree=$fixtureRoot" check-attr text eol -- 'Runtime/Patches/CourseScenePatch.cs')
    if ($LASTEXITCODE -ne 0 -or
        $sourceAttributes -notcontains 'Runtime/Patches/CourseScenePatch.cs: text: auto' -or
        $sourceAttributes -notcontains 'Runtime/Patches/CourseScenePatch.cs: eol: lf') {
        throw 'Git attributes must normalize C# source as text with LF line endings.'
    }
    $bundleAttributes = @(& $gitCommand.Source "--git-dir=$fixtureGitDir" "--work-tree=$fixtureRoot" check-attr text eol binary -- 'Maps/TreeLand/scene.bundle')
    if ($LASTEXITCODE -ne 0 -or
        $bundleAttributes -notcontains 'Maps/TreeLand/scene.bundle: text: unset' -or
        $bundleAttributes -notcontains 'Maps/TreeLand/scene.bundle: binary: set') {
        throw 'Unity scene bundles must be treated as binary assets.'
    }

    Write-Host "Git source-control checks passed in a disposable repository: $($ignoredPaths.Count) local/build paths ignored, $($includedPaths.Count) source paths includable, LF source attributes and binary bundle attributes confirmed."
}
finally {
    $env:GIT_CONFIG_GLOBAL = $previousGitConfigGlobal
    $env:GIT_CONFIG_NOSYSTEM = $previousGitConfigNoSystem
    $resolvedFixtureTaskRoot = [IO.Path]::GetFullPath($fixtureTaskRoot)
    if (-not $resolvedFixtureTaskRoot.StartsWith($fixtureContainerPrefix, [StringComparison]::OrdinalIgnoreCase) -or
        [string]::Equals($resolvedFixtureTaskRoot, $fixtureContainer.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar), [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove a path outside the disposable fixture container: $resolvedFixtureTaskRoot"
    }
    if (Test-Path -LiteralPath $resolvedFixtureTaskRoot) {
        Remove-Item -LiteralPath $resolvedFixtureTaskRoot -Recurse -Force
    }
}
