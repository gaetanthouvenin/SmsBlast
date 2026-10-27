# SmsBlast

Envoi de SMS personnalisés à partir d'un fichier Excel, via un téléphone Android piloté en ADB.

L'outil se compose de deux morceaux :

| Pièce | Techno | Rôle |
|---|---|---|
| `src/SmsBlast.App` | WPF, .NET 8 | Lit le classeur, normalise les numéros, compose les messages, pilote `adb.exe` |
| `android/SmsRelay` | Java, Android | Relais installé sur le téléphone : reçoit l'ordre par broadcast et envoie le SMS |

Le relais est nécessaire parce qu'`adb shell service call isms` est refusé par le
système sur Android 10 et suivants : le shell ADB (uid 2000) ne détient pas la
permission `SEND_SMS`. Une application qui la détient, déclenchée par broadcast
explicite, est la seule voie fiable et silencieuse sans root.

## Pourquoi zéro dépendance NuGet

Tout est écrit sur le framework : lecture `.xlsx` par `System.IO.Compression` +
`System.Xml.Linq`, normalisation des numéros à la main, MVVM léger. `dotnet build`
fonctionne sans restore réseau. Pour passer à ClosedXML ou EPPlus, seul
`Core/TabularReader.cs` est à remplacer.

---

## Démarrage rapide

`dist/SmsRelay.apk` est **déjà compilé et signé** — vous pouvez l'installer tout de
suite. Il ne reste qu'à compiler l'application Windows :

```powershell
.\scripts\build-windows.ps1                     # -> publish\SmsBlast.exe + SmsRelay.apk
.\scripts\preparer-telephone.ps1 -Apk .\dist\SmsRelay.apk
.\publish\SmsBlast.exe
```

Le premier script exige uniquement le **SDK .NET 8**. Le second exige `adb` dans le
PATH et le téléphone branché en débogage USB.

## 1. Le relais Android

### Installer l'APK fourni

Sur le téléphone : **Options pour les développeurs → Débogage USB**, puis branchez-le
et acceptez la clé RSA. Ensuite, depuis le PC :

```powershell
.\scripts\preparer-telephone.ps1 -Apk .\dist\SmsRelay.apk
```

ou, à la main :

```
adb install -r -g dist\SmsRelay.apk
adb shell pm grant com.smsblast.relay android.permission.SEND_SMS
adb shell am start -n com.smsblast.relay/.MainActivity
```

Le bouton **« Préparer le téléphone »** de l'application WPF fait exactement ces
trois opérations, et propose `SmsRelay.apk` par défaut lorsqu'il se trouve à côté
de l'exécutable.

### Le recompiler vous-même

L'APK livré est signé avec une clé de debug générée pour ce projet
(`android/SmsRelay/debug.keystore`, mot de passe `android`) : suffisant pour un
sideload, à remplacer par votre keystore pour toute diffusion interne. Recompiler
est de toute façon une bonne idée avant d'accorder `SEND_SMS` à un binaire.

Deux chaînes de compilation produisent le même APK.

**Gradle** — la référence, si vous avez Android Studio ou le SDK Android :

```bash
cd android/SmsRelay
gradle wrapper --gradle-version 8.9   # une seule fois ; Android Studio le fait aussi
./gradlew assembleRelease             # gradlew.bat sous Windows
# -> app/build/outputs/apk/release/app-release.apk
```

**Sans Gradle** — l'application n'ayant ni ressource ni dépendance, `aapt` + `javac`
+ `d8` suffisent. C'est ainsi que `dist/SmsRelay.apk` a été produit :

```bash
sudo apt-get install -y openjdk-17-jdk-headless android-sdk-platform-23 \
                        android-sdk-build-tools aapt apksigner zipalign
./scripts/build-apk.sh                # -> build/apk/SmsRelay.apk
```

Sous Windows (Git Bash), avec un JDK 17 et le SDK installé par `sdkmanager`
(`platforms;android-34` et `build-tools;34.0.0`), les chemins des outils se passent
en variables d'environnement ; l'en-tête du script donne l'exemple complet.

L'APK fourni est compilé contre `android.jar` de l'API 23 — d'où l'absence de
lambdas et la constante `5` écrite en dur pour `RESULT_ERROR_LIMIT_EXCEEDED` dans
`SmsStatusReceiver`. Il déclare `minSdkVersion 23` / `targetSdkVersion 34`, une
seule permission (`SEND_SMS`), et est signé v1 + v2 + v3.

