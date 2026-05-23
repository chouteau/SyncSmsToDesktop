package com.example.smssyncandroid.ui.main

import android.app.Application
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.os.Build
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.lifecycle.AndroidViewModel
import com.example.smssyncandroid.services.SmsSyncService

class MainScreenViewModel(application: Application) : AndroidViewModel(application) {

    var ipAddress by mutableStateOf("")
        private set

    var isConnected by mutableStateOf(false)
        private set

    var statusMessage by mutableStateOf("Service arrêté. Entrez l'IP du PC.")
        private set

    var isNotificationAccessGranted by mutableStateOf(false)
        private set

    private val sharedPrefs = application.getSharedPreferences("SmsSyncPrefs", Context.MODE_PRIVATE)

    private val connectionStatusReceiver = object : BroadcastReceiver() {
        override fun onReceive(context: Context?, intent: Intent?) {
            intent?.let {
                isConnected = it.getBooleanExtra("extra_connected", false)
                statusMessage = it.getStringExtra("extra_status") ?: ""
            }
        }
    }

    init {
        ipAddress = sharedPrefs.getString("server_ip", "") ?: ""
        checkNotificationAccess()
        
        val filter = IntentFilter("com.example.smssyncandroid.CONNECTION_STATUS")
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            application.registerReceiver(connectionStatusReceiver, filter, Context.RECEIVER_EXPORTED)
        } else {
            @Suppress("UnspecifiedRegisterReceiverFlag")
            application.registerReceiver(connectionStatusReceiver, filter)
        }
    }

    fun checkNotificationAccess() {
        val context = getApplication<Application>()
        val pkgName = context.packageName
        val flat = android.provider.Settings.Secure.getString(context.contentResolver, "enabled_notification_listeners")
        isNotificationAccessGranted = if (!flat.isNullOrEmpty()) {
            flat.split(":").any {
                val cn = android.content.ComponentName.unflattenFromString(it)
                cn != null && cn.packageName == pkgName
            }
        } else {
            false
        }
    }

    fun updateIpAddress(newIp: String) {
        ipAddress = newIp
        sharedPrefs.edit().putString("server_ip", newIp).apply()
    }

    fun startSyncService() {
        if (ipAddress.isBlank()) {
            statusMessage = "Erreur: L'adresse IP ne peut pas être vide."
            return
        }

        val intent = Intent(getApplication(), SmsSyncService::class.java).apply {
            action = SmsSyncService.ACTION_START
            putExtra(SmsSyncService.EXTRA_SERVER_IP, ipAddress)
        }
        
        try {
            getApplication<Application>().startForegroundService(intent)
            statusMessage = "Démarrage du service..."
        } catch (e: Exception) {
            statusMessage = "Erreur de démarrage : ${e.message}"
        }
    }

    fun stopSyncService() {
        val intent = Intent(getApplication(), SmsSyncService::class.java).apply {
            action = SmsSyncService.ACTION_STOP
        }
        getApplication<Application>().startService(intent)
        isConnected = false
        statusMessage = "Service arrêté."
    }

    override fun onCleared() {
        super.onCleared()
        try {
            getApplication<Application>().unregisterReceiver(connectionStatusReceiver)
        } catch (e: Exception) {
            // Déjà désenregistré
        }
    }
}
