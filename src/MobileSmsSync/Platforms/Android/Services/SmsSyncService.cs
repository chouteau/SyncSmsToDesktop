using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Database;
using Android.OS;
using Android.Provider;
using Android.Telephony;
using Android.Util;
using AndroidX.Core.App;
using MobileSmsSync.Models;
using MobileSmsSync.ViewModels;
using SmsMessage = MobileSmsSync.Models.SmsMessage;

namespace MobileSmsSync.Services
{
    [Service(Name = "com.companyname.mobilesmssync.SmsSyncService", ForegroundServiceType = Android.Content.PM.ForegroundService.TypeDataSync)]
    public class SmsSyncService : Service
    {
        public const string ChannelId = "SmsSyncChannel";
        public const int NotificationId = 101;

        public const string ActionStart = "com.companyname.mobilesmssync.ACTION_START";
        public const string ActionStop = "com.companyname.mobilesmssync.ACTION_STOP";
        public const string ActionNewSms = "com.companyname.mobilesmssync.ACTION_NEW_SMS";

        public const string ExtraServerIp = "com.companyname.mobilesmssync.EXTRA_SERVER_IP";
        public const string ExtraSmsAddress = "com.companyname.mobilesmssync.EXTRA_SMS_ADDRESS";
        public const string ExtraSmsBody = "com.companyname.mobilesmssync.EXTRA_SMS_BODY";
        public const string ExtraSmsDate = "com.companyname.mobilesmssync.EXTRA_SMS_DATE";

        private ClientWebSocket? _webSocket;
        private CancellationTokenSource? _cts;
        private string _serverUrl = string.Empty;
        private bool _isServiceRunning = false;
        private bool _isConnected = false;
        private readonly object _wsLock = new object();

        public override void OnCreate()
        {
            base.OnCreate();
            CreateNotificationChannel();
        }

        public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
        {
            if (intent == null) return StartCommandResult.Sticky;

            switch (intent.Action)
            {
                case ActionStart:
                    string ip = intent.GetStringExtra(ExtraServerIp) ?? string.Empty;
                    if (!string.IsNullOrEmpty(ip))
                    {
                        string cleanedIp = ip.Replace("ws://", "").Replace("wss://", "");
                        string formattedIp = !cleanedIp.Contains(":") ? $"{cleanedIp.TrimEnd('/')}:8888" : cleanedIp;
                        _serverUrl = formattedIp.StartsWith("ws://") ? formattedIp : $"ws://{formattedIp}";
                        
                        StartForegroundServiceCompat();
                        
                        _cts?.Cancel();
                        _cts = new CancellationTokenSource();
                        Task.Run(() => ConnectWebSocketAsync(_serverUrl, _cts.Token));
                    }
                    break;

                case ActionStop:
                    StopWebSocket();
                    StopSelf();
                    break;

                case ActionNewSms:
                    string address = intent.GetStringExtra(ExtraSmsAddress) ?? string.Empty;
                    string body = intent.GetStringExtra(ExtraSmsBody) ?? string.Empty;
                    long date = intent.GetLongExtra(ExtraSmsDate, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

                    if (!string.IsNullOrEmpty(address) && !string.IsNullOrEmpty(body))
                    {
                        Task.Run(() => SendNewSmsToWebsocketAsync(address, body, date));
                    }
                    break;
            }

            return StartCommandResult.Sticky;
        }

        private void StartForegroundServiceCompat()
        {
            var notification = CreateNotification("Connexion au PC en cours...");
            if (Build.VERSION.SdkInt >= BuildVersionCodes.Q)
            {
                StartForeground(NotificationId, notification, Android.Content.PM.ForegroundService.TypeDataSync);
            }
            else
            {
                StartForeground(NotificationId, notification);
            }
            _isServiceRunning = true;
        }

        private Notification CreateNotification(string contentText)
        {
            // Intent to open MainActivity
            var notificationIntent = new Intent(this, typeof(MainActivity));
            var pendingIntent = PendingIntent.GetActivity(
                this, 0, notificationIntent,
                Build.VERSION.SdkInt >= BuildVersionCodes.M ? PendingIntentFlags.Immutable : 0
            );

            return new NotificationCompat.Builder(this, ChannelId)
                .SetContentTitle("SMS Sync")
                .SetContentText(contentText)
                .SetSmallIcon(Android.Resource.Drawable.StatNotifySync)
                .SetContentIntent(pendingIntent)
                .SetOngoing(true)
                .Build();
        }

        private void UpdateNotification(string contentText)
        {
            var notificationManager = (NotificationManager)GetSystemService(NotificationService);
            notificationManager?.Notify(NotificationId, CreateNotification(contentText));
        }

        private async Task ConnectWebSocketAsync(string url, CancellationToken token)
        {
            while (_isServiceRunning && !token.IsCancellationRequested)
            {
                try
                {
                    lock (_wsLock)
                    {
                        _webSocket = new ClientWebSocket();
                    }
                    
                    UpdateNotification($"Recherche du PC à l'adresse {url}...");
                    MainViewModel.UpdateStatus(false, "Connexion en cours...");

                    await _webSocket.ConnectAsync(new Uri(url), token);

                    _isConnected = true;
                    UpdateNotification("Connecté au PC. Synchronisation active.");
                    MainViewModel.UpdateStatus(true, "Connecté au PC");

                    await SendWsMessageAsync("connect", "Android connecté");

                    await ReceiveLoopAsync(_webSocket, token);
                }
                catch (Exception ex)
                {
                    _isConnected = false;
                    UpdateNotification("Déconnecté. Tentative de reconnexion...");
                    MainViewModel.UpdateStatus(false, $"Déconnecté. Connexion échouée : {ex.Message}");
                    
                    lock (_wsLock)
                    {
                        _webSocket?.Dispose();
                        _webSocket = null;
                    }
                }

                if (_isServiceRunning && !token.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(5000, token);
                    }
                    catch (TaskCanceledException)
                    {
                        break;
                    }
                }
            }
        }

