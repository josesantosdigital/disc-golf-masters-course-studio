$ErrorActionPreference = 'Stop'

$builtDll = Join-Path $PSScriptRoot 'bin\Release\net6.0\CustomCourseLoader.dll'
$installedDll = Join-Path $PSScriptRoot '..\BepInEx\plugins\CustomCourseLoader\CustomCourseLoader.dll'
$logPath = Join-Path $PSScriptRoot 'deferred-install.log'
$versionMatch = Select-String -Path (Join-Path $PSScriptRoot 'Plugin.cs') -Pattern 'PluginVersion\s*=\s*"([^"]+)"' | Select-Object -First 1
$pluginVersion = if ($versionMatch) { $versionMatch.Matches[0].Groups[1].Value } else { 'unknown' }
$expectedHash = (Get-FileHash -LiteralPath $builtDll -Algorithm SHA256).Hash
$lastError = ''

for ($attempt = 0; $attempt -lt 720; $attempt++) {
    try {
        Copy-Item -LiteralPath $builtDll -Destination $installedDll -Force
        $installedHash = (Get-FileHash -LiteralPath $installedDll -Algorithm SHA256).Hash
        if ($installedHash -eq $expectedHash) {
            Add-Content -LiteralPath $logPath -Value "Installed CustomCourseLoader $pluginVersion at $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss'). SHA256 $installedHash"
            exit 0
        }
        $lastError = "Hash mismatch after copy: $installedHash"
    }
    catch {
        $lastError = $_.Exception.Message
    }
    Start-Sleep -Seconds 5
}

Add-Content -LiteralPath $logPath -Value "Could not install CustomCourseLoader ${pluginVersion}: $lastError"
exit 1
