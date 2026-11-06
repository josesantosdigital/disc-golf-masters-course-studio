param(
    [string]$GameRoot,
    [string]$Configuration = 'Release'
)

if ([string]::IsNullOrWhiteSpace($GameRoot)) { $GameRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path }
$project = Join-Path $PSScriptRoot 'CustomCourseLoader.csproj'
$pluginDir = Join-Path $GameRoot 'BepInEx\plugins\CustomCourseLoader'
$coursesDir = Join-Path $pluginDir 'Courses'
$editorDir = Join-Path $pluginDir 'CourseEditor'
$gameRootPath = (Resolve-Path -LiteralPath $GameRoot).Path.TrimEnd('\')
$localDotnet = Join-Path $PSScriptRoot '.tools\dotnet\dotnet.exe'
$dotnetCommand = if (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { 'dotnet' }
$taskDotnetCliHome = Join-Path $PSScriptRoot '.tools\cli-home'
New-Item -ItemType Directory -Force -Path $taskDotnetCliHome | Out-Null
$previousDotnetCliHome = $env:DOTNET_CLI_HOME
$previousSkipFirstTime = $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE
$previousTelemetryOptOut = $env:DOTNET_CLI_TELEMETRY_OPTOUT
try {
    $env:DOTNET_CLI_HOME = $taskDotnetCliHome
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

    & $dotnetCommand build $project --configuration $Configuration "-p:GameRoot=$gameRootPath"
    if ($LASTEXITCODE -ne 0) {
        throw "CustomCourseLoader build failed with exit code $LASTEXITCODE."
    }
}
finally {
    $env:DOTNET_CLI_HOME = $previousDotnetCliHome
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = $previousSkipFirstTime
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = $previousTelemetryOptOut
}

New-Item -ItemType Directory -Force -Path $coursesDir | Out-Null
$builtDll = Join-Path $PSScriptRoot "bin\$Configuration\net6.0\CustomCourseLoader.dll"
$installedDll = Join-Path $pluginDir 'CustomCourseLoader.dll'
$builtHash = (Get-FileHash -LiteralPath $builtDll -Algorithm SHA256).Hash
$deferredInstall = $false
try {
    Copy-Item -LiteralPath $builtDll -Destination $installedDll -Force -ErrorAction Stop
}
catch {
    if ($_.Exception.Message -notmatch 'used by another process|being used') {
        throw
    }
    $deferredInstall = $true
    $installer = Join-Path $PSScriptRoot 'InstallUpdateAfterExit.ps1'
    $arguments = '-NoProfile -ExecutionPolicy Bypass -File "' + $installer + '"'
    Start-Process -FilePath 'powershell.exe' -ArgumentList $arguments -WindowStyle Hidden | Out-Null
    Write-Warning "The game currently has the plugin DLL open. The update is staged and will install when Windows releases it."
}
if (-not $deferredInstall) {
    $installedHash = (Get-FileHash -LiteralPath $installedDll -Algorithm SHA256).Hash
    if ($builtHash -ne $installedHash) {
        throw "Installed plugin hash does not match the build output ($builtHash != $installedHash)."
    }
}
Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'Courses') -File | Where-Object { $_.Extension -in '.json', '.png' } | ForEach-Object {
    $courseDestination = Join-Path $coursesDir $_.Name
    if (Test-Path -LiteralPath $courseDestination) {
        Write-Host "Keeping existing course file: $courseDestination"
    }
    else {
        Copy-Item -LiteralPath $_.FullName -Destination $courseDestination
    }
}
New-Item -ItemType Directory -Force -Path $editorDir | Out-Null
$editorSource = Join-Path $PSScriptRoot 'CourseEditor'
$editorSourcePrefix = [IO.Path]::GetFullPath($editorSource).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
Get-ChildItem -LiteralPath $editorSource -File -Recurse | ForEach-Object {
    $relativePath = $_.FullName.Substring($editorSourcePrefix.Length)
    $editorDestination = Join-Path $editorDir $relativePath
    if ((Test-Path -LiteralPath $editorDestination) -and
        ($relativePath -ieq 'catalog.js' -or $relativePath -match '^assets[\\/]maps[\\/]')) {
        Write-Host "Keeping generated editor asset: $editorDestination"
    }
    else {
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $editorDestination) | Out-Null
        Copy-Item -LiteralPath $_.FullName -Destination $editorDestination -Force
    }
}
Write-Host "Installed CustomCourseLoader and Course Editor to $pluginDir"
if ($deferredInstall) {
    Write-Host "The game can keep running; the updated loader DLL will be installed after it releases the file."
}
