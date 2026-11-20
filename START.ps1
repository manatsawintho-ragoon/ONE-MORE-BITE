param([int]$Port = 5080, [switch]$NoBrowser)
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$runtimeRoot = Join-Path $projectRoot '.runtime'
$dataRoot = Join-Path $projectRoot 'data'
$process = $null
$launchLock = $null

try {
    if (-not [Environment]::Is64BitOperatingSystem) {
        throw 'This package needs 64-bit Windows 10 or Windows 11.'
    }
    New-Item -ItemType Directory -Path $runtimeRoot, $dataRoot -Force | Out-Null
    # Keep one running app per data folder, including when START is clicked twice.
    try {
        $launchLock = [IO.File]::Open((Join-Path $runtimeRoot 'launcher.lock'), 'OpenOrCreate', 'ReadWrite', 'None')
    } catch {
        throw 'ONE MORE BITE is already open. Use its existing window.'
    }
    $manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'runtime.json') -Raw | ConvertFrom-Json
    $appFolder = Join-Path $projectRoot 'app'
    if (-not (Test-Path -LiteralPath (Join-Path $appFolder 'KruaImDee.exe'))) {
        $versionFolder = Join-Path $runtimeRoot $manifest.version
        $appFolder = Join-Path $versionFolder 'app'
        if (-not (Test-Path -LiteralPath (Join-Path $appFolder 'KruaImDee.exe'))) {
            Write-Host 'Downloading ONE MORE BITE. This is needed only on the first start.'
            [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
            $download = Join-Path $runtimeRoot ('download-' + [Guid]::NewGuid().ToString('N') + '.zip')
            $ProgressPreference = 'SilentlyContinue'
            Invoke-WebRequest -Uri $manifest.url -OutFile $download -UseBasicParsing
            $actualHash = (Get-FileHash -LiteralPath $download -Algorithm SHA256).Hash
            if ($actualHash -ne $manifest.sha256) {
                throw 'The download is incomplete. Close this window and open START.cmd again.'
            }
            Expand-Archive -LiteralPath $download -DestinationPath $versionFolder -Force
            Remove-Item -LiteralPath $download
        }
    }
    $executable = Join-Path $appFolder 'KruaImDee.exe'
    if (-not (Test-Path -LiteralPath $executable)) { throw 'Application files are missing.' }
    $database = Join-Path $dataRoot 'restaurant.db'
    if (-not (Test-Path -LiteralPath $database)) {
        Copy-Item -LiteralPath (Join-Path $projectRoot 'database/restaurant.db') -Destination $database
    }
    # Try another local port if another program uses the preferred one.
    $selectedPort = 0
    for ($candidate = $Port; $candidate -lt $Port + 20; $candidate++) {
        $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, $candidate)
        try {
            $listener.Start()
            $selectedPort = $candidate
            break
        } catch { } finally { $listener.Stop() }
    }
    if ($selectedPort -eq 0) { throw 'No free local port. Close another copy and try again.' }
    $url = "http://127.0.0.1:$selectedPort"
    $env:ONE_MORE_BITE_DATA = $dataRoot
    $env:DatabaseProvider = 'Sqlite'
    $env:ASPNETCORE_ENVIRONMENT = 'Production'
    Remove-Item Env:ConnectionStrings__Restaurant -ErrorAction SilentlyContinue
    $process = Start-Process -FilePath $executable -WorkingDirectory $appFolder -ArgumentList @('--urls', $url, '--DatabaseProvider', 'Sqlite') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $dataRoot 'server.log') -RedirectStandardError (Join-Path $dataRoot 'error.log')
    $ready = $false
    for ($attempt = 0; $attempt -lt 90; $attempt++) {
        if ($process.HasExited) { throw 'Cannot start the app. See data/error.log.' }
        try {
            $reply = Invoke-RestMethod -Uri "$url/health/ready" -TimeoutSec 2
            if ($reply.application -eq 'ONE MORE BITE' -and $reply.database -eq 'Sqlite') {
                $ready = $true
                break
            }
        } catch { }
        Start-Sleep -Milliseconds 500
    }
    if (-not $ready) { throw 'The app did not become ready. See data/error.log.' }
    Write-Host "ONE MORE BITE is ready: $url" -ForegroundColor Green
    Write-Host 'Your data is saved in the data folder.'
    if (-not $NoBrowser) { Start-Process $url }
    Read-Host 'Keep this window open while using the website. Press Enter to stop' | Out-Null
} catch {
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
} finally {
    if ($process -and -not $process.HasExited) { Stop-Process -Id $process.Id }
    if ($launchLock) { $launchLock.Dispose() }
}
