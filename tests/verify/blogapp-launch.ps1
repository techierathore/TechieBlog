# Boots BlogApp (the MAUI Blazor Hybrid desktop head) with WebView2 remote debugging so the
# Playwright specs can attach through CDP_URL. Prints the PID it started; stop it with
#   Stop-Process -Id <pid>   (never taskkill /IM — other BlogApp instances may be the owner's).
#
#   powershell.exe -NoProfile -File tests\verify\blogapp-launch.ps1 [-Port 9333]
#   then, from WSL:  CDP_URL=http://127.0.0.1:9333 npx playwright test blogapp-
#
# BlogApp reads its database from its OWN stored connection (securestorage.dat). The specs configure
# it for the LOCAL database (localhost:5550) through its connection screen before any write, and
# refuse to continue when the screen does not report localhost:5550.
param([int]$Port = 9333)

$exe = Join-Path $PSScriptRoot '..\..\source\BlogApp\bin\Debug\net10.0-windows10.0.19041.0\win-x64\BlogApp.exe'
$exe = (Resolve-Path $exe).Path
$env:WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS = "--remote-debugging-port=$Port --remote-allow-origins=*"
$p = Start-Process -FilePath $exe -PassThru
Write-Output "BLOGAPP_PID=$($p.Id)"
$deadline = (Get-Date).AddSeconds(90)
while ((Get-Date) -lt $deadline) {
    try {
        $r = Invoke-WebRequest -UseBasicParsing -Uri "http://127.0.0.1:$Port/json/version" -TimeoutSec 2
        if ($r.StatusCode -eq 200) { Write-Output "CDP_READY=http://127.0.0.1:$Port"; break }
    } catch { Start-Sleep -Milliseconds 500 }
}
