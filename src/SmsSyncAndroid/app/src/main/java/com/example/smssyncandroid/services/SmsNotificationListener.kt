package com.example.smssyncandroid.services

import android.app.Notification
import android.content.Context
import android.content.Intent
import android.database.Cursor
import android.os.Bundle
import android.os.Parcelable
import android.provider.ContactsContract
import android.service.notification.NotificationListenerService
import android.service.notification.StatusBarNotification
import android.util.Log

class SmsNotificationListener : NotificationListenerService() {

    override fun onNotificationPosted(sbn: StatusBarNotification) {
        val packageName = sbn.packageName
        if (packageName != "com.google.android.apps.messaging") return

        val extras = sbn.notification.extras ?: return
        val title = extras.getString(Notification.EXTRA_TITLE) ?: ""
        val text = extras.getCharSequence(Notification.EXTRA_TEXT)?.toString() ?: ""

        if (title.isEmpty() || text.isEmpty()) return

        Log.d("SmsNotificationListener", "Notification reçue de Google Messages. Expéditeur: $title, Message: $text")

        // Tenter d'extraire le numéro de téléphone depuis la notification (EXTRA_MESSAGING_PERSON)
        var address: String? = null
        if (android.os.Build.VERSION.SDK_INT >= android.os.Build.VERSION_CODES.P) {
            val person = extras.getParcelable<android.app.Person>(Notification.EXTRA_MESSAGING_PERSON)
            person?.uri?.let { uriStr ->
                if (uriStr.startsWith("tel:")) {
                    address = uriStr.removePrefix("tel:")
                }
            }
        }

        // Si non trouvé, chercher le numéro dans les contacts à partir du nom d'affichage
        if (address.isNullOrEmpty()) {
            address = getPhoneNumberFromName(this, title)
        }

        // Si toujours non trouvé, utiliser le titre directement (qui peut être un numéro brut)
        if (address.isNullOrEmpty()) {
            address = title
        }

        // Nettoyer l'adresse (enlever les espaces inutiles)
        val finalAddress = address!!.trim().replace(" ", "")

        if (finalAddress.isNotEmpty()) {
            Log.d("SmsNotificationListener", "Redirection du message RCS de $finalAddress vers SmsSyncService")
            val intent = Intent(this, SmsSyncService::class.java).apply {
                action = SmsSyncService.ACTION_NEW_SMS
                putExtra(SmsSyncService.EXTRA_SMS_ADDRESS, finalAddress)
                putExtra(SmsSyncService.EXTRA_SMS_BODY, text)
                putExtra(SmsSyncService.EXTRA_SMS_DATE, sbn.postTime)
            }
            startService(intent)
        }
    }

    private fun getPhoneNumberFromName(context: Context, contactName: String): String? {
        var number: String? = null
        val uri = ContactsContract.CommonDataKinds.Phone.CONTENT_URI
        val projection = arrayOf(ContactsContract.CommonDataKinds.Phone.NUMBER)
        val selection = "${ContactsContract.CommonDataKinds.Phone.DISPLAY_NAME} = ?"
        val selectionArgs = arrayOf(contactName)
        
        var cursor: Cursor? = null
        try {
            cursor = context.contentResolver.query(uri, projection, selection, selectionArgs, null)
            if (cursor != null && cursor.moveToFirst()) {
                number = cursor.getString(0)
            }
        } catch (e: Exception) {
            Log.e("SmsNotificationListener", "Erreur lors de la recherche du contact par nom: ${e.message}")
        } finally {
            cursor?.close()
        }
        return number
    }
}
