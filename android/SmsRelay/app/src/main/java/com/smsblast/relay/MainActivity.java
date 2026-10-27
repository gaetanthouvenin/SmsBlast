package com.smsblast.relay;

import android.Manifest;
import android.app.Activity;
import android.content.pm.PackageInfo;
import android.content.pm.PackageManager;
import android.content.res.ColorStateList;
import android.content.res.Configuration;
import android.graphics.Color;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.graphics.drawable.RippleDrawable;
import android.os.Bundle;
import android.util.TypedValue;
import android.view.Gravity;
import android.view.View;
import android.view.ViewGroup;
import android.view.Window;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.TextView;

/**
 * Écran unique : affiche l'état de la permission SEND_SMS et permet de l'accorder.
 * L'activité sert aussi à sortir l'application de l'état « stopped » après
 * installation, ce qui garantit la réception des broadcasts envoyés depuis le PC.
 *
 * L'interface est construite en code, sans aucune ressource : l'application reste
 * compilable avec n'importe quelle chaîne d'outils Android (voir build-apk.sh).
 * Elle suit le mode sombre du téléphone et reprend la palette de l'application PC.
 */
public class MainActivity extends Activity {

    // ------------------------------------------------------------ Palette

    private boolean dark;

    private int colorBackground;
    private int colorCard;
    private int colorBorder;
    private int colorText;
    private int colorMuted;
    private int colorAccent;
    private int colorOnAccent;
    private int colorSuccess;
    private int colorSuccessSoft;
    private int colorSuccessText;
    private int colorDanger;
    private int colorDangerSoft;
    private int colorDangerText;
    private int colorSubtle;

    private void loadPalette() {
        int night = getResources().getConfiguration().uiMode & Configuration.UI_MODE_NIGHT_MASK;
        dark = night == Configuration.UI_MODE_NIGHT_YES;

        if (dark) {
            colorBackground = 0xFF0F1522;
            colorCard = 0xFF1A2130;
            colorBorder = 0xFF273044;
            colorText = 0xFFE6E9EF;
            colorMuted = 0xFF98A2B3;
            colorAccent = 0xFF3B82F6;
            colorOnAccent = 0xFFFFFFFF;
            colorSuccess = 0xFF4ADE80;
            colorSuccessSoft = 0xFF16382A;
            colorSuccessText = 0xFF86EFAC;
            colorDanger = 0xFFF87171;
            colorDangerSoft = 0xFF4A2024;
            colorDangerText = 0xFFFCA5A5;
            colorSubtle = 0xFF222A3B;
        } else {
            colorBackground = 0xFFF3F5F9;
            colorCard = 0xFFFFFFFF;
            colorBorder = 0xFFE2E6EE;
            colorText = 0xFF1B1F2A;
            colorMuted = 0xFF6B7280;
            colorAccent = 0xFF2563EB;
            colorOnAccent = 0xFFFFFFFF;
            colorSuccess = 0xFF16A34A;
            colorSuccessSoft = 0xFFDCFCE7;
            colorSuccessText = 0xFF166534;
            colorDanger = 0xFFDC2626;
            colorDangerSoft = 0xFFFEE2E2;
            colorDangerText = 0xFF991B1B;
            colorSubtle = 0xFFF7F8FB;
        }
    }

    // ------------------------------------------------------------ Vues

    private LinearLayout statusCard;
    private View statusDot;
    private TextView statusTitle;
    private TextView statusDetail;
    private TextView grantButton;
    private TextView grantedHint;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        loadPalette();
        // Thème système sans barre d'action : le titre est dessiné par l'écran lui-même.
        setTheme(dark
                ? android.R.style.Theme_Material_NoActionBar
                : android.R.style.Theme_Material_Light_NoActionBar);
        super.onCreate(savedInstanceState);

        Window window = getWindow();
        window.setStatusBarColor(colorBackground);
        window.setNavigationBarColor(colorBackground);
        window.getDecorView().setSystemUiVisibility(
                dark ? 0 : View.SYSTEM_UI_FLAG_LIGHT_STATUS_BAR);

        LinearLayout root = new LinearLayout(this);
        root.setOrientation(LinearLayout.VERTICAL);
        root.setBackgroundColor(colorBackground);
        root.setPadding(dp(22), dp(28), dp(22), dp(28));

        root.addView(buildHeader());
        root.addView(buildStatusCard());
        root.addView(buildHowItWorksCard());
        root.addView(buildPrivacyCard());
        root.addView(buildFooter());

