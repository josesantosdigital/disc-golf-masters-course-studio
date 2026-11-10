[CmdletBinding()]
param(
    [string]$GameRoot,
    [string]$PackageRoot = $PSScriptRoot,
    [switch]$AllowDraft,
    [switch]$NoShortcut,
    [string]$BepInExArchive,
    [string]$NodeBinary
)

$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$bepUrl = 'https://builds.bepinex.dev/projects/bepinex_be/791/BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.791%2Bb782da4.zip'
$bepHash = 'BB1C510909B69522733791F3A0817A487379A52DA6468A492F1C9C19B3EC6F9A'
$nodeUrl = 'https://nodejs.org/dist/v24.22.1/win-x64/node.exe'
$nodeHash = 'D702DAA815EABBFF763C7D3808E235CF031CF2CA70211B56847BC72F51E240E5'

function Find-SteamGame {
    $steam = (Get-ItemProperty -Path 'HKCU:\Software\Valve\Steam' -ErrorAction SilentlyContinue).SteamPath
    if ([string]::IsNullOrWhiteSpace($steam) -and ${env:ProgramFiles(x86)}) {
        $steam = Join-Path ${env:ProgramFiles(x86)} 'Steam'
    }
    if ([string]::IsNullOrWhiteSpace($steam)) { return $null }
    $libraries = @($steam)
    $vdf = Join-Path $steam 'steamapps\libraryfolders.vdf'
    if (Test-Path -LiteralPath $vdf -PathType Leaf) {
        $contents = Get-Content -LiteralPath $vdf -Raw
        foreach ($match in [regex]::Matches($contents, '(?m)"path"\s*"((?:\\\\|[^"\\])*)"')) {
            $libraries += $match.Groups[1].Value.Replace('\\', '\')
        }
    }
    foreach ($library in ($libraries | Select-Object -Unique)) {
        $manifest = Join-Path $library 'steamapps\appmanifest_2819400.acf'
        if (-not (Test-Path -LiteralPath $manifest -PathType Leaf)) { continue }
        $content = Get-Content -LiteralPath $manifest -Raw
        $match = [regex]::Match($content, '"installdir"\s*"([^"\\/]+)"')
        if (-not $match.Success) { continue }
        $candidate = Join-Path (Join-Path $library 'steamapps\common') $match.Groups[1].Value
        if (Test-Path -LiteralPath (Join-Path $candidate 'Disc Golf Masters.exe') -PathType Leaf) { return $candidate }
    }
    return $null
}

function Get-VerifiedFile([string]$Provided, [string]$Url, [string]$Target, [string]$Hash, [string]$Label) {
    if ($Provided) { Copy-Item -LiteralPath (Resolve-Path -LiteralPath $Provided).Path -Destination $Target }
    else { Write-Host "Downloading $Label..."; Invoke-WebRequest -Uri $Url -OutFile $Target -UseBasicParsing -TimeoutSec 180 }
    if ((Get-FileHash -LiteralPath $Target -Algorithm SHA256).Hash -ne $Hash) { throw "$Label checksum verification failed. Installation stopped." }
}

if (-not $GameRoot) { $GameRoot = Find-SteamGame }
if (-not $GameRoot) {
    Add-Type -AssemblyName System.Windows.Forms
    $picker = [Windows.Forms.FolderBrowserDialog]::new()
    $picker.Description = 'Select the Disc Golf Masters game folder'
    if ($picker.ShowDialog() -ne [Windows.Forms.DialogResult]::OK) { throw 'No game folder was selected.' }
    $GameRoot = $picker.SelectedPath
}
$GameRoot = (Resolve-Path -LiteralPath $GameRoot).Path
$PackageRoot = (Resolve-Path -LiteralPath $PackageRoot).Path
if (-not (Test-Path -LiteralPath (Join-Path $GameRoot 'Disc Golf Masters.exe') -PathType Leaf)) { throw 'The selected folder does not contain Disc Golf Masters.exe.' }
if (@(Get-Process -Name 'Disc Golf Masters' -ErrorAction SilentlyContinue).Count) { throw 'Close Disc Golf Masters and run setup again.' }
$manifest = Get-Content -LiteralPath (Join-Path $PackageRoot 'package-manifest.json') -Raw | ConvertFrom-Json
if ($manifest.draft -and -not $AllowDraft) {
    Add-Type -AssemblyName System.Windows.Forms
    $answer = [Windows.Forms.MessageBox]::Show('This is a local review build. Continue with the test installation?', 'Course Studio test build', 'OKCancel', 'Warning')
    if ($answer -ne [Windows.Forms.DialogResult]::OK) { throw 'Test installation cancelled.' }
    $AllowDraft = $true
}

$temp = Join-Path ([IO.Path]::GetTempPath()) ('DgmCourseSetup-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp | Out-Null
try {
    $core = Join-Path $GameRoot 'BepInEx\core\BepInEx.Core.dll'
    $il2cpp = Join-Path $GameRoot 'BepInEx\core\BepInEx.Unity.IL2CPP.dll'
    $hasCore = Test-Path -LiteralPath $core -PathType Leaf
    $hasIl2cpp = Test-Path -LiteralPath $il2cpp -PathType Leaf
    if ($hasCore -xor $hasIl2cpp) { throw 'An incomplete BepInEx installation was found. Please repair it before running setup.' }
    if (-not $hasCore) {
        $archive = Join-Path $temp 'bepinex.zip'
        Get-VerifiedFile $BepInExArchive $bepUrl $archive $bepHash 'BepInEx'
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $zip = [IO.Compression.ZipFile]::OpenRead($archive)
        try {
            $gamePrefix = [IO.Path]::GetFullPath($GameRoot).TrimEnd('\') + '\'
            $files = @($zip.Entries | Where-Object { $_.Name -and $_.FullName -ne 'changelog.txt' })
            $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
            foreach ($entry in $files) {
                $relative = $entry.FullName.Replace('/', '\')
                if ([IO.Path]::IsPathRooted($relative) -or $relative.Split('\') -contains '..' -or -not $seen.Add($relative)) { throw 'Unsafe or duplicate path in BepInEx archive.' }
                $target = [IO.Path]::GetFullPath((Join-Path $GameRoot $relative))
                if (-not $target.StartsWith($gamePrefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'BepInEx archive path escapes the game folder.' }
                if (Test-Path -LiteralPath $target) { throw "An existing file would be overwritten: $target. Install or repair BepInEx separately." }
                $parent = Split-Path -Parent $target
                while ($parent -and $parent.StartsWith($gamePrefix, [StringComparison]::OrdinalIgnoreCase)) {
                    if ((Test-Path -LiteralPath $parent) -and ((Get-Item -LiteralPath $parent -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'A linked BepInEx installation path was found.' }
                    $parent = Split-Path -Parent $parent
                }
            }
            Write-Host 'Installing BepInEx...'
            foreach ($entry in $files) {
                $target = Join-Path $GameRoot ($entry.FullName.Replace('/', '\'))
                New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
                [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $target, $false)
            }
        } finally { $zip.Dispose() }
    } else { Write-Host 'Existing BepInEx installation found; leaving it in place.' }

    $existingNode = Get-Command node.exe -ErrorAction SilentlyContinue
    $portable = Join-Path $GameRoot 'BepInEx\plugins\CustomCourseLoader\CourseEditor\runtime\node.exe'
    $newNode = $null
    if ($NodeBinary -or (-not $existingNode -and -not (Test-Path -LiteralPath $portable -PathType Leaf))) {
        $newNode = Join-Path $temp 'node.exe'
        Get-VerifiedFile $NodeBinary $nodeUrl $newNode $nodeHash 'Node.js'
    }
    Write-Host 'Installing Course Studio and the custom map loader...'
    & (Join-Path $PackageRoot 'Install-CourseStudio.ps1') -GameRoot $GameRoot -AllowDraft:$AllowDraft
    if ($newNode) {
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $portable) | Out-Null
        Copy-Item -LiteralPath $newNode -Destination $portable
    }
    if (-not $NoShortcut) {
        $desktop = [Environment]::GetFolderPath('DesktopDirectory')
        $shortcut = (New-Object -ComObject WScript.Shell).CreateShortcut((Join-Path $desktop 'Disc Golf Course Studio.lnk'))
        $shortcut.TargetPath = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
        $launcher = Join-Path $GameRoot 'BepInEx\plugins\CustomCourseLoader\CourseEditor\Launch-Course-Editor.ps1'
        $shortcut.Arguments = '-NoProfile -ExecutionPolicy Bypass -File "' + $launcher + '"'
        $shortcut.WorkingDirectory = Split-Path -Parent $launcher
        $shortcut.Description = 'Open Disc Golf Course Studio'
        $shortcut.Save()
    }
    Write-Host 'Setup complete. Start the game once, open the offline course selector, then use the Course Studio desktop shortcut.'
} finally {
    $tempPrefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    $tempFull = [IO.Path]::GetFullPath($temp)
    if (-not $tempFull.StartsWith($tempPrefix, [StringComparison]::OrdinalIgnoreCase) -or -not [IO.Path]::GetFileName($tempFull).StartsWith('DgmCourseSetup-')) { throw 'Refusing to clean an unexpected temporary path.' }
    if (Test-Path -LiteralPath $tempFull) { Remove-Item -LiteralPath $tempFull -Recurse -Force }
}
