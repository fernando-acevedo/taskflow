<#
.SYNOPSIS
    Starts TaskFlow for local development (database, API and frontend) and opens the app in the browser.

.DESCRIPTION
    1. Database. With Docker available, makes sure the SQL Server container from docker-compose.yml is up
       and healthy (starting Docker Desktop and the container if needed) and points the API at it.
       Without Docker it falls back to SQL Server LocalDB. Choose explicitly with -Database.
    2. Opens one window per server (API, frontend) so their logs stay readable.
    3. Waits until both answer, then opens http://localhost:4200.
    Press Enter in this window to stop the servers it started. The database container keeps running so
    the next start is fast; add -StopDatabase to stop it too.
    A server that is already listening on its port is reused instead of started again.

.EXAMPLE
    .\dev.ps1                     # Docker if available, otherwise LocalDB
    .\dev.ps1 -Database Docker    # require the SQL Server container
    .\dev.ps1 -Database LocalDb   # skip Docker
    .\dev.ps1 -ApiDocs            # also open the Scalar API reference
    .\dev.ps1 -NoBrowser -StopDatabase
#>
[CmdletBinding()]
param(
    [ValidateSet('Auto', 'Docker', 'LocalDb')]
    [string]$Database = 'Auto',
    [switch]$ApiDocs,
    [switch]$NoBrowser,
    [switch]$StopDatabase,
    [int]$TimeoutSeconds = 180
)

$ErrorActionPreference = 'Stop'

# Show failures as a plain message instead of a PowerShell stack trace.
trap {
    Write-Host ''
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
}

$root = $PSScriptRoot
$backendDir = Join-Path $root 'backend'
$frontendDir = Join-Path $root 'frontend'
$envFile = Join-Path $root '.env'
$apiPort = 5080
$appPort = 4200
$apiHealthUrl = "http://localhost:$apiPort/health"
$apiDocsUrl = "http://localhost:$apiPort/scalar/v1"
$appUrl = "http://localhost:$appPort"

# Runs a native command without letting its stderr turn into a PowerShell error, and returns the exit code
# and the combined output.
function Invoke-Native([scriptblock]$Command) {
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = & $Command 2>&1 | Out-String
        return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $output.Trim() }
    }
    finally {
        $ErrorActionPreference = $previous
    }
}

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

function Test-DockerEngine {
    return (Invoke-Native { docker info --format '{{.ServerVersion}}' }).ExitCode -eq 0
}

# Returns $true once the Docker engine answers, starting Docker Desktop if it is installed but not running.
function Initialize-DockerEngine {
    if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
        return $false
    }
    if (Test-DockerEngine) {
        return $true
    }

    $desktop = Join-Path $env:ProgramFiles 'Docker\Docker\Docker Desktop.exe'
    if (-not (Test-Path $desktop)) {
        return $false
    }

    Write-Host 'Starting Docker Desktop' -NoNewline -ForegroundColor Cyan
    Start-Process -FilePath $desktop | Out-Null
    $deadline = (Get-Date).AddSeconds(120)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Seconds 3
        Write-Host '.' -NoNewline
        if (Test-DockerEngine) {
            Write-Host ' ready' -ForegroundColor Green
            return $true
        }
    }
    Write-Host ' timed out' -ForegroundColor Red
    return $false
}

function Read-EnvFile {
    $values = @{}
    if (Test-Path $envFile) {
        foreach ($line in Get-Content $envFile) {
            if ($line -match '^\s*([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(.*)\s*$') {
                $values[$Matches[1]] = $Matches[2].Trim('"')
            }
        }
    }
    return $values
}

# A random password that meets SQL Server's complexity rules and is safe inside a connection string.
function New-SqlPassword {
    $alphabet = 'abcdefghijkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789'
    $bytes = New-Object byte[] 24
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $rng.GetBytes($bytes)
    }
    finally {
        $rng.Dispose()
    }
    $random = -join ($bytes | ForEach-Object { $alphabet[$_ % $alphabet.Length] })
    return "Tf-$random-9x"
}

# Makes sure .env holds SA_PASSWORD, then returns the settings docker compose will use.
function Get-DockerSettings {
    $values = Read-EnvFile
    if (-not $values.ContainsKey('SA_PASSWORD') -or -not $values['SA_PASSWORD']) {
        $values['SA_PASSWORD'] = New-SqlPassword
        Add-Content -Path $envFile -Value "SA_PASSWORD=$($values['SA_PASSWORD'])" -Encoding ascii
        Write-Host "Created .env with a random SQL Server password (git-ignored)." -ForegroundColor Cyan
    }
    if (-not $values.ContainsKey('SQL_PORT') -or -not $values['SQL_PORT']) {
        $values['SQL_PORT'] = '1433'
    }
    return $values
}

