package com.smsblast.relay;

import android.app.Activity;
import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;
import android.telephony.SmsManager;
import android.util.Log;

/**
 * Reçoit l'accusé d'émission de chaque segment SMS et le trace dans logcat.
 * Côté PC : adb logcat -d -s SMSRELAY
 */
public class SmsStatusReceiver extends BroadcastReceiver {

    @Override
    public void onReceive(Context context, Intent intent) {
        String id = intent.getStringExtra("id");
        if (id == null) {
            id = "-";
        }
        int part = intent.getIntExtra("part", 1);
        int total = intent.getIntExtra("total", 1);

        String label;
        switch (getResultCode()) {
            case Activity.RESULT_OK:
                label = "OK";
                break;
            case SmsManager.RESULT_ERROR_GENERIC_FAILURE:
                label = "ERREUR_GENERIQUE";
                break;
            case SmsManager.RESULT_ERROR_NO_SERVICE:
                label = "PAS_DE_RESEAU";
                break;
            case SmsManager.RESULT_ERROR_NULL_PDU:
                label = "PDU_NULL";
                break;
            case SmsManager.RESULT_ERROR_RADIO_OFF:
                label = "RADIO_ETEINTE";
                break;
            // RESULT_ERROR_LIMIT_EXCEEDED : constante ajoutée tardivement au SDK,
            // écrite en dur pour rester compilable contre android.jar API 23.
            case 5:
                label = "QUOTA_DEPASSE";
                break;
            default:
                label = "CODE_" + getResultCode();
                break;
        }

        Log.i(SendSmsReceiver.TAG,
                "SENT id=" + id + " part=" + part + "/" + total + " status=" + label);
    }
}
