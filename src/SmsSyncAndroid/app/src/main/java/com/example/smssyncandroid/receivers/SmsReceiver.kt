package com.example.smssyncandroid.receivers

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.provider.Telephony
import android.util.Log
import com.example.smssyncandroid.services.SmsSyncService

class SmsReceiver : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        if (intent.action == Telephony.Sms.Intents.SMS_RECEIVED_ACTION) {
            val messages = Telephony.Sms.Intents.getMessagesFromIntent(intent)
            for (sms in messages) {
                val address = sms.displayOriginatingAddress ?: continue
                val body = sms.displayMessageBody ?: ""
                val date = sms.timestampMillis

                Log.d("SmsReceiver", "SMS reçu de $address : $body")

                // Transmettre le SMS au service de synchronisation
                val serviceIntent = Intent(context, SmsSyncService::class.java).apply {
                    action = SmsSyncService.ACTION_NEW_SMS
                    putExtra(SmsSyncService.EXTRA_SMS_ADDRESS, address)
                    putExtra(SmsSyncService.EXTRA_SMS_BODY, body)
                    putExtra(SmsSyncService.EXTRA_SMS_DATE, date)
                }

                try {
                    context.startForegroundService(serviceIntent)
                } catch (e: Exception) {
                    Log.e("SmsReceiver", "Impossible de démarrer le service en premier plan: ${e.message}")
                    try {
                        context.startService(serviceIntent)
                    } catch (ex: Exception) {
                        Log.e("SmsReceiver", "Échec total du démarrage du service: ${ex.message}")
                    }
                }
            }
        }
    }
}
