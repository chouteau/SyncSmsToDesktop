package com.example.smssyncandroid.services

import android.app.*
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.content.pm.ServiceInfo
import android.database.Cursor
import android.net.Uri
import android.os.Build
import android.os.IBinder
import android.provider.ContactsContract
import android.provider.Telephony
import android.telephony.SmsManager
import android.util.Log
import androidx.core.app.NotificationCompat
import com.example.smssyncandroid.MainActivity
import com.example.smssyncandroid.models.*
import kotlinx.coroutines.*
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json
import okhttp3.*
import java.math.BigInteger
import java.security.MessageDigest
import java.util.*
import java.util.concurrent.TimeUnit

class SmsSyncService : Service() {

    private val serviceScope = CoroutineScope(Dispatchers.Default + Job())
    private var webSocket: WebSocket? = null
    private val client = OkHttpClient.Builder()
        .readTimeout(0, TimeUnit.MILLISECONDS)
        .build()
        
    private var serverUrl: String = ""
    private var isServiceRunning = false
    private var isConnected = false

    companion object {
        const val CHANNEL_ID = "SmsSyncChannel"
        const val NOTIFICATION_ID = 101

        const val ACTION_START = "com.example.smssyncandroid.ACTION_START"
        const val ACTION_STOP = "com.example.smssyncandroid.ACTION_STOP"
        const val ACTION_NEW_SMS = "com.example.smssyncandroid.ACTION_NEW_SMS"

        const val EXTRA_SERVER_IP = "com.example.smssyncandroid.EXTRA_SERVER_IP"
        const val EXTRA_SMS_ADDRESS = "com.example.smssyncandroid.EXTRA_SMS_ADDRESS"
        const val EXTRA_SMS_BODY = "com.example.smssyncandroid.EXTRA_SMS_BODY"
        const val EXTRA_SMS_DATE = "com.example.smssyncandroid.EXTRA_SMS_DATE"
    }

    override fun onCreate() {
        super.onCreate()
        createNotificationChannel()
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        if (intent == null) return START_STICKY

        when (intent.action) {
            ACTION_START -> {
                val ip = intent.getStringExtra(EXTRA_SERVER_IP) ?: ""
                if (ip.isNotEmpty()) {
                    val cleanedIp = ip.removePrefix("ws://").removePrefix("wss://")
                    val formattedIp = if (!cleanedIp.contains(":")) {
                        "${ip.trimEnd('/')}:8888"
                    } else {
                        ip
                    }
                    serverUrl = if (formattedIp.startsWith("ws://")) formattedIp else "ws://$formattedIp"
                    startForegroundServiceCompat()
                    connectWebSocket()
                }
            }
            ACTION_STOP -> {
                stopWebSocket()
                stopSelf()
            }
            ACTION_NEW_SMS -> {
                val address = intent.getStringExtra(EXTRA_SMS_ADDRESS) ?: ""
                val body = intent.getStringExtra(EXTRA_SMS_BODY) ?: ""
                val date = intent.getLongExtra(EXTRA_SMS_DATE, System.currentTimeMillis())

                if (address.isNotEmpty() && body.isNotEmpty()) {
                    sendNewSmsToWebsocket(address, body, date)
                }
            }
        }

        return START_STICKY
    }