### Le lancement de l'activité n'est pas décoratif

Une application jamais ouverte reste à l'état *stopped* et ne reçoit aucun
broadcast. Le PC envoie de toute façon `FLAG_INCLUDE_STOPPED_PACKAGES`
(`-f 0x00000020`) en complément. Si `pm grant` échoue — certains constructeurs le
bloquent — accordez la permission depuis l'écran de l'application, qui affiche son
état en clair.

## 2. L'application PC

Prérequis : **SDK .NET 8**. Aucun paquet NuGet n'est nécessaire.

```powershell
.\scripts\build-windows.ps1          # tests + exécutable autonome dans .\publish
```

ou, pendant le développement :

```bash
dotnet build SmsBlast.sln -c Release
dotnet run --project src/SmsBlast.App
```

`build-windows.ps1` produit par défaut un exécutable **autonome** (aucun runtime à
installer sur la machine cible, ~150 Mo) ; `-Framework` produit un binaire léger qui
exige le *.NET 8 Desktop Runtime*.

`adb.exe` est cherché automatiquement dans le PATH, dans
`%LOCALAPPDATA%\Android\Sdk\platform-tools` et à côté de l'exécutable ; le chemin
reste modifiable dans l'interface.

---

## Format du fichier source

`.xlsx`, `.xlsm`, `.csv` ou `.txt`. Une ligne d'en-tête, une ligne par destinataire.
`samples/contacts-exemple.xlsx` sert de modèle.

| Prénom | Nom | Portable | Société | Rendez-vous | Montant |
|---|---|---|---|---|---|
| Gaëtan | Thouvenin | 06 12 34 56 78 | Dupont | 15/09/2026 | 120 |

La colonne de numéros est devinée (en-tête contenant *portable*, *mobile*, *gsm*,
*tel*…, sinon par analyse du contenu) et reste modifiable.

Formats acceptés pour un numéro français : `06 12 34 56 78`, `06.12.34.56.78`,
`0612345678`, `612345678`, `+33612345678`, `0033612345678`. Un numéro stocké en
colonne *Nombre* dans Excel perd son zéro initial ; c'est géré. En revanche une
cellule affichée `6,12E+08` est rejetée avec un message explicite : repassez la
colonne en **Texte** dans Excel.

Chaque ligne est classée **Mobile**, **Fixe**, **Étranger** ou **Invalide**, et les
doublons sont signalés. Par défaut seuls les mobiles non dupliqués sont cochés.

## Gabarit du message

`{{Colonne}}` insère la valeur de la colonne, insensible à la casse et aux espaces.
`{{Colonne|repli}}` fournit une valeur de secours si la cellule est vide.

```
Bonjour {{Prénom|cher client}}, votre rendez-vous chez {{Société}}
est confirmé le {{Rendez-vous}}. Solde à régler : {{Montant}} EUR.
```

Un double-clic sur un nom de colonne l'insère au curseur. Le bandeau sous l'éditeur
indique le nombre de caractères et de **segments SMS facturés** : 160 caractères en
alphabet GSM, mais 70 seulement dès qu'un caractère en sort — `ç`, `œ`, `«` ou un
emoji font basculer tout le message en Unicode et doublent la facture.

## Envoyer

1. **Simulation** (cochée par défaut) : rien ne part, le journal montre la commande
   `adb` exacte et le message rendu pour chaque destinataire.
2. **Numéro de test** : envoie le premier message rendu à vous-même.
3. Décochez *Simulation*, cliquez **ENVOYER**, confirmez le récapitulatif.

Le délai entre envois (4 s par défaut) est augmenté d'un aléa de 0 à 25 % : une
cadence parfaitement régulière est un marqueur de spam pour les filtres opérateur.

Deux niveaux d'accusé sont remontés :

- **acceptation immédiate** — le relais a pris l'ordre (`result=0` renvoyé par
  `am broadcast`) ;
- **accusé d'émission** — relu dans `logcat -s SMSRELAY` en fin de campagne, il
  distingue *remis à l'opérateur* de *pas de réseau*, *radio éteinte*, *quota dépassé*.

Le bouton **Exporter le rapport** produit un CSV `;` en UTF-8 avec BOM, directement
ouvrable dans Excel.

---

