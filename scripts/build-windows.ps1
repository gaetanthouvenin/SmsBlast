<#
    Compile l'application WPF et rassemble tout le nécessaire dans .\publish :
    SmsBlast.exe et l'APK du relais côte à côte, ce qui permet au bouton
    « Préparer le téléphone » de proposer l'APK directement.

    Usage :
        .\scripts\build-windows.ps1                 # exécutable autonome, sans prérequis
        .\scripts\build-windows.ps1 -Framework      # plus léger, exige le runtime .NET 8 Desktop
        .\scripts\build-windows.ps1 -SkipTests
#>
[CmdletBinding()]
param(
    [switch]$Framework,
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root

try {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw "SDK .NET 8 introuvable. Installez-le : https://dotnet.microsoft.com/download/dotnet/8.0"
    }

    if (-not $SkipTests) {
        Write-Host "`n=== Tests de la logique métier ===" -ForegroundColor Cyan
        dotnet run --project src/SmsBlast.Tests -c Release -- samples
        if ($LASTEXITCODE -ne 0) { throw "Les tests ont échoué." }
    }

    Write-Host "`n=== Publication de l'application ===" -ForegroundColor Cyan
    $selfContained = (-not $Framework).ToString().ToLower()
    dotnet publish src/SmsBlast.App/SmsBlast.App.csproj `
        -c Release -r win-x64 `
        --self-contained $selfContained `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -o publish
    if ($LASTEXITCODE -ne 0) { throw "La publication a échoué." }

    $apk = Join-Path $root 'dist\SmsRelay.apk'
    if (Test-Path $apk) {
        Copy-Item $apk (Join-Path $root 'publish\SmsRelay.apk') -Force
        Write-Host "APK du relais copié à côté de l'exécutable." -ForegroundColor Green
    } else {
        Write-Warning "dist\SmsRelay.apk absent : compilez-le ou récupérez-le depuis la CI."
    }

    Write-Host "`nPrêt : $(Join-Path $root 'publish\SmsBlast.exe')" -ForegroundColor Green
}
finally {
    Pop-Location
}