    private fun startForegroundServiceCompat() {
        val notification = createNotification("Connexion au PC en cours...")
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
            startForeground(
                NOTIFICATION_ID, 
                notification, 
                ServiceInfo.FOREGROUND_SERVICE_TYPE_DATA_SYNC
            )
        } else {
            startForeground(NOTIFICATION_ID, notification)
        }
        isServiceRunning = true
    }

    private fun createNotification(contentText: String): Notification {
        val notificationIntent = Intent(this, MainActivity::class.java)
        val pendingIntent = PendingIntent.getActivity(
            this, 0, notificationIntent,
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.M) PendingIntent.FLAG_IMMUTABLE else 0
        )

        return NotificationCompat.Builder(this, CHANNEL_ID)
            .setContentTitle("SMS Sync")
            .setContentText(contentText)
            .setSmallIcon(android.R.drawable.stat_notify_sync)
            .setContentIntent(pendingIntent)
            .setOngoing(true)
            .build()
    }

    private fun updateNotification(contentText: String) {
        val notificationManager = getSystemService(Context.NOTIFICATION_SERVICE) as NotificationManager
        notificationManager.notify(NOTIFICATION_ID, createNotification(contentText))
    }

    private fun connectWebSocket() {
        if (serverUrl.isEmpty()) return

        Log.d("SmsSyncService", "Connexion WebSocket vers $serverUrl")
        updateNotification("Recherche du PC à l'adresse $serverUrl...")
        broadcastStatus(false, "Connexion en cours...")

        val request = Request.Builder().url(serverUrl).build()
        webSocket = client.newWebSocket(request, object : WebSocketListener() {
            override fun onOpen(webSocket: WebSocket, response: Response) {
                isConnected = true
                Log.d("SmsSyncService", "WebSocket Connecté !")
                updateNotification("Connecté au PC. Synchronisation active.")
                broadcastStatus(true, "Connecté au PC")
                
                // Envoyer un message de bienvenue
                sendWsMessage("connect", "Android connecté")
            }

            override fun onMessage(webSocket: WebSocket, text: String) {
                handleIncomingMessage(text)
            }

            override fun onClosing(webSocket: WebSocket, code: Int, reason: String) {
                isConnected = false
                Log.d("SmsSyncService", "WebSocket fermeture en cours...")
            }

            override fun onFailure(webSocket: WebSocket, t: Throwable, response: Response?) {
                isConnected = false
                Log.e("SmsSyncService", "Échec WebSocket: ${t.message}")
                updateNotification("Déconnecté. Tentative de reconnexion...")
                broadcastStatus(false, "Déconnecté. Connexion échouée : ${t.message}")
                
                // Reconnexion automatique après 5 secondes
                serviceScope.launch {
                    delay(5000)
                    if (isServiceRunning && !isConnected) {
                        connectWebSocket()
                    }
                }
            }

            override fun onClosed(webSocket: WebSocket, code: Int, reason: String) {
                isConnected = false
                Log.d("SmsSyncService", "WebSocket fermé. Tentative de reconnexion...")
                broadcastStatus(false, "Déconnecté. Connexion fermée par l'hôte.")
                
                // Reconnexion automatique après 5 secondes
                serviceScope.launch {
                    delay(5000)
                    if (isServiceRunning && !isConnected) {
                        connectWebSocket()
                    }
                }
            }
        })
    }

    private fun stopWebSocket() {
        webSocket?.close(1000, "Service arrêté")
        webSocket = null
        isConnected = false
        isServiceRunning = false
        broadcastStatus(false, "Service arrêté")
    }

    private fun handleIncomingMessage(text: String) {
        try {
            val message = Json.decodeFromString<WebSocketMessage>(text)
            when (message.Type) {
                "request_sync" -> {
                    serviceScope.launch {
                        syncSmsHistory()
                        syncFavoriteContacts()
                    }
                }
                "send_sms" -> {
                    val payload = Json.decodeFromString<SendSmsPayload>(message.Payload)
                    sendSms(payload.Address, payload.Body, payload.RequestId)
                }
            }
        } catch (e: Exception) {
            Log.e("SmsSyncService", "Erreur parsing websocket msg: ${e.message}")
        }
    }
    private suspend fun syncSmsHistory() {
        if (!isConnected) return
        Log.d("SmsSyncService", "Début de la synchronisation de l'historique SMS/MMS")
        
        try {
            val smsList = getSmsHistory()
            val mmsList = getMmsHistory()
            val allMessages = (smsList + mmsList).sortedByDescending { it.DateTimestamp }
            
            val totalCount = allMessages.size
            Log.d("SmsSyncService", "Total messages trouvés (SMS + MMS) : $totalCount")
            
            sendWsMessage("sync_start", """{"Total":$totalCount}""")
            
            var processed = 0
            var currentChunk = mutableListOf<SmsMessage>()
            
            for (msg in allMessages) {
                // If the message has an image attachment, flush current text chunk first,
                // then load the image bytes and send the message individually.
                if (msg.AttachmentMimeType != null) {
                    if (currentChunk.isNotEmpty()) {
                        sendWsMessage("sms_history", Json.encodeToString(currentChunk))
                        currentChunk = mutableListOf()
                        delay(50)
                    }
                    
                    val mmsId = msg.Id.removePrefix("mms_")
                    val bytes = getMmsImageBytes(this, mmsId)
                    val base64 = bytes?.let {
                        android.util.Base64.encodeToString(it, android.util.Base64.NO_WRAP)
                    }
                    
                    val msgWithImage = msg.copy(AttachmentBase64 = base64)
                    processed++
                    
                    sendWsMessage("sms_history", Json.encodeToString(listOf(msgWithImage)))
                    sendWsMessage("sync_progress", """{"Current":$processed,"Total":$totalCount}""")
                    delay(50)
                } else {
                    currentChunk.add(msg)
                    processed++
                    
                    if (currentChunk.size >= 200) {
                        sendWsMessage("sms_history", Json.encodeToString(currentChunk))
                        sendWsMessage("sync_progress", """{"Current":$processed,"Total":$totalCount}""")
                        currentChunk = mutableListOf()
                        delay(50)
                    }
                }
            }
            
            if (currentChunk.isNotEmpty()) {
                sendWsMessage("sms_history", Json.encodeToString(currentChunk))
            }
            sendWsMessage("sync_progress", """{"Current":$totalCount,"Total":$totalCount}""")
            sendWsMessage("sync_end", "")
            
            Log.d("SmsSyncService", "Synchronisation de l'historique terminée.")
        } catch (e: Exception) {
            Log.e("SmsSyncService", "Erreur lors de la synchronisation: ${e.message}")
        }
    }

    private fun getSmsHistory(): List<SmsMessage> {
        val list = mutableListOf<SmsMessage>()
        val uri = Uri.parse("content://sms/")
        val projection = arrayOf("_id", "address", "body", "date", "type")
        val contactCache = mutableMapOf<String, String?>()
        var cursor: Cursor? = null
        try {
            cursor = contentResolver.query(
                uri, projection, null, null, "date DESC LIMIT 40000"
            )
            cursor?.let {
                val idIndex = it.getColumnIndexOrThrow("_id")
                val addressIndex = it.getColumnIndexOrThrow("address")
                val bodyIndex = it.getColumnIndexOrThrow("body")
                val dateIndex = it.getColumnIndexOrThrow("date")
                val typeIndex = it.getColumnIndexOrThrow("type")
                
                while (it.moveToNext()) {
                    val id = it.getString(idIndex) ?: UUID.randomUUID().toString()
                    val address = it.getString(addressIndex) ?: ""
                    val body = it.getString(bodyIndex) ?: ""
                    val date = it.getLong(dateIndex)
                    val type = it.getInt(typeIndex)
                    
                    if (address.isNotEmpty()) {
                        val contactName = contactCache.getOrPut(address) {
                            getContactName(this@SmsSyncService, address)
                        }
                        list.add(
                            SmsMessage(
                                Id = id,
                                Address = address,
                                ContactName = contactName,
                                Body = body,
                                DateTimestamp = date,
                                Type = type,
                                IsSynced = true
                            )
                        )
                    }
                }
            }
        } catch (e: Exception) {
            Log.e("SmsSyncService", "Erreur lecture SMS: ${e.message}")
        } finally {
            cursor?.close()
        }
        return list
    }

    private fun getMmsHistory(): List<SmsMessage> {
        val list = mutableListOf<SmsMessage>()
        val uri = Uri.parse("content://mms/")
        val projection = arrayOf("_id", "date", "msg_box")
        val contactCache = mutableMapOf<String, String?>()
        var cursor: Cursor? = null
        try {
            cursor = contentResolver.query(
                uri, projection, null, null, "date DESC LIMIT 10000"
            )
            cursor?.let {
                val idIndex = it.getColumnIndexOrThrow("_id")
                val dateIndex = it.getColumnIndexOrThrow("date")
                val msgBoxIndex = it.getColumnIndexOrThrow("msg_box")
                
                while (it.moveToNext()) {
                    val mmsId = it.getString(idIndex) ?: continue
                    val dateSec = it.getLong(dateIndex)
                    val dateMs = dateSec * 1000
                    val msgBox = it.getInt(msgBoxIndex)
                    
                    val address = getMmsAddress(this, mmsId, msgBox)
                    if (address.isEmpty()) continue
                    
                    val body = getMmsText(this, mmsId)
                    val mimeType = getMmsImageMimeType(this, mmsId)
                    
                    val contactName = contactCache.getOrPut(address) {
                        getContactName(this, address)
                    }
                    
                    list.add(
                        SmsMessage(
                            Id = "mms_$mmsId",
                            Address = address,
                            ContactName = contactName,
                            Body = body.ifEmpty { "Photo" },
                            DateTimestamp = dateMs,
                            Type = msgBox,
                            IsSynced = true,
                            AttachmentBase64 = null,
                            AttachmentMimeType = mimeType
                        )
                    )
                }
            }
        } catch (e: Exception) {
            Log.e("SmsSyncService", "Erreur lecture MMS: ${e.message}")
        } finally {
            cursor?.close()
        }
        return list
    }

    private fun getMmsAddress(context: Context, mmsId: String, msgBox: Int): String {
        val targetType = if (msgBox == 1) "137" else "151"
        val uri = Uri.parse("content://mms/$mmsId/addr")
        val cursor = context.contentResolver.query(
            uri, arrayOf("address"), "type = ?", arrayOf(targetType), null
        )
        cursor?.use {
            if (it.moveToFirst()) {
                val addr = it.getString(0)
                if (addr != null && addr != "insert-address-token") {
                    return addr
                }
            }
        }
        
        // Fallback to the other type if the preferred is not found
        val fallbackType = if (msgBox == 1) "151" else "137"
        val cursorFallback = context.contentResolver.query(
            uri, arrayOf("address"), "type = ?", arrayOf(fallbackType), null
        )
        cursorFallback?.use {
            if (it.moveToFirst()) {
                val addr = it.getString(0)
                if (addr != null && addr != "insert-address-token") {
                    return addr
                }
            }
        }
        return ""
    }

    private fun getMmsText(context: Context, mmsId: String): String {
        val uri = Uri.parse("content://mms/part")
        val cursor = context.contentResolver.query(
            uri, arrayOf("text"), "mid = ? AND ct = 'text/plain'", arrayOf(mmsId), null
        )
        cursor?.use {
            if (it.moveToFirst()) {
                return it.getString(0) ?: ""
            }
        }
        return ""
    }

    private fun getMmsImageMimeType(context: Context, mmsId: String): String? {
        val uri = Uri.parse("content://mms/part")
        val cursor = context.contentResolver.query(
            uri, arrayOf("ct"), "mid = ? AND ct LIKE 'image/%'", arrayOf(mmsId), null
        )
        cursor?.use {
            if (it.moveToFirst()) {
                return it.getString(0)
            }
        }
        return null
    }

    private fun getMmsImageBytes(context: Context, mmsId: String): ByteArray? {
        val uri = Uri.parse("content://mms/part")
        val cursor = context.contentResolver.query(
            uri, arrayOf("_id"), "mid = ? AND ct LIKE 'image/%'", arrayOf(mmsId), null
        )
        cursor?.use {
            if (it.moveToFirst()) {
                val partId = it.getString(0)
                val partUri = Uri.parse("content://mms/part/$partId")
                try {
                    context.contentResolver.openInputStream(partUri)?.use { inputStream ->
                        return inputStream.readBytes()
                    }
                } catch (e: Exception) {
                    Log.e("SmsSyncService", "Erreur lecture image MMS part $partId: ${e.message}")
                }
            }
        }
        return null
    }

    private fun syncFavoriteContacts() {
        if (!isConnected) return
        Log.d("SmsSyncService", "Début de la synchronisation des contacts favoris")
        val favorites = getFavoriteContacts()
        if (favorites.isNotEmpty()) {
            val jsonPayload = Json.encodeToString(favorites)
            sendWsMessage("favorite_contacts", jsonPayload)
            Log.d("SmsSyncService", "Liste de ${favorites.size} contacts favoris envoyée.")
        }
    }

    private fun getFavoriteContacts(): List<FavoriteContact> {
        val list = mutableListOf<FavoriteContact>()
        val uri = ContactsContract.Contacts.CONTENT_URI
        val projection = arrayOf(
            ContactsContract.Contacts._ID,
            ContactsContract.Contacts.DISPLAY_NAME
        )
        val selection = "${ContactsContract.Contacts.STARRED} = 1"
        var cursor: Cursor? = null
        try {
            cursor = contentResolver.query(uri, projection, selection, null, null)
            cursor?.let {
                val idIndex = it.getColumnIndexOrThrow(ContactsContract.Contacts._ID)
                val nameIndex = it.getColumnIndexOrThrow(ContactsContract.Contacts.DISPLAY_NAME)
                
                while (it.moveToNext()) {
                    val id = it.getString(idIndex)
                    val name = it.getString(nameIndex) ?: ""
                    
                    if (id != null && name.isNotEmpty()) {
                        // Récupérer le(s) numéro(s) de téléphone
                        val phoneCursor = contentResolver.query(
                            ContactsContract.CommonDataKinds.Phone.CONTENT_URI,
                            arrayOf(ContactsContract.CommonDataKinds.Phone.NUMBER),
                            "${ContactsContract.CommonDataKinds.Phone.CONTACT_ID} = ?",
                            arrayOf(id),
                            null
                        )
                        phoneCursor?.use { pc ->
                            val numberIndex = pc.getColumnIndexOrThrow(ContactsContract.CommonDataKinds.Phone.NUMBER)
                            while (pc.moveToNext()) {
                                val number = pc.getString(numberIndex) ?: ""
                                if (number.isNotEmpty()) {
                                    list.add(FavoriteContact(name, number))
                                }
                            }
                        }
                    }
                }
            }
        } catch (e: Exception) {
            Log.e("SmsSyncService", "Erreur lecture favoris: ${e.message}")
        } finally {
            cursor?.close()
        }
        return list
    }

    private fun sendNewSmsToWebsocket(address: String, body: String, date: Long) {
        val id = generateMd5("$address:$date:$body")
        val contactName = getContactName(this, address)
        val smsMessage = SmsMessage(
            Id = id,
            Address = address,
            ContactName = contactName,
            Body = body,
            DateTimestamp = date,
            Type = 1, // Reçu
            IsSynced = true
        )
        val jsonPayload = Json.encodeToString(smsMessage)
        sendWsMessage("new_sms", jsonPayload)
    }

    private fun sendSms(address: String, body: String, requestId: String) {
        try {
            val smsManager = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.M) {
                getSystemService(SmsManager::class.java)
            } else {
                @Suppress("DEPRECATION")
                SmsManager.getDefault()
            }

            val sentAction = "com.example.smssyncandroid.SMS_SENT_$requestId"
            val sentIntent = PendingIntent.getBroadcast(
                this, 0, Intent(sentAction),
                if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.M) PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_ONE_SHOT else PendingIntent.FLAG_ONE_SHOT
            )

            // Enregistrer temporairement un récepteur pour vérifier si le message a été envoyé
            val receiver = object : BroadcastReceiver() {
                override fun onReceive(context: Context?, intent: Intent?) {
                    val success = resultCode == Activity.RESULT_OK
                    val errorMsg = if (success) null else "Code erreur SMS: $resultCode"
                    
                    val statusPayload = SendSmsStatusPayload(
                        RequestId = requestId,
                        Success = success,
                        ErrorMessage = errorMsg
                    )
                    
                    sendWsMessage("send_sms_status", Json.encodeToString(statusPayload))
                    
                    // Désenregistrer le récepteur après réception du statut
                    unregisterReceiver(this)
                }
            }

            // Enregistrer le récepteur avec l'action unique
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
                registerReceiver(receiver, IntentFilter(sentAction), RECEIVER_EXPORTED)
            } else {
                @Suppress("UnspecifiedRegisterReceiverFlag")
                registerReceiver(receiver, IntentFilter(sentAction))
            }

            smsManager.sendTextMessage(address, null, body, sentIntent, null)
            Log.d("SmsSyncService", "Envoi SMS en cours vers $address...")
        } catch (e: Exception) {
            Log.e("SmsSyncService", "Erreur envoi SMS: ${e.message}")
            val statusPayload = SendSmsStatusPayload(
                RequestId = requestId,
                Success = false,
                ErrorMessage = e.message ?: "Erreur inconnue"
            )
            sendWsMessage("send_sms_status", Json.encodeToString(statusPayload))
        }
    }

    private fun sendWsMessage(type: String, payload: String) {
        if (!isConnected || webSocket == null) return
        val message = WebSocketMessage(Type = type, Payload = payload)
        val json = Json.encodeToString(message)
        webSocket?.send(json)
    }

    private fun getContactName(context: Context, phoneNumber: String): String? {
        val uri = Uri.withAppendedPath(
            ContactsContract.PhoneLookup.CONTENT_FILTER_URI, 
            Uri.encode(phoneNumber)
        )
        val projection = arrayOf(ContactsContract.PhoneLookup.DISPLAY_NAME)
        var cursor: Cursor? = null
        try {
            cursor = context.contentResolver.query(uri, projection, null, null, null)
            if (cursor != null && cursor.moveToFirst()) {
                return cursor.getString(0)
            }
        } catch (e: Exception) {
            // Pas de permission ou erreur
        } finally {
            cursor?.close()
        }
        return null
    }

    private fun generateMd5(input: String): String {
        val md = MessageDigest.getInstance("MD5")
        return BigInteger(1, md.digest(input.toByteArray())).toString(16).padStart(32, '0')
    }

    private fun createNotificationChannel() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            val serviceChannel = NotificationChannel(
                CHANNEL_ID,
                "Canal SMS Sync Service",
                NotificationManager.IMPORTANCE_LOW
            )
            val manager = getSystemService(NotificationManager::class.java)
            manager.createNotificationChannel(serviceChannel)
        }
    }

    private fun broadcastStatus(connected: Boolean, message: String) {
        val intent = Intent("com.example.smssyncandroid.CONNECTION_STATUS").apply {
            putExtra("extra_connected", connected)
            putExtra("extra_status", message)
        }
        sendBroadcast(intent)
    }

    override fun onBind(intent: Intent?): IBinder? {
        return null
    }

    override fun onDestroy() {
        super.onDestroy()
        stopWebSocket()
        serviceScope.cancel()
    }
}