## Pièges connus

**Quota SMS d'Android.** Une application qui n'est pas l'app SMS par défaut est
surveillée par `SmsUsageMonitor` : au-delà d'un certain nombre de SMS dans une
fenêtre glissante, le système affiche une boîte de dialogue de confirmation *sur le
téléphone*, ce qui bloque une campagne non surveillée. Pour connaître et relever le
seuil de votre appareil :

```
adb shell settings get global sms_outgoing_check_max_count
adb shell settings get global sms_outgoing_check_interval_ms
adb shell settings put global sms_outgoing_check_max_count 200
```

(`null` signifie « valeur par défaut du framework », souvent 30 par tranche de
30 minutes.) La modification par `settings put` est réversible et disparaît à la
réinitialisation d'usine.

**Coût.** Chaque message part sur le forfait de la carte SIM. Un message Unicode de
200 caractères, c'est 3 SMS facturés par destinataire.

**Écran et câble.** Le téléphone doit rester branché et déverrouillé pendant la
campagne. Un déverrouillage biométrique qui expire coupe l'envoi.

**Double SIM.** Le relais accepte un extra `--ei sub <subscriptionId>` pour choisir
la carte ; l'interface n'expose pas encore ce réglage et utilise la SIM par défaut.

**Ce que l'outil ne fait pas.** Pas de gestion des réponses entrantes, pas de
désinscription automatique, pas d'accusé de *réception* (seulement d'émission — les
accusés de réception dépendent de l'opérateur).

## Cadre légal

Pour tout message autre que relationnel (rappel de rendez-vous demandé par le
destinataire, information de service), la prospection par SMS en France impose un
**consentement préalable** et la mention d'un moyen d'opposition — historiquement
« STOP au 3xxxx », impossible à fournir depuis un numéro personnel. Un envoi
commercial de masse depuis une SIM grand public sort du cadre prévu par les CGU des
opérateurs et n'offre aucune traçabilité en cas de contrôle.

Pour de la prospection ou du volume récurrent, une passerelle SMS
(OVHcloud, SMSFactor, Brevo, Twilio, Vonage) reste la bonne réponse : SDK .NET,
statuts de livraison, gestion du STOP, et un numéro d'expéditeur déclaré.
SmsBlast vise l'envoi ponctuel et légitime depuis sa propre ligne.

## Tests

```bash
dotnet run --project src/SmsBlast.Tests -- samples
```

133 assertions couvrent la normalisation des numéros, le moteur de gabarit, le
comptage des segments SMS, la lecture `.xlsx` / `.csv` et la construction des
commandes `adb` — notamment le fait qu'un message contenant `; $( ) " '` traverse
le shell Android sans dommage, grâce à l'encodage base64 URL-safe des extras.

## Intégration continue

`.github/workflows/build.yml` construit tout à chaque push : le harnais de test sur
Ubuntu, l'APK par Gradle, et l'application WPF publiée en exécutable autonome sur un
*runner* Windows. Les deux binaires sont déposés en artefacts, et attachés
automatiquement à une *release* si vous poussez un tag `v*` :

```bash
git tag v1.0.0 && git push origin v1.0.0
```

## Arborescence

```
SmsBlast.sln
dist/SmsRelay.apk          APK du relais, compilé et signé, prêt à installer
src/SmsBlast.App/          Application WPF
  Core/                    Logique métier, sans dépendance UI
    Base64Url.cs           Encodage sûr pour la ligne de commande
    PhoneNormalizer.cs     Numéros -> E.164, mobile / fixe / invalide
    MessageTemplate.cs     Moteur {{Colonne}}
    SmsInfo.cs             GSM-7 vs UCS-2, segments facturés
    TabularReader.cs       Lecteur .xlsx et .csv
    AdbClient.cs           Exécution d'adb.exe
    SmsRelayClient.cs      Dialogue avec le relais Android
    ReportWriter.cs        Rapport CSV
src/SmsBlast.Tests/        Harnais de test sans dépendance
android/SmsRelay/          Relais Android (Java, 3 fichiers)
scripts/
  build-windows.ps1        Tests + publication de l'exécutable
  build-apk.sh             Compilation de l'APK sans Gradle
  preparer-telephone.ps1   Installation et permission sur le téléphone
samples/                   Classeur d'exemple
.github/workflows/         CI : tests, APK, exécutable Windows, release
```
