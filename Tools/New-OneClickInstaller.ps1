[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$ArchivePath,
    [Parameter(Mandatory=$true)][string]$OutputPath
)
$ErrorActionPreference = 'Stop'
$archive = (Resolve-Path -LiteralPath $ArchivePath).Path
$output = [IO.Path]::GetFullPath($OutputPath)
if (Test-Path -LiteralPath $output) { throw "Output already exists: $output" }
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler -PathType Leaf)) { throw 'The Windows .NET Framework C# compiler was not found.' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead($archive)
try {
    foreach ($required in @('Setup-CourseStudio.ps1', 'Install-CourseStudio.ps1', 'package-manifest.json', 'BepInEx/plugins/CustomCourseLoader/CustomCourseLoader.dll')) {
        if (-not $zip.GetEntry($required)) { throw "Archive missing required setup file: $required" }
    }
} finally { $zip.Dispose() }
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $output) | Out-Null
$source = Join-Path $PSScriptRoot 'OneClickBootstrap.cs'
& $compiler /nologo /target:winexe /optimize+ ("/out:$output") ("/resource:$archive,CourseStudioPackage.zip") /reference:System.IO.Compression.FileSystem.dll /reference:System.Windows.Forms.dll $source
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $output)) { throw "Windows installer compilation failed: $LASTEXITCODE" }
$check = Start-Process -FilePath $output -ArgumentList '--check' -WindowStyle Hidden -Wait -PassThru
if ($check.ExitCode -ne 0) { throw 'The generated installer could not extract its embedded package.' }
Write-Host "Created one-click installer: $output"
Write-Host "SHA256: $((Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash)"