        ScrollView scroll = new ScrollView(this);
        scroll.setBackgroundColor(colorBackground);
        scroll.setFillViewport(true);
        scroll.addView(root, new ViewGroup.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));
        setContentView(scroll);
    }

    // --- En-tête : logo, titre, sous-titre -------------------------------

    private View buildHeader() {
        LinearLayout row = new LinearLayout(this);
        row.setOrientation(LinearLayout.HORIZONTAL);
        row.setGravity(Gravity.CENTER_VERTICAL);

        TextView logo = new TextView(this);
        logo.setText("SMS");
        logo.setTextColor(colorOnAccent);
        logo.setTypeface(Typeface.DEFAULT_BOLD);
        logo.setTextSize(TypedValue.COMPLEX_UNIT_SP, 14f);
        logo.setGravity(Gravity.CENTER);
        logo.setBackground(rounded(colorAccent, 0, dp(14)));
        logo.setElevation(dp(2));
        LinearLayout.LayoutParams logoParams = new LinearLayout.LayoutParams(dp(52), dp(52));
        logoParams.rightMargin = dp(14);
        row.addView(logo, logoParams);

        LinearLayout titles = new LinearLayout(this);
        titles.setOrientation(LinearLayout.VERTICAL);

        TextView title = new TextView(this);
        title.setText("SMS Relay");
        title.setTextColor(colorText);
        title.setTypeface(Typeface.DEFAULT_BOLD);
        title.setTextSize(TypedValue.COMPLEX_UNIT_SP, 24f);
        titles.addView(title);

        TextView subtitle = new TextView(this);
        subtitle.setText("Relais d'envoi piloté par SmsBlast, depuis le PC");
        subtitle.setTextColor(colorMuted);
        subtitle.setTextSize(TypedValue.COMPLEX_UNIT_SP, 13.5f);
        titles.addView(subtitle);

        row.addView(titles, new LinearLayout.LayoutParams(
                0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f));
        return row;
    }

    // --- Carte d'état de la permission ----------------------------------

    private View buildStatusCard() {
        statusCard = card();
        LinearLayout.LayoutParams params = cardParams();
        params.topMargin = dp(26);
        statusCard.setLayoutParams(params);

        LinearLayout row = new LinearLayout(this);
        row.setOrientation(LinearLayout.HORIZONTAL);
        row.setGravity(Gravity.CENTER_VERTICAL);

        statusDot = new View(this);
        LinearLayout.LayoutParams dotParams = new LinearLayout.LayoutParams(dp(10), dp(10));
        dotParams.rightMargin = dp(10);
        row.addView(statusDot, dotParams);

        statusTitle = new TextView(this);
        statusTitle.setTypeface(Typeface.DEFAULT_BOLD);
        statusTitle.setTextSize(TypedValue.COMPLEX_UNIT_SP, 16f);
        row.addView(statusTitle);
        statusCard.addView(row);

        statusDetail = new TextView(this);
        statusDetail.setTextSize(TypedValue.COMPLEX_UNIT_SP, 13.5f);
        statusDetail.setLineSpacing(0f, 1.15f);
        LinearLayout.LayoutParams detailParams = wrap();
        detailParams.topMargin = dp(8);
        statusCard.addView(statusDetail, detailParams);

        grantButton = new TextView(this);
        grantButton.setText("Accorder la permission SMS");
        grantButton.setTextColor(colorOnAccent);
        grantButton.setTypeface(Typeface.DEFAULT_BOLD);
        grantButton.setTextSize(TypedValue.COMPLEX_UNIT_SP, 15f);
        grantButton.setGravity(Gravity.CENTER);
        grantButton.setPadding(dp(20), dp(14), dp(20), dp(14));
        grantButton.setBackground(ripple(rounded(colorAccent, 0, dp(12)), 0x33FFFFFF));
        grantButton.setElevation(dp(2));
        grantButton.setClickable(true);
        grantButton.setFocusable(true);
        // Classe anonyme plutôt qu'une lambda : android.jar ne fournit pas
        // java.lang.invoke, dont dépend la compilation des lambdas.
        grantButton.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View view) {
                requestPermissions(new String[] { Manifest.permission.SEND_SMS }, 1);
            }
        });
        LinearLayout.LayoutParams buttonParams = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        buttonParams.topMargin = dp(16);
        statusCard.addView(grantButton, buttonParams);

        grantedHint = new TextView(this);
        grantedHint.setText("Laissez le téléphone branché et déverrouillé pendant une campagne.");
        grantedHint.setTextColor(colorMuted);
        grantedHint.setTextSize(TypedValue.COMPLEX_UNIT_SP, 12.5f);
        LinearLayout.LayoutParams hintParams = wrap();
        hintParams.topMargin = dp(12);
        statusCard.addView(grantedHint, hintParams);

        return statusCard;
    }

    // --- Comment ça marche ----------------------------------------------

    private View buildHowItWorksCard() {
        LinearLayout card = card();
        card.setLayoutParams(cardParams());
        card.addView(sectionTitle("Comment ça marche"));
        card.addView(step(1, "Le PC envoie un ordre par ADB",
                "adb shell am broadcast -n com.smsblast.relay/.SendSmsReceiver …"));
        card.addView(step(2, "Le relais envoie le SMS",
                "Avec la carte SIM par défaut, comme si vous l'aviez tapé vous-même."));
        card.addView(step(3, "L'accusé d'émission remonte au PC",
                "Lu dans logcat (tag SMSRELAY) en fin de campagne."));
        return card;
    }

    private View step(int number, String title, String detail) {
        LinearLayout row = new LinearLayout(this);
        row.setOrientation(LinearLayout.HORIZONTAL);
        LinearLayout.LayoutParams rowParams = wrap();
        rowParams.topMargin = dp(12);
        row.setLayoutParams(rowParams);

        TextView badge = new TextView(this);
        badge.setText(String.valueOf(number));
        badge.setTextColor(colorOnAccent);
        badge.setTypeface(Typeface.DEFAULT_BOLD);
        badge.setTextSize(TypedValue.COMPLEX_UNIT_SP, 12f);
        badge.setGravity(Gravity.CENTER);
        badge.setBackground(rounded(colorAccent, 0, dp(12)));
        LinearLayout.LayoutParams badgeParams = new LinearLayout.LayoutParams(dp(24), dp(24));
        badgeParams.rightMargin = dp(12);
        badgeParams.topMargin = dp(1);
        row.addView(badge, badgeParams);

        LinearLayout texts = new LinearLayout(this);
        texts.setOrientation(LinearLayout.VERTICAL);

        TextView titleView = new TextView(this);
        titleView.setText(title);
        titleView.setTextColor(colorText);
        titleView.setTypeface(Typeface.DEFAULT_BOLD);
        titleView.setTextSize(TypedValue.COMPLEX_UNIT_SP, 14f);
        texts.addView(titleView);

        TextView detailView = new TextView(this);
        detailView.setText(detail);
        detailView.setTextColor(colorMuted);
        detailView.setTextSize(TypedValue.COMPLEX_UNIT_SP, 12.5f);
        if (detail.startsWith("adb ")) {
            detailView.setTypeface(Typeface.MONOSPACE);
            detailView.setTextSize(TypedValue.COMPLEX_UNIT_SP, 11.5f);
        }
        LinearLayout.LayoutParams detailParams = wrap();
        detailParams.topMargin = dp(2);
        texts.addView(detailView, detailParams);

        row.addView(texts, new LinearLayout.LayoutParams(
                0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f));
        return row;
    }

    // --- Confidentialité ------------------------------------------------

    private View buildPrivacyCard() {
        LinearLayout card = card();
        card.setLayoutParams(cardParams());
        card.addView(sectionTitle("Confidentialité"));

        TextView text = new TextView(this);
        text.setText("Aucune interface d'envoi : l'application réagit uniquement aux ordres "
                + "reçus par ADB. Elle n'accède ni au réseau, ni aux contacts, ni aux SMS "
                + "reçus, et n'envoie aucune donnée ailleurs que vers l'opérateur.");
        text.setTextColor(colorMuted);
        text.setTextSize(TypedValue.COMPLEX_UNIT_SP, 13f);
        text.setLineSpacing(0f, 1.2f);
        LinearLayout.LayoutParams params = wrap();
        params.topMargin = dp(8);
        card.addView(text, params);

        card.addView(chip("Permission unique : SEND_SMS"));
        return card;
    }

    private View chip(String label) {
        TextView chip = new TextView(this);
        chip.setText(label);
        chip.setTextColor(colorText);
        chip.setTypeface(Typeface.DEFAULT_BOLD);
        chip.setTextSize(TypedValue.COMPLEX_UNIT_SP, 12f);
        chip.setPadding(dp(12), dp(6), dp(12), dp(6));
        chip.setBackground(rounded(colorSubtle, colorBorder, dp(14)));
        LinearLayout.LayoutParams params = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        params.topMargin = dp(12);
        chip.setLayoutParams(params);
        return chip;
    }

    // --- Pied de page ---------------------------------------------------

    private View buildFooter() {
        LinearLayout footer = new LinearLayout(this);
        footer.setOrientation(LinearLayout.VERTICAL);
        footer.setGravity(Gravity.CENTER_HORIZONTAL);
        LinearLayout.LayoutParams params = wrap();
        params.topMargin = dp(20);
        footer.setLayoutParams(params);

        String version = "1.0";
        try {
            PackageInfo info = getPackageManager().getPackageInfo(getPackageName(), 0);
            if (info.versionName != null) {
                version = info.versionName;
            }
        } catch (PackageManager.NameNotFoundException ignored) {
            // Impossible en pratique : on affiche la version par défaut.
        }

        TextView line = new TextView(this);
        line.setText("com.smsblast.relay · version " + version);
        line.setTextColor(colorMuted);
        line.setTextSize(TypedValue.COMPLEX_UNIT_SP, 12f);
        footer.addView(line);

        TextView check = new TextView(this);
        check.setText("adb shell dumpsys package com.smsblast.relay");
        check.setTextColor(colorMuted);
        check.setTypeface(Typeface.MONOSPACE);
        check.setTextSize(TypedValue.COMPLEX_UNIT_SP, 11f);
        LinearLayout.LayoutParams checkParams = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        checkParams.topMargin = dp(4);
        footer.addView(check, checkParams);

        return footer;
    }

    // ------------------------------------------------------------ État

    @Override
    protected void onResume() {
        super.onResume();
        refresh();
    }

    @Override
    public void onRequestPermissionsResult(int requestCode, String[] permissions, int[] grantResults) {
        super.onRequestPermissionsResult(requestCode, permissions, grantResults);
        refresh();
    }

    private void refresh() {
        boolean granted = checkSelfPermission(Manifest.permission.SEND_SMS)
                == PackageManager.PERMISSION_GRANTED;

        int soft = granted ? colorSuccessSoft : colorDangerSoft;
        int strong = granted ? colorSuccess : colorDanger;
        int text = granted ? colorSuccessText : colorDangerText;

        statusCard.setBackground(rounded(soft, 0, dp(16)));
        statusDot.setBackground(rounded(strong, 0, dp(5)));
        statusTitle.setTextColor(text);
        statusDetail.setTextColor(text);

        statusTitle.setText(granted ? "Prêt à envoyer" : "Permission SMS manquante");
        statusDetail.setText(granted
                ? "La permission SEND_SMS est accordée. Le relais recevra les ordres "
                + "de SmsBlast et enverra les messages avec cette carte SIM."
                : "Sans la permission SEND_SMS, chaque ordre reçu sera refusé. "
                + "Accordez-la ici, ou depuis le PC : "
                + "adb shell pm grant com.smsblast.relay android.permission.SEND_SMS");

        grantButton.setVisibility(granted ? View.GONE : View.VISIBLE);
        grantedHint.setVisibility(granted ? View.VISIBLE : View.GONE);
    }

    // ------------------------------------------------------------ Outils

    private LinearLayout card() {
        LinearLayout card = new LinearLayout(this);
        card.setOrientation(LinearLayout.VERTICAL);
        card.setPadding(dp(18), dp(16), dp(18), dp(18));
        card.setBackground(rounded(colorCard, colorBorder, dp(16)));
        card.setElevation(dark ? 0f : dp(1));
        return card;
    }

    private LinearLayout.LayoutParams cardParams() {
        LinearLayout.LayoutParams params = wrap();
        params.topMargin = dp(14);
        return params;
    }

    private TextView sectionTitle(String text) {
        TextView title = new TextView(this);
        title.setText(text);
        title.setTextColor(colorText);
        title.setTypeface(Typeface.DEFAULT_BOLD);
        title.setTextSize(TypedValue.COMPLEX_UNIT_SP, 15f);
        return title;
    }

    private LinearLayout.LayoutParams wrap() {
        return new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
    }

    private GradientDrawable rounded(int fill, int stroke, int radius) {
        GradientDrawable drawable = new GradientDrawable();
        drawable.setShape(GradientDrawable.RECTANGLE);
        drawable.setColor(fill);
        drawable.setCornerRadius(radius);
        if (stroke != 0) {
            drawable.setStroke(dp(1), stroke);
        }
        return drawable;
    }

    private RippleDrawable ripple(GradientDrawable content, int rippleColor) {
        return new RippleDrawable(ColorStateList.valueOf(rippleColor), content, null);
    }

    private int dp(int value) {
        return (int) (value * getResources().getDisplayMetrics().density);
    }
}
