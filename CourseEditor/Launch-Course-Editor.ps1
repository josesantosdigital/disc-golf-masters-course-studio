$nodeCommand = Get-Command node.exe -ErrorAction SilentlyContinue
if (-not $nodeCommand) {
    throw "Node.js is required to serve the local Course Studio page."
}

$serverScript = Join-Path $PSScriptRoot "server.js"
$nodeArguments = '"' + $serverScript + '"'
Start-Process -FilePath $nodeCommand.Source -ArgumentList $nodeArguments -WorkingDirectory $PSScriptRoot -WindowStyle Hidden
Start-Sleep -Milliseconds 700
Start-Process "http://127.0.0.1:4170/"
