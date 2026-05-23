package com.example.smssyncandroid.models

import kotlinx.serialization.Serializable

@Serializable
data class WebSocketMessage(
    val Type: String,
    val Payload: String
)

@Serializable
data class SmsMessage(
    val Id: String,
    val Address: String,
    val ContactName: String?,
    val Body: String,
    val DateTimestamp: Long,
    val Type: Int, // 1 = INBOX, 2 = SENT
    val IsSynced: Boolean,
    val AttachmentBase64: String? = null,
    val AttachmentMimeType: String? = null
)

@Serializable
data class SendSmsPayload(
    val Address: String,
    val Body: String,
    val RequestId: String
)

@Serializable
data class SendSmsStatusPayload(
    val RequestId: String,
    val Success: Boolean,
    val ErrorMessage: String?
)

@Serializable
data class FavoriteContact(
    val Name: String,
    val Number: String
)
