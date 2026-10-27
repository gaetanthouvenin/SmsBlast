#!/usr/bin/env bash
#
# Construit SmsRelay.apk sans Gradle, avec la chaîne d'outils Android classique.
# Utile en CI, en environnement hors ligne, ou simplement pour aller vite :
# l'application n'a aucune ressource ni dépendance, donc aapt + javac + dx suffisent.
#
# Debian / Ubuntu :
#   sudo apt-get install -y openjdk-17-jdk-headless android-sdk-platform-23 \
#                           android-sdk-build-tools aapt apksigner zipalign
#
# Windows (Git Bash), avec le SDK installé par sdkmanager :
#   BT="$LOCALAPPDATA/Android/Sdk/build-tools/34.0.0"
#   export PATH="/c/Users/<vous>/AppData/Local/Java/jdk-17/bin:$PATH"   # javac 17
#   ANDROID_JAR="$LOCALAPPDATA/Android/Sdk/platforms/android-34/android.jar" \
#   AAPT="$BT/aapt.exe" D8="$BT/d8.bat" ZIPALIGN="$BT/zipalign.exe" APKSIGNER="$BT/apksigner.bat" \
#   ./scripts/build-apk.sh
#
# Variables surchargeables : ANDROID_JAR, AAPT, D8 (ou DX), ZIPALIGN, APKSIGNER, OUT_DIR
#
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
APP="$ROOT/android/SmsRelay/app/src/main"
OUT_DIR="${OUT_DIR:-$ROOT/build/apk}"
KEYSTORE="$ROOT/android/SmsRelay/debug.keystore"

PACKAGE="com.smsblast.relay"
MIN_SDK=23
TARGET_SDK=34
VERSION_CODE=1
VERSION_NAME="1.0"

ANDROID_JAR="${ANDROID_JAR:-/usr/lib/android-sdk/platforms/android-23/android.jar}"
AAPT="${AAPT:-aapt}"
D8="${D8:-d8}"
DX="${DX:-}"                 # ancien dx (dalvik-exchange) : facultatif, d8 est la référence
ZIPALIGN="${ZIPALIGN:-zipalign}"
APKSIGNER="${APKSIGNER:-apksigner}"

[ -f "$ANDROID_JAR" ] || { echo "android.jar introuvable : $ANDROID_JAR" >&2; exit 1; }

rm -rf "$OUT_DIR"
mkdir -p "$OUT_DIR/classes"

echo "== Compilation Java (cible 1.8, bootclasspath = android.jar)"
javac -source 8 -target 8 -nowarn \
      -encoding UTF-8 \
      -bootclasspath "$ANDROID_JAR" \
      -classpath "$ANDROID_JAR" \
      -d "$OUT_DIR/classes" \
      "$APP"/java/com/smsblast/relay/*.java

echo "== Conversion en bytecode Dalvik"
if [ -n "$DX" ]; then
    "$DX" --dex --min-sdk-version=$MIN_SDK --output="$OUT_DIR/classes.dex" "$OUT_DIR/classes"
else
    # d8 (build-tools >= 28) écrit classes.dex dans le dossier de sortie.
    find "$OUT_DIR/classes" -name "*.class" -print0 \
        | xargs -0 "$D8" --release --min-api $MIN_SDK --lib "$ANDROID_JAR" --output "$OUT_DIR"
fi

echo "== Empaquetage du manifeste"
# Le manifeste source ne porte ni « package » ni « uses-sdk » : le plugin Gradle
# les injecte depuis namespace / defaultConfig, et les y écrire ferait échouer AGP 8.
# On les ajoute donc dans une copie temporaire, à l'usage exclusif d'aapt.
MANIFEST="$OUT_DIR/AndroidManifest.xml"
sed -e "s|<manifest xmlns:android=\"http://schemas.android.com/apk/res/android\">|<manifest xmlns:android=\"http://schemas.android.com/apk/res/android\"\n    package=\"$PACKAGE\"\n    android:versionCode=\"$VERSION_CODE\"\n    android:versionName=\"$VERSION_NAME\">\n    <uses-sdk android:minSdkVersion=\"$MIN_SDK\" android:targetSdkVersion=\"$TARGET_SDK\" />|" \
    "$APP/AndroidManifest.xml" > "$MANIFEST"

grep -q "package=\"$PACKAGE\"" "$MANIFEST" || {
    echo "Injection du package dans le manifeste impossible." >&2; exit 1; }

"$AAPT" package -f \
        -M "$MANIFEST" \
        -I "$ANDROID_JAR" \
        -F "$OUT_DIR/unsigned.apk"

echo "== Ajout du dex"
( cd "$OUT_DIR" && "$AAPT" add -f unsigned.apk classes.dex >/dev/null )

if [ ! -f "$KEYSTORE" ]; then
    echo "== Génération du keystore de debug"
    keytool -genkeypair -noprompt \
            -keystore "$KEYSTORE" -storepass android -keypass android \
            -alias androiddebugkey -keyalg RSA -keysize 2048 -validity 10950 \
            -dname "CN=SmsBlast Debug, O=SmsBlast, C=FR"
fi

echo "== Alignement et signature"
"$ZIPALIGN" -f 4 "$OUT_DIR/unsigned.apk" "$OUT_DIR/aligned.apk"
"$APKSIGNER" sign \
        --ks "$KEYSTORE" --ks-pass pass:android --key-pass pass:android \
        --ks-key-alias androiddebugkey \
        --out "$OUT_DIR/SmsRelay.apk" \
        "$OUT_DIR/aligned.apk"

"$APKSIGNER" verify --print-certs "$OUT_DIR/SmsRelay.apk"
rm -f "$OUT_DIR/unsigned.apk" "$OUT_DIR/aligned.apk" "$OUT_DIR/SmsRelay.apk.idsig"

echo
echo "APK prêt : $OUT_DIR/SmsRelay.apk"
