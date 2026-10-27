<#
    Prépare un téléphone Android pour SmsBlast : installe le relais, accorde la
    permission SEND_SMS et sort l'application de l'état « stopped ».

    Exemple :
        .\preparer-telephone.ps1 -Apk ..\android\SmsRelay\app\build\outputs\apk\release\app-release.apk
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Apk,
    [string]$Serial,
    [string]$Adb = 'adb'
)

$ErrorActionPreference = 'Stop'
$package = 'com.smsblast.relay'

function Invoke-Adb {
    param([string[]]$Arguments)
    $all = @()
    if ($Serial) { $all += @('-s', $Serial) }
    $all += $Arguments
    Write-Host "> adb $($all -join ' ')" -ForegroundColor DarkGray
    & $Adb @all
}

if (-not (Test-Path $Apk)) { throw "APK introuvable : $Apk" }

Write-Host "`n=== Appareils détectés ===" -ForegroundColor Cyan
& $Adb devices -l

Write-Host "`n=== Installation du relais ===" -ForegroundColor Cyan
Invoke-Adb @('install', '-r', '-g', (Resolve-Path $Apk).Path)

Write-Host "`n=== Permission SEND_SMS ===" -ForegroundColor Cyan
try {
    Invoke-Adb @('shell', 'pm', 'grant', $package, 'android.permission.SEND_SMS')
    Write-Host 'Permission accordée.' -ForegroundColor Green
} catch {
    Write-Warning "pm grant a échoué (fréquent sur Xiaomi/Huawei). Accordez la permission depuis l'application."
}

Write-Host "`n=== Lancement de l'application ===" -ForegroundColor Cyan
Invoke-Adb @('shell', 'am', 'start', '-n', "$package/.MainActivity")

Write-Host "`n=== Vérification ===" -ForegroundColor Cyan
Invoke-Adb @('shell', 'pm', 'list', 'packages', $package)

Write-Host "`nTéléphone prêt. Test d'envoi manuel :" -ForegroundColor Green
Write-Host '  adb shell am broadcast -n com.smsblast.relay/.SendSmsReceiver -f 0x00000020 \'
Write-Host '    --es id test --es num_b64 <numero-base64url> --es msg_b64 <message-base64url>'
