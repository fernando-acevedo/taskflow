<#
.SYNOPSIS
    Starts the TaskFlow API and frontend for local development and opens the app in the browser.

.DESCRIPTION
    Opens one window per server so their logs stay readable, waits until both answer, then opens
    http://localhost:4200. Press Enter in this window to stop both servers.
    A server that is already listening on its port is reused instead of started again.

.EXAMPLE
    .\dev.ps1
    .\dev.ps1 -ApiDocs      # also opens the Scalar API reference
    .\dev.ps1 -NoBrowser
#>
[CmdletBinding()]
param(
    [switch]$ApiDocs,
    [switch]$NoBrowser,
    [int]$TimeoutSeconds = 180
)

$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$backendDir = Join-Path $root 'backend'
$frontendDir = Join-Path $root 'frontend'
$apiPort = 5080
$appPort = 4200
$apiHealthUrl = "http://localhost:$apiPort/health"
$apiDocsUrl = "http://localhost:$apiPort/scalar/v1"
$appUrl = "http://localhost:$appPort"

function Test-PortInUse([int]$Port) {
    $client = New-Object System.Net.Sockets.TcpClient
    try {
        $client.Connect('localhost', $Port)
        return $true
    }
    catch {
        return $false
    }
    finally {
        $client.Dispose()
    }
}

function Start-ServerWindow([string]$Title, [string]$WorkingDirectory, [string]$Command) {
    $windowScript = "`$Host.UI.RawUI.WindowTitle = '$Title'; Set-Location -LiteralPath '$WorkingDirectory'; $Command"
    Start-Process -FilePath 'powershell.exe' -ArgumentList '-NoExit', '-NoProfile', '-Command', $windowScript -PassThru
}

# Returns the HTTP status code once the URL answers, or 0 on timeout.
function Wait-ForUrl([string]$Url, [string]$Name) {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    Write-Host "Waiting for $Name ($Url)" -NoNewline
    while ((Get-Date) -lt $deadline) {
        try {
            $response = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 5
            Write-Host ' ready' -ForegroundColor Green
            return [int]$response.StatusCode
        }
        catch {
            # A non-2xx answer still means the server is up (for example /health returns 503 when the database is down).
            if ($null -ne $_.Exception.Response) {
                $status = [int]$_.Exception.Response.StatusCode
                Write-Host " answered $status" -ForegroundColor Yellow
                return $status
            }
        }
        Write-Host '.' -NoNewline
        Start-Sleep -Seconds 2
    }
    Write-Host ' timed out' -ForegroundColor Red
    return 0
}

foreach ($tool in 'dotnet', 'npm') {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) {
        throw "'$tool' was not found on PATH. Install it and open a new terminal."
    }
}

if (-not (Test-Path (Join-Path $frontendDir 'node_modules'))) {
    Write-Host 'Installing frontend packages (first run)...' -ForegroundColor Cyan
    Push-Location $frontendDir
    try {
        npm ci
        if ($LASTEXITCODE -ne 0) { throw 'npm ci failed.' }
    }
    finally {
        Pop-Location
    }
}

$started = @()

if (Test-PortInUse $apiPort) {
    Write-Host "Port $apiPort is already in use; reusing the running API." -ForegroundColor Yellow
}
else {
    Write-Host 'Starting API...' -ForegroundColor Cyan
    $started += Start-ServerWindow 'TaskFlow API' $backendDir 'dotnet run --project src/TaskFlow.Api'
}

if (Test-PortInUse $appPort) {
    Write-Host "Port $appPort is already in use; reusing the running frontend." -ForegroundColor Yellow
}
else {
    Write-Host 'Starting frontend...' -ForegroundColor Cyan
    $started += Start-ServerWindow 'TaskFlow frontend' $frontendDir "npm start -- --port $appPort"
}

$apiStatus = Wait-ForUrl $apiHealthUrl 'API'
$appStatus = Wait-ForUrl $appUrl 'frontend'

if ($apiStatus -eq 0 -or $appStatus -eq 0) {
    Write-Host 'A server did not start in time. Check its window for errors.' -ForegroundColor Red
}
elseif ($apiStatus -ne 200) {
    Write-Host "The API reports a problem (HTTP $apiStatus). Is SQL Server LocalDB available?" -ForegroundColor Yellow
}

if (-not $NoBrowser -and $appStatus -ne 0) {
    Start-Process $appUrl
    if ($ApiDocs -and $apiStatus -ne 0) {
        Start-Process $apiDocsUrl
    }
}

Write-Host ''
Write-Host "App:      $appUrl"
Write-Host "API:      $apiHealthUrl"
Write-Host "API docs: $apiDocsUrl"
Write-Host ''

if ($started.Count -eq 0) {
    return
}

Read-Host 'Press Enter to stop the servers started by this script' | Out-Null

foreach ($process in $started) {
    if (-not $process.HasExited) {
        # /T also stops the child processes (dotnet, node) running inside each window.
        & taskkill.exe /PID $process.Id /T /F | Out-Null
    }
}
Write-Host 'Servers stopped.' -ForegroundColor Green
