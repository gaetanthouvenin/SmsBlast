plugins {
    id("com.android.application")
}

android {
    namespace = "com.smsblast.relay"
    compileSdk = 34

    defaultConfig {
        applicationId = "com.smsblast.relay"
        minSdk = 23
        targetSdk = 34
        versionCode = 1
        versionName = "1.0"
    }

    signingConfigs {
        getByName("debug") {
            // Même keystore que scripts/build-apk.sh : les deux chaînes de
            // compilation produisent donc des APK à signature identique, et une
            // réinstallation par-dessus dist/SmsRelay.apk ne sera pas rejetée.
            val shared = rootProject.file("debug.keystore")
            if (shared.exists()) {
                storeFile = shared
                storePassword = "android"
                keyAlias = "androiddebugkey"
                keyPassword = "android"
            }
        }
    }

    buildTypes {
        release {
            isMinifyEnabled = false
            signingConfig = signingConfigs.getByName("debug")
        }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_1_8
        targetCompatibility = JavaVersion.VERSION_1_8
    }
}

dependencies {
    // Aucune dépendance : uniquement le framework Android.
}