        private async Task ReceiveLoopAsync(ClientWebSocket webSocket, CancellationToken token)
        {
            var buffer = new byte[8192];
            var messageBuilder = new StringBuilder();

            while (webSocket.State == WebSocketState.Open && !token.IsCancellationRequested)
            {
                WebSocketReceiveResult result;
                try
                {
                    result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                }
                catch
                {
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    string chunk = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    messageBuilder.Append(chunk);

                    if (result.EndOfMessage)
                    {
                        string completeMessage = messageBuilder.ToString();
                        messageBuilder.Clear();
                        
                        _ = Task.Run(() => HandleIncomingMessageAsync(completeMessage));
                    }
                }
            }
        }

        private void StopWebSocket()
        {
            _isServiceRunning = false;
            _isConnected = false;
            _cts?.Cancel();

            lock (_wsLock)
            {
                if (_webSocket != null)
                {
                    try
                    {
                        if (_webSocket.State == WebSocketState.Open)
                        {
                            _webSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Service stopped", CancellationToken.None).Wait(1000);
                        }
                    }
                    catch { }
                    _webSocket.Dispose();
                    _webSocket = null;
                }
            }
            
            MainViewModel.UpdateStatus(false, "Service arrêté.");
        }

        private async Task HandleIncomingMessageAsync(string text)
        {
            try
            {
                var message = JsonSerializer.Deserialize<WebSocketMessage>(text);
                if (message == null) return;

                switch (message.Type)
                {
                    case "request_sync":
                        await SyncSmsHistoryAsync();
                        await SyncFavoriteContactsAsync();
                        break;

                    case "send_sms":
                        var payload = JsonSerializer.Deserialize<SendSmsPayload>(message.Payload);
                        if (payload != null)
                        {
                            SendSms(payload.Address, payload.Body, payload.RequestId);
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                Log.Error("SmsSyncService", $"Erreur traitement msg WebSocket: {ex.Message}");
            }
        }

        private async Task SyncSmsHistoryAsync()
        {
            if (!_isConnected) return;
            Log.Debug("SmsSyncService", "Début sync historique SMS/MMS");

            try
            {
                var smsList = GetSmsHistory();
                var mmsList = GetMmsHistory();
                var allMessages = smsList.Concat(mmsList).OrderByDescending(m => m.DateTimestamp).ToList();

                int totalCount = allMessages.Count;
                Log.Debug("SmsSyncService", $"Total messages trouvés : {totalCount}");

                await SendWsMessageAsync("sync_start", $"{{\"Total\":{totalCount}}}");

                int processed = 0;
                var currentChunk = new List<SmsMessage>();

                foreach (var msg in allMessages)
                {
                    if (msg.AttachmentMimeType != null)
                    {
                        // Flush previous text chunk
                        if (currentChunk.Count > 0)
                        {
                            await SendWsMessageAsync("sms_history", JsonSerializer.Serialize(currentChunk));
                            currentChunk.Clear();
                            await Task.Delay(50);
                        }

                        string mmsId = msg.Id.Replace("mms_", "");
                        byte[]? bytes = GetMmsImageBytes(mmsId);
                        string? base64 = bytes != null ? Convert.ToBase64String(bytes) : null;
                        
                        msg.AttachmentBase64 = base64;
                        processed++;

                        await SendWsMessageAsync("sms_history", JsonSerializer.Serialize(new List<SmsMessage> { msg }));
                        await SendWsMessageAsync("sync_progress", $"{{\"Current\":{processed},\"Total\":{totalCount}}}");
                        await Task.Delay(50);
                    }
                    else
                    {
                        currentChunk.Add(msg);
                        processed++;

                        if (currentChunk.Count >= 200)
                        {
                            await SendWsMessageAsync("sms_history", JsonSerializer.Serialize(currentChunk));
                            await SendWsMessageAsync("sync_progress", $"{{\"Current\":{processed},\"Total\":{totalCount}}}");
                            currentChunk.Clear();
                            await Task.Delay(50);
                        }
                    }
                }

                if (currentChunk.Count > 0)
                {
                    await SendWsMessageAsync("sms_history", JsonSerializer.Serialize(currentChunk));
                }

                await SendWsMessageAsync("sync_progress", $"{{\"Current\":{totalCount},\"Total\":{totalCount}}}");
                await SendWsMessageAsync("sync_end", "");

                Log.Debug("SmsSyncService", "Sync historique terminée.");
            }
            catch (Exception ex)
            {
                Log.Error("SmsSyncService", $"Erreur sync historique: {ex.Message}");
            }
        }

        private List<SmsMessage> GetSmsHistory()
        {
            var list = new List<SmsMessage>();
            var uri = Android.Net.Uri.Parse("content://sms/");
            string[] projection = { "_id", "address", "body", "date", "type" };
            var contactCache = new Dictionary<string, string?>();

            using var cursor = ContentResolver?.Query(uri, projection, null, null, "date DESC LIMIT 40000");
            if (cursor != null)
            {
                int idIndex = cursor.GetColumnIndexOrThrow("_id");
                int addressIndex = cursor.GetColumnIndexOrThrow("address");
                int bodyIndex = cursor.GetColumnIndexOrThrow("body");
                int dateIndex = cursor.GetColumnIndexOrThrow("date");
                int typeIndex = cursor.GetColumnIndexOrThrow("type");

                while (cursor.MoveToNext())
                {
                    string id = cursor.GetString(idIndex) ?? Guid.NewGuid().ToString();
                    string address = cursor.GetString(addressIndex) ?? string.Empty;
                    string body = cursor.GetString(bodyIndex) ?? string.Empty;
                    long date = cursor.GetLong(dateIndex);
                    int type = cursor.GetInt(typeIndex);

                    if (!string.IsNullOrEmpty(address))
                    {
                        if (!contactCache.TryGetValue(address, out string? contactName))
                        {
                            contactName = GetContactName(address);
                            contactCache[address] = contactName;
                        }

                        list.Add(new SmsMessage
                        {
                            Id = id,
                            Address = address,
                            ContactName = contactName,
                            Body = body,
                            DateTimestamp = date,
                            Type = type,
                            IsSynced = true
                        });
                    }
                }
            }
            return list;
        }

        private List<SmsMessage> GetMmsHistory()
        {
            var list = new List<SmsMessage>();
            var uri = Android.Net.Uri.Parse("content://mms/");
            string[] projection = { "_id", "date", "msg_box" };
            var contactCache = new Dictionary<string, string?>();

            using var cursor = ContentResolver?.Query(uri, projection, null, null, "date DESC LIMIT 10000");
            if (cursor != null)
            {
                int idIndex = cursor.GetColumnIndexOrThrow("_id");
                int dateIndex = cursor.GetColumnIndexOrThrow("date");
                int msgBoxIndex = cursor.GetColumnIndexOrThrow("msg_box");

                while (cursor.MoveToNext())
                {
                    string? mmsId = cursor.GetString(idIndex);
                    if (string.IsNullOrEmpty(mmsId)) continue;

                    long dateSec = cursor.GetLong(dateIndex);
                    long dateMs = dateSec * 1000;
                    int msgBox = cursor.GetInt(msgBoxIndex);

                    string address = GetMmsAddress(mmsId, msgBox);
                    if (string.IsNullOrEmpty(address)) continue;

                    string body = GetMmsText(mmsId);
                    string? mimeType = GetMmsImageMimeType(mmsId);

                    if (!contactCache.TryGetValue(address, out string? contactName))
                    {
                        contactName = GetContactName(address);
                        contactCache[address] = contactName;
                    }

                    list.Add(new SmsMessage
                    {
                        Id = $"mms_{mmsId}",
                        Address = address,
                        ContactName = contactName,
                        Body = string.IsNullOrEmpty(body) ? "Photo" : body,
                        DateTimestamp = dateMs,
                        Type = msgBox,
                        IsSynced = true,
                        AttachmentMimeType = mimeType
                    });
                }
            }
            return list;
        }

        private string GetMmsAddress(string mmsId, int msgBox)
        {
            string targetType = (msgBox == 1) ? "137" : "151";
            var uri = Android.Net.Uri.Parse($"content://mms/{mmsId}/addr");
            
            using var cursor = ContentResolver?.Query(uri, new[] { "address" }, "type = ?", new[] { targetType }, null);
            if (cursor != null && cursor.MoveToFirst())
            {
                string? addr = cursor.GetString(0);
                if (!string.IsNullOrEmpty(addr) && addr != "insert-address-token")
                {
                    return addr;
                }
            }

            // Fallback
            string fallbackType = (msgBox == 1) ? "151" : "137";
            using var cursorFallback = ContentResolver?.Query(uri, new[] { "address" }, "type = ?", new[] { fallbackType }, null);
            if (cursorFallback != null && cursorFallback.MoveToFirst())
            {
                string? addr = cursorFallback.GetString(0);
                if (!string.IsNullOrEmpty(addr) && addr != "insert-address-token")
                {
                    return addr;
                }
            }

            return string.Empty;
        }

        private string GetMmsText(string mmsId)
        {
            var uri = Android.Net.Uri.Parse("content://mms/part");
            using var cursor = ContentResolver?.Query(uri, new[] { "text" }, "mid = ? AND ct = 'text/plain'", new[] { mmsId }, null);
            if (cursor != null && cursor.MoveToFirst())
            {
                return cursor.GetString(0) ?? string.Empty;
            }
            return string.Empty;
        }

        private string? GetMmsImageMimeType(string mmsId)
        {
            var uri = Android.Net.Uri.Parse("content://mms/part");
            using var cursor = ContentResolver?.Query(uri, new[] { "ct" }, "mid = ? AND ct LIKE 'image/%'", new[] { mmsId }, null);
            if (cursor != null && cursor.MoveToFirst())
            {
                return cursor.GetString(0);
            }
            return null;
        }

        private byte[]? GetMmsImageBytes(string mmsId)
        {
            var uri = Android.Net.Uri.Parse("content://mms/part");
            using var cursor = ContentResolver?.Query(uri, new[] { "_id" }, "mid = ? AND ct LIKE 'image/%'", new[] { mmsId }, null);
            if (cursor != null && cursor.MoveToFirst())
            {
                string? partId = cursor.GetString(0);
                if (string.IsNullOrEmpty(partId)) return null;

                var partUri = Android.Net.Uri.Parse($"content://mms/part/{partId}");
                try
                {
                    using var stream = ContentResolver?.OpenInputStream(partUri);
                    if (stream != null)
                    {
                        using var ms = new MemoryStream();
                        stream.CopyTo(ms);
                        return ms.ToArray();
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("SmsSyncService", $"Erreur lecture image MMS part {partId}: {ex.Message}");
                }
            }
            return null;
        }

        private async Task SyncFavoriteContactsAsync()
        {
            if (!_isConnected) return;
            Log.Debug("SmsSyncService", "Début sync contacts favoris");

            var favorites = GetFavoriteContacts();
            if (favorites.Count > 0)
            {
                string json = JsonSerializer.Serialize(favorites);
                await SendWsMessageAsync("favorite_contacts", json);
                Log.Debug("SmsSyncService", $"Liste de {favorites.Count} contacts favoris envoyée.");
            }
        }

        private List<FavoriteContact> GetFavoriteContacts()
        {
            var list = new List<FavoriteContact>();
            var uri = ContactsContract.Contacts.ContentUri;
            string[] projection = { ContactsContract.Contacts.InterfaceConsts.Id, ContactsContract.Contacts.InterfaceConsts.DisplayName };
            string selection = $"{ContactsContract.Contacts.InterfaceConsts.Starred} = 1";

            using var cursor = ContentResolver?.Query(uri, projection, selection, null, null);
            if (cursor != null)
            {
                int idIndex = cursor.GetColumnIndexOrThrow(ContactsContract.Contacts.InterfaceConsts.Id);
                int nameIndex = cursor.GetColumnIndexOrThrow(ContactsContract.Contacts.InterfaceConsts.DisplayName);

                while (cursor.MoveToNext())
                {
                    string? id = cursor.GetString(idIndex);
                    string name = cursor.GetString(nameIndex) ?? string.Empty;

                    if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(name))
                    {
                        using var phoneCursor = ContentResolver?.Query(
                            ContactsContract.CommonDataKinds.Phone.ContentUri,
                            new[] { ContactsContract.CommonDataKinds.Phone.Number },
                            $"{ContactsContract.CommonDataKinds.Phone.InterfaceConsts.ContactId} = ?",
                            new[] { id },
                            null
                        );

                        if (phoneCursor != null)
                        {
                            int numberIndex = phoneCursor.GetColumnIndexOrThrow(ContactsContract.CommonDataKinds.Phone.Number);
                            while (phoneCursor.MoveToNext())
                            {
                                string number = phoneCursor.GetString(numberIndex) ?? string.Empty;
                                if (!string.IsNullOrEmpty(number))
                                {
                                    list.Add(new FavoriteContact { Name = name, Number = number });
                                }
                            }
                        }
                    }
                }
            }
            return list;
        }

        private async Task SendNewSmsToWebsocketAsync(string address, string body, long date)
        {
            string id = GenerateMd5($"{address}:{date}:{body}");
            string? contactName = GetContactName(address);

            var smsMessage = new SmsMessage
            {
                Id = id,
                Address = address,
                ContactName = contactName,
                Body = body,
                DateTimestamp = date,
                Type = 1, // Reçu
                IsSynced = true
            };

            await SendWsMessageAsync("new_sms", JsonSerializer.Serialize(smsMessage));
        }

        private void SendSms(string address, string body, string requestId)
        {
            try
            {
                SmsManager smsManager;
                if (Build.VERSION.SdkInt >= BuildVersionCodes.M)
                {
                    smsManager = (SmsManager)GetSystemService(Java.Lang.Class.FromType(typeof(SmsManager)));
                }
                else
                {
#pragma warning disable CS0618 // Type or member is obsolete
                    smsManager = SmsManager.Default;
#pragma warning restore CS0618
                }

                string sentAction = $"com.companyname.mobilesmssync.SMS_SENT_{requestId}";
                var sentIntent = PendingIntent.GetBroadcast(
                    this, 0, new Intent(sentAction),
                    Build.VERSION.SdkInt >= BuildVersionCodes.M ? PendingIntentFlags.Immutable | PendingIntentFlags.OneShot : PendingIntentFlags.OneShot
                );

                var receiver = new SmsSentReceiver(requestId, (statusPayload) =>
                {
                    _ = SendWsMessageAsync("send_sms_status", JsonSerializer.Serialize(statusPayload));
                });

                if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu)
                {
                    RegisterReceiver(receiver, new IntentFilter(sentAction), ReceiverFlags.Exported);
                }
                else
                {
                    RegisterReceiver(receiver, new IntentFilter(sentAction));
                }

                smsManager.SendTextMessage(address, null, body, sentIntent, null);
                Log.Debug("SmsSyncService", $"Envoi SMS en cours vers {address}...");
            }
            catch (Exception ex)
            {
                Log.Error("SmsSyncService", $"Erreur envoi SMS: {ex.Message}");
                var statusPayload = new SendSmsStatusPayload
                {
                    RequestId = requestId,
                    Success = false,
                    ErrorMessage = ex.Message
                };
                _ = SendWsMessageAsync("send_sms_status", JsonSerializer.Serialize(statusPayload));
            }
        }

        private async Task SendWsMessageAsync(string type, string payload)
        {
            ClientWebSocket? ws;
            lock (_wsLock)
            {
                ws = _webSocket;
            }

            if (!_isConnected || ws == null || ws.State != WebSocketState.Open) return;

            try
            {
                var message = new WebSocketMessage { Type = type, Payload = payload };
                string json = JsonSerializer.Serialize(message);
                byte[] bytes = Encoding.UTF8.GetBytes(json);

                await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
            }
            catch (Exception ex)
            {
                Log.Error("SmsSyncService", $"Erreur envoi msg WebSocket: {ex.Message}");
            }
        }

        private string? GetContactName(string phoneNumber)
        {
            var uri = Android.Net.Uri.WithAppendedPath(ContactsContract.PhoneLookup.ContentFilterUri, Android.Net.Uri.Encode(phoneNumber));
            string[] projection = { ContactsContract.PhoneLookup.InterfaceConsts.DisplayName };
            
            using var cursor = ContentResolver?.Query(uri, projection, null, null, null);
            if (cursor != null && cursor.MoveToFirst())
            {
                return cursor.GetString(0);
            }
            return null;
        }

        private string GenerateMd5(string input)
        {
            using var md5 = MD5.Create();
            byte[] inputBytes = Encoding.UTF8.GetBytes(input);
            byte[] hashBytes = md5.ComputeHash(inputBytes);
            var sb = new StringBuilder();
            for (int i = 0; i < hashBytes.Length; i++)
            {
                sb.Append(hashBytes[i].ToString("x2"));
            }
            return sb.ToString();
        }

        private void CreateNotificationChannel()
        {
            if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
            {
                var serviceChannel = new NotificationChannel(
                    ChannelId,
                    "Canal SMS Sync Service",
                    NotificationImportance.Low
                );
                var manager = (NotificationManager)GetSystemService(NotificationService);
                manager?.CreateNotificationChannel(serviceChannel);
            }
        }

        public override IBinder? OnBind(Intent? intent)
        {
            return null;
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            StopWebSocket();
        }

        private class SmsSentReceiver : BroadcastReceiver
        {
            private readonly string _requestId;
            private readonly Action<SendSmsStatusPayload> _onResult;

            public SmsSentReceiver(string requestId, Action<SendSmsStatusPayload> onResult)
            {
                _requestId = requestId;
                _onResult = onResult;
            }

            public override void OnReceive(Context? context, Intent? intent)
            {
                bool success = ResultCode == Result.Ok;
                string? errorMsg = success ? null : $"Code erreur SMS: {ResultCode}";

                var statusPayload = new SendSmsStatusPayload
                {
                    RequestId = _requestId,
                    Success = success,
                    ErrorMessage = errorMsg
                };

                _onResult(statusPayload);

                try
                {
                    context?.UnregisterReceiver(this);
                }
                catch { }
            }
        }
    }
}
