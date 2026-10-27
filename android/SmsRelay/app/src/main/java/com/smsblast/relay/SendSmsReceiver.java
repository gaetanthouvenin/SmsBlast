package com.smsblast.relay;

import android.Manifest;
import android.app.PendingIntent;
import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.telephony.SmsManager;
import android.util.Base64;
import android.util.Log;

import java.io.UnsupportedEncodingException;
import java.util.ArrayList;
import java.util.concurrent.atomic.AtomicInteger;

/**
 * Reçoit un ordre d'envoi émis par le PC via ADB.
 *
 * Le numéro et le message transitent encodés en base64 URL-safe : cela évite tout
 * problème d'échappement shell (espaces, apostrophes, accents, retours à la ligne,
 * emojis) lors du passage par « adb shell am broadcast ».
 *
 * Exemple de commande :
 *   adb shell am broadcast \
 *     -n com.smsblast.relay/.SendSmsReceiver \
 *     -a com.smsblast.relay.action.SEND \
 *     -f 0x00000020 \
 *     --es id 42 --es num_b64 KzMzNjEyMzQ1Njc4 --es msg_b64 Qm9uam91cg
 *
 * Le résultat immédiat (acceptation ou refus) revient dans le « data= » affiché par
 * am broadcast. L'accusé d'émission réel arrive ensuite dans logcat, tag SMSRELAY.
 */
public class SendSmsReceiver extends BroadcastReceiver {

    public static final String TAG = "SMSRELAY";
    public static final String ACTION_STATUS = "com.smsblast.relay.action.STATUS";

    private static final AtomicInteger REQUEST_CODE = new AtomicInteger(1000);

    @Override
    public void onReceive(Context context, Intent intent) {
        String id = intent.getStringExtra("id");
        if (id == null) {
            id = "-";
        }

        try {
            String number = decode(intent.getStringExtra("num_b64"));
            String message = decode(intent.getStringExtra("msg_b64"));

            if (isBlank(number)) {
                reply(id, 2, "ERROR numero_vide");
                return;
            }
            if (isBlank(message)) {
                reply(id, 2, "ERROR message_vide");
                return;
            }

            if (context.checkSelfPermission(Manifest.permission.SEND_SMS)
                    != PackageManager.PERMISSION_GRANTED) {
                reply(id, 3, "ERROR permission_SEND_SMS_absente "
                        + "(adb shell pm grant com.smsblast.relay android.permission.SEND_SMS)");
                return;
            }

            int subscriptionId = intent.getIntExtra("sub", -1);
            SmsManager sms = subscriptionId >= 0
                    ? SmsManager.getSmsManagerForSubscriptionId(subscriptionId)
                    : SmsManager.getDefault();

            ArrayList<String> parts = sms.divideMessage(message);
            ArrayList<PendingIntent> sentIntents = new ArrayList<PendingIntent>(parts.size());

            for (int i = 0; i < parts.size(); i++) {
                Intent status = new Intent(context, SmsStatusReceiver.class)
                        .setAction(ACTION_STATUS)
                        .putExtra("id", id)
                        .putExtra("part", i + 1)
                        .putExtra("total", parts.size());

                sentIntents.add(PendingIntent.getBroadcast(
                        context,
                        REQUEST_CODE.incrementAndGet(),
                        status,
                        PendingIntent.FLAG_UPDATE_CURRENT | PendingIntent.FLAG_IMMUTABLE));
            }

            sms.sendMultipartTextMessage(number, null, parts, sentIntents, null);
            reply(id, 0, "QUEUED parts=" + parts.size());
        } catch (Throwable t) {
            Log.e(TAG, "id=" + id + " echec", t);
            reply(id, 4, "ERROR " + t.getClass().getSimpleName() + ": " + t.getMessage());
        }
    }

    private void reply(String id, int code, String data) {
        Log.i(TAG, "ACK id=" + id + " " + data);
        try {
            // am broadcast se comporte comme un broadcast ordonné : ces valeurs
            // remontent dans la sortie standard de la commande côté PC.
            setResultCode(code);
            setResultData(data);
        } catch (RuntimeException ignored) {
            // Broadcast non ordonné : seul le journal fera foi.
        }
    }

    private static String decode(String value) throws UnsupportedEncodingException {
        if (value == null || value.length() == 0) {
            return null;
        }
        byte[] bytes = Base64.decode(value, Base64.URL_SAFE | Base64.NO_PADDING | Base64.NO_WRAP);
        return new String(bytes, "UTF-8");
    }

    private static boolean isBlank(String value) {
        return value == null || value.trim().length() == 0;
    }
}
