$ErrorActionPreference = 'Stop'

$portableNode = Join-Path $PSScriptRoot 'runtime\node.exe'
$nodeCommand = Get-Command node.exe -ErrorAction SilentlyContinue
$nodePath = if (Test-Path -LiteralPath $portableNode -PathType Leaf) { $portableNode } elseif ($nodeCommand) { $nodeCommand.Source } else { $null }
if (-not $nodePath) {
    throw "Node.js is required to serve the local Course Studio page. Install Node.js LTS, then run this launcher again."
}

$serverScript = Join-Path $PSScriptRoot 'server.js'
if (-not (Test-Path -LiteralPath $serverScript -PathType Leaf)) {
    throw "Course Studio server.js was not found beside this launcher: $serverScript"
}

# Reserve an ephemeral loopback port rather than assuming 4170 is unused. The
# server is still bound to 127.0.0.1; the chosen port is passed to its process.
$portReservation = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
try {
    $portReservation.Start()
    $port = ([System.Net.IPEndPoint]$portReservation.LocalEndpoint).Port
}
finally {
    $portReservation.Stop()
}

$previousPort = $env:DGM_COURSE_EDITOR_PORT
$serverProcess = $null
try {
    $env:DGM_COURSE_EDITOR_PORT = [string]$port
    $serverProcess = Start-Process -FilePath $nodePath `
        -ArgumentList ('"' + $serverScript + '"') `
        -WorkingDirectory $PSScriptRoot `
        -WindowStyle Hidden `
        -PassThru
}
finally {
    if ($null -eq $previousPort) { Remove-Item Env:DGM_COURSE_EDITOR_PORT -ErrorAction SilentlyContinue }
    else { $env:DGM_COURSE_EDITOR_PORT = $previousPort }
}

$url = "http://127.0.0.1:$port/"
$ready = $false
$deadline = [DateTime]::UtcNow.AddSeconds(15)
while ([DateTime]::UtcNow -lt $deadline) {
    if ($serverProcess.HasExited) {
        throw "Course Studio server exited before it became ready. Check that the installed CourseEditor folder is intact."
    }

    try {
        $response = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 1
        if ($response.StatusCode -eq 200) {
            $ready = $true
            break
        }
    }
    catch {
        Start-Sleep -Milliseconds 250
    }
}

if (-not $ready) {
    if (-not $serverProcess.HasExited) { Stop-Process -Id $serverProcess.Id -Force -ErrorAction SilentlyContinue }
    throw "Course Studio did not respond at $url within 15 seconds. The server was stopped."
}

try {
    Start-Process $url | Out-Null
}
catch {
    Write-Warning "Course Studio is ready, but the browser could not be opened automatically. Open $url manually."
}
Write-Host "Course Studio is ready at $url"
