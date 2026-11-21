# Installs the Fade Master + APC Executor Link plugins into DMXControl 3.
# Run from an elevated PowerShell (Program Files needs admin rights):
#   powershell -ExecutionPolicy Bypass -File .\install.ps1
param(
    [string]$DmxcDir = 'C:\Program Files (x86)\DMXControl3',
    # also copy the APC config into the plugin folder (otherwise it is created in %APPDATA%\DMXControl3)
    [switch]$WithApcConfig
)

$ErrorActionPreference = 'Stop'
$src = $PSScriptRoot

$running = Get-Process -Name 'Lumos', 'LumosGUI', 'DMXCLauncher', 'Umbra' -ErrorAction SilentlyContinue
if ($running) {
    Write-Host "DMXControl laeuft noch ($($running.Name -join ', ')). Bitte erst komplett beenden." -ForegroundColor Red
    exit 1
}

$targets = @(
    @{ From = 'Kernel\LumosFadeMasterPlugin';  To = 'Kernel\Plugins\LumosFadeMasterPlugin' },
    @{ From = 'GUI\LumosFadeMasterGuiPlugin';  To = 'GUI\Plugins\LumosFadeMasterGuiPlugin' },
    @{ From = 'GUI\LumosApcExecutorLink';      To = 'GUI\Plugins\LumosApcExecutorLink' },
    @{ From = 'GUI\LumosExecutorTouchGrid';    To = 'GUI\Plugins\LumosExecutorTouchGrid' }
)

# Remove stray copies of our plugins directly in the Plugins root (manual installs) - otherwise the same plugin
# would be loaded twice. 0Harmony.dll is only removed if it is byte-identical to ours.
foreach ($side in 'Kernel', 'GUI') {
    $root = Join-Path $DmxcDir "$side\Plugins"
    $ours = Get-ChildItem (Join-Path $src $side) -Recurse -File -ErrorAction SilentlyContinue
    foreach ($f in Get-ChildItem $root -File -ErrorAction SilentlyContinue) {
        $match = $ours | Where-Object { $_.Name -eq $f.Name } | Select-Object -First 1
        if (-not $match) { continue }
        if ($f.Name -eq '0Harmony.dll' -and (Get-FileHash $f.FullName).Hash -ne (Get-FileHash $match.FullName).Hash) { continue }
        Remove-Item $f.FullName -Force
        Write-Host "  entfernt (lose Kopie): $($f.FullName)" -ForegroundColor Yellow
    }
}

foreach ($t in $targets) {
    $from = Join-Path $src $t.From
    $to = Join-Path $DmxcDir $t.To
    New-Item -ItemType Directory -Force $to | Out-Null
    Get-ChildItem $from -File |
        Where-Object { $WithApcConfig -or $_.Extension -ne '.xml' } |
        ForEach-Object {
            Copy-Item $_.FullName $to -Force
            Write-Host "  $($_.Name) -> $to"
        }
}

Write-Host "Fertig. DMXControl starten und im Plugin-Fenster aktivieren:" -ForegroundColor Green
Write-Host "  Kernel: Cuelist Fade Master"
Write-Host "  GUI:    Cuelist Fade Master (GUI), APC Executor Link, Executor Touch Grid"
Write-Host "Das Touch-Fenster danach ueber Menue Windows -> 'Executor Touch' oeffnen."