# Starts the SQL Server container if needed and waits until its health check passes.
function Start-DatabaseContainer([hashtable]$Settings) {
    Push-Location $root
    try {
        $running = (Invoke-Native { docker compose ps --status running --services }).Output -split "`r?`n"
        if ($running -contains 'sqlserver') {
            Write-Host 'SQL Server container is already running; checking its health...' -ForegroundColor Cyan
        }
        else {
            if (Test-PortInUse ([int]$Settings['SQL_PORT'])) {
                throw "Port $($Settings['SQL_PORT']) is already in use (another SQL Server?). Set SQL_PORT in .env to a free port, or use -Database LocalDb."
            }
            Write-Host 'Starting the SQL Server container (the first run downloads the image, ~1.5 GB)...' -ForegroundColor Cyan
        }

        $up = Invoke-Native { docker compose up -d --wait sqlserver }
        if ($up.ExitCode -ne 0) {
            throw "docker compose could not start SQL Server:`n$($up.Output)"
        }
        Write-Host 'SQL Server container is healthy.' -ForegroundColor Green
    }
    finally {
        Pop-Location
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

# 1. Database
$useDocker = $false
if ($Database -ne 'LocalDb') {
    $useDocker = Initialize-DockerEngine
    if (-not $useDocker -and $Database -eq 'Docker') {
        throw 'Docker is not available. Install Docker Desktop (https://www.docker.com/products/docker-desktop/) and start it, or run with -Database LocalDb.'
    }
}

$apiEnvironment = ''
if ($useDocker) {
    $settings = Get-DockerSettings
    Start-DatabaseContainer $settings
    $connectionString = "Server=localhost,$($settings['SQL_PORT']);Database=TaskFlow;User Id=sa;Password=$($settings['SA_PASSWORD']);TrustServerCertificate=True"
    # Overrides the LocalDB connection string from appsettings.Development.json for this API window only.
    $apiEnvironment = "`$env:ConnectionStrings__TaskFlow = '$connectionString'; "
    $databaseLabel = "SQL Server in Docker (localhost,$($settings['SQL_PORT']))"
}
else {
    if ($Database -eq 'Auto') {
        Write-Host 'Docker is not available; using SQL Server LocalDB.' -ForegroundColor Yellow
    }
    if (-not (Get-Command sqllocaldb -ErrorAction SilentlyContinue)) {
        throw 'SQL Server LocalDB was not found. Install Docker Desktop, or LocalDB (included with Visual Studio or the SQL Server Express installer).'
    }
    $databaseLabel = 'SQL Server LocalDB ((localdb)\MSSQLLocalDB)'
}

# 2. Frontend packages
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

# 3. Servers
$started = @()
$ownedPorts = @()

if (Test-PortInUse $apiPort) {
    Write-Host "Port $apiPort is already in use; reusing the running API (it keeps whatever database it was started with)." -ForegroundColor Yellow
}
else {
    Write-Host 'Starting API...' -ForegroundColor Cyan
    $started += Start-ServerWindow 'TaskFlow API' $backendDir "${apiEnvironment}dotnet run --project src/TaskFlow.Api"
    $ownedPorts += $apiPort
}

if (Test-PortInUse $appPort) {
    Write-Host "Port $appPort is already in use; reusing the running frontend." -ForegroundColor Yellow
}
else {
    Write-Host 'Starting frontend...' -ForegroundColor Cyan
    $started += Start-ServerWindow 'TaskFlow frontend' $frontendDir "npm start -- --port $appPort"
    $ownedPorts += $appPort
}

$apiStatus = Wait-ForUrl $apiHealthUrl 'API'
$appStatus = Wait-ForUrl $appUrl 'frontend'

if ($apiStatus -eq 0 -or $appStatus -eq 0) {
    Write-Host 'A server did not start in time. Check its window for errors.' -ForegroundColor Red
}
elseif ($apiStatus -ne 200) {
    Write-Host "The API reports a problem (HTTP $apiStatus). Check the database and the API window." -ForegroundColor Yellow
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
Write-Host "Database: $databaseLabel"
Write-Host 'Sign in:  admin@taskflow.local / Admin12345 (development only)'
Write-Host ''

if ($started.Count -eq 0 -and -not ($useDocker -and $StopDatabase)) {
    return
}

Read-Host 'Press Enter to stop the servers started by this script' | Out-Null

# The process listening on a port can outlive its window's process tree (dotnet run hands off to the
# app host), so stop the listeners on the ports this script started as well as the windows.
$listenerIds = @(Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue |
    Where-Object { $ownedPorts -contains $_.LocalPort } |
    Select-Object -ExpandProperty OwningProcess -Unique)

foreach ($process in $started) {
    if (-not $process.HasExited) {
        # /T also stops the child processes (dotnet, node) running inside each window.
        & taskkill.exe /PID $process.Id /T /F 2>$null | Out-Null
    }
}
foreach ($id in $listenerIds) {
    Stop-Process -Id $id -Force -ErrorAction SilentlyContinue
}
Write-Host 'Servers stopped.' -ForegroundColor Green

if ($useDocker) {
    if ($StopDatabase) {
        Push-Location $root
        try {
            Invoke-Native { docker compose stop sqlserver } | Out-Null
        }
        finally {
            Pop-Location
        }
        Write-Host 'SQL Server container stopped (data is kept in the sqlserver-data volume).' -ForegroundColor Green
    }
    else {
        Write-Host 'The SQL Server container is still running for a faster next start. Stop it with: docker compose stop' -ForegroundColor DarkGray
    }
}
