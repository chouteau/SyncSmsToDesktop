using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading.Tasks;
using System.Security.Cryptography;
using SmsSyncWindows.Models;
using SmsSyncWindows.Data;

namespace SmsSyncWindows.Services
{
    public class WebSocketServerManager
    {
        private TcpListener _listener;
        private System.Net.WebSockets.WebSocket _activeClient;
        private System.Threading.CancellationTokenSource _cts;
        private string _connectedClientIp;
        private readonly int _port = 8888; // Port d'écoute pour la synchro

        public event EventHandler<string> StatusChanged;
        public event EventHandler<SmsMessage> NewSmsReceived;
        public event EventHandler<List<SmsMessage>> HistoryReceived;
        public event EventHandler<SendSmsStatusPayload> SendSmsStatusReceived;
        public event EventHandler<List<FavoriteContact>> FavoritesReceived;
        public event EventHandler<int> SyncStarted;
        public event EventHandler<SyncProgressPayload> SyncProgressChanged;
        public event EventHandler SyncCompleted;

        public bool IsClientConnected => _activeClient != null && _activeClient.State == WebSocketState.Open;
        public string ConnectedClientIp => _connectedClientIp;
        public string ServerIpAddress { get; private set; }

        public WebSocketServerManager()
        {
            ServerIpAddress = GetLocalIPAddress();
        }

        public void Start()
        {
            try
            {
                _cts = new System.Threading.CancellationTokenSource();
                _listener = new TcpListener(IPAddress.Any, _port);
                
                // Autoriser la réutilisation de l'adresse pour éviter le blocage après un redémarrage rapide
                _listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                
                _listener.Start();
                
                StatusChanged?.Invoke(this, $"Serveur démarré sur ws://{ServerIpAddress}:{_port}. En attente du téléphone...");
                
                _ = ListenForClientsAsync(_cts.Token);
            }
            catch (Exception ex)
            {
                Log($"Erreur de démarrage du serveur: {ex}");
                StatusChanged?.Invoke(this, $"Erreur de démarrage du serveur : {ex.Message}");
            }
        }

        public void Stop()
        {
            try
            {
                _cts?.Cancel();
                _listener?.Stop();
                
                if (_activeClient != null)
                {
                    try
                    {
                        // Ne pas bloquer indéfiniment sur la fermeture
                        _activeClient.CloseAsync(WebSocketCloseStatus.NormalClosure, "Stopping server", System.Threading.CancellationToken.None).Wait(1000);
                    }
                    catch {}
                    finally
                    {
                        _activeClient.Dispose();
                        _activeClient = null;
                    }
                }
            }
            catch {}
            finally
            {
                _connectedClientIp = null;
                StatusChanged?.Invoke(this, "Serveur arrêté.");
            }
        }

        private async Task ListenForClientsAsync(System.Threading.CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    TcpClient client = await _listener.AcceptTcpClientAsync(token);
                    
                    // Déconnecter le client précédent si un nouveau arrive
                    if (_activeClient != null)
                    {
                        try
                        {
                            await _activeClient.CloseAsync(WebSocketCloseStatus.NormalClosure, "New client connecting", System.Threading.CancellationToken.None);
                        }
                        catch {}
                    }
                    
                    _ = HandleClientAsync(client, token);
                }
            }
            catch
            {
                // Arrêt normal ou cancellation
            }
        }

        private async Task HandleClientAsync(TcpClient client, System.Threading.CancellationToken token)
        {
            string clientIp = client.Client.RemoteEndPoint?.ToString() ?? "Inconnu";
            NetworkStream stream = client.GetStream();
            
            try
            {
                string secKey = await ReadHandshakeKeyAsync(stream, token);
                if (secKey == null)
                {
                    client.Close();
                    return;
                }
                
                string acceptKey = ComputeWebSocketAcceptKey(secKey);
                await SendHandshakeResponseAsync(stream, acceptKey, token);
                
                using (var webSocket = System.Net.WebSockets.WebSocket.CreateFromStream(stream, isServer: true, subProtocol: null, keepAliveInterval: TimeSpan.FromSeconds(30)))
                {
                    _activeClient = webSocket;
                    _connectedClientIp = clientIp;
                    
                    // Notifier le ViewModel de la connexion sur le thread UI
                    StatusChanged?.Invoke(this, $"Téléphone connecté depuis : {clientIp}");
                    
                    // Demander automatiquement une synchronisation de l'historique lors de la connexion
                    RequestSync();
                    
                    byte[] buffer = new byte[65536];
                    while (webSocket.State == WebSocketState.Open && !token.IsCancellationRequested)
                    {
                        var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", System.Threading.CancellationToken.None);
                            break;
                        }
                        else if (result.MessageType == WebSocketMessageType.Text)
                        {
                            var messageBuilder = new System.Text.StringBuilder(System.Text.Encoding.UTF8.GetString(buffer, 0, result.Count));
                            while (!result.EndOfMessage)
                            {
                                result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                                messageBuilder.Append(System.Text.Encoding.UTF8.GetString(buffer, 0, result.Count));
                            }
                            
                            OnMessageReceived(messageBuilder.ToString());
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Exception dans HandleClientAsync: {ex}");
            }
            finally
            {
                if (_connectedClientIp == clientIp)
                {
                    _activeClient = null;
                    _connectedClientIp = null;
                    StatusChanged?.Invoke(this, "Téléphone déconnecté. En attente de reconnexion...");
                }
                client.Close();
            }
        }

        private async Task<string> ReadHandshakeKeyAsync(NetworkStream stream, System.Threading.CancellationToken token)
        {
            byte[] buffer = new byte[8192];
            int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, token);
            string request = System.Text.Encoding.UTF8.GetString(buffer, 0, bytesRead);
            
            var lines = request.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            foreach (var line in lines)
            {
                if (line.StartsWith("Sec-WebSocket-Key:", StringComparison.OrdinalIgnoreCase))
                {
                    return line.Substring("Sec-WebSocket-Key:".Length).Trim();
                }
            }
            return null;
        }

        private string ComputeWebSocketAcceptKey(string secWebSocketKey)
        {
            const string MagicGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
            string concatenated = secWebSocketKey + MagicGuid;
            byte[] sha1Hash = SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(concatenated));
            return Convert.ToBase64String(sha1Hash);
        }

        private async Task SendHandshakeResponseAsync(NetworkStream stream, string acceptKey, System.Threading.CancellationToken token)
        {
            string response = "HTTP/1.1 101 Switching Protocols\r\n" +
                              "Upgrade: websocket\r\n" +
                              "Connection: Upgrade\r\n" +
                              $"Sec-WebSocket-Accept: {acceptKey}\r\n\r\n";
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(response);
            await stream.WriteAsync(bytes, 0, bytes.Length, token);
        }

        private void OnMessageReceived(string messageJson)
        {
            try
            {
                Log($"Message WebSocket reçu ({messageJson.Length} caractères)");
                var message = JsonSerializer.Deserialize<WebSocketMessage>(messageJson);
                if (message == null)
                {
                    Log("Message désérialisé est null.");
                    return;
                }

                Log($"Type de message: {message.Type}");
                switch (message.Type)
                {
                    case "sms_history":
                        var history = JsonSerializer.Deserialize<List<SmsMessage>>(message.Payload);
                        if (history != null)
                        {
                            Log($"Reçu {history.Count} messages d'historique.");
                            SaveMessagesToDb(history);
                            Log("Historique sauvegardé avec succès.");
                            HistoryReceived?.Invoke(this, history);
                        }
                        else
                        {
                            Log("Historique désérialisé est null.");
                        }
                        break;

                    case "sync_start":
                        var startData = JsonSerializer.Deserialize<SyncStartPayload>(message.Payload);
                        if (startData != null)
                        {
                            Log($"Synchro démarrée. Total attendu : {startData.Total}");
                            SyncStarted?.Invoke(this, startData.Total);
                        }
                        break;

                    case "sync_progress":
                        var progressData = JsonSerializer.Deserialize<SyncProgressPayload>(message.Payload);
                        if (progressData != null)
                        {
                            Log($"Synchro progression : {progressData.Current} / {progressData.Total}");
                            SyncProgressChanged?.Invoke(this, progressData);
                        }
                        break;

                    case "sync_end":
                        Log("Synchro terminée.");
                        SyncCompleted?.Invoke(this, EventArgs.Empty);
                        break;

                    case "new_sms":
                        var newSms = JsonSerializer.Deserialize<SmsMessage>(message.Payload);
                        if (newSms != null)
                        {
                            Log($"Reçu nouveau SMS de {newSms.Address}.");
                            SaveMessagesToDb(new List<SmsMessage> { newSms });
                            Log("Nouveau SMS sauvegardé.");
                            NewSmsReceived?.Invoke(this, newSms);
                        }
                        else
                        {
                            Log("Nouveau SMS désérialisé est null.");
                        }
                        break;

                    case "send_sms_status":
                        var status = JsonSerializer.Deserialize<SendSmsStatusPayload>(message.Payload);
                        if (status != null)
                        {
                            Log($"Reçu statut envoi pour {status.RequestId}: Success={status.Success}.");
                            SendSmsStatusReceived?.Invoke(this, status);
                        }
                        else
                        {
                            Log("Statut envoi désérialisé est null.");
                        }
                        break;

                    case "favorite_contacts":
                        var favorites = JsonSerializer.Deserialize<List<FavoriteContact>>(message.Payload);
                        if (favorites != null)
                        {
                            Log($"Reçu {favorites.Count} contacts favoris.");
                            FavoritesReceived?.Invoke(this, favorites);
                        }
                        else
                        {
                            Log("Contacts favoris désérialisés sont null.");
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                Log($"Erreur de traitement du message WebSocket: {ex}");
            }
        }

        public async Task<bool> SendSmsAsync(string address, string body, string requestId)
        {
            var activeClient = _activeClient;
            if (activeClient == null || activeClient.State != WebSocketState.Open)
            {
                return false;
            }

            var payload = new SendSmsPayload
            {
                Address = address,
                Body = body,
                RequestId = requestId
            };

            var message = new WebSocketMessage
            {
                Type = "send_sms",
                Payload = JsonSerializer.Serialize(payload)
            };

            var json = JsonSerializer.Serialize(message);
            var bytes = System.Text.Encoding.UTF8.GetBytes(json);

            try
            {
                await activeClient.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, System.Threading.CancellationToken.None);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public void RequestSync()
        {
            var activeClient = _activeClient;
            if (activeClient == null || activeClient.State != WebSocketState.Open) return;

            var message = new WebSocketMessage
            {
                Type = "request_sync",
                Payload = ""
            };

            var json = JsonSerializer.Serialize(message);
            var bytes = System.Text.Encoding.UTF8.GetBytes(json);

            _ = Task.Run(async () =>
            {
                try
                {
                    await activeClient.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, System.Threading.CancellationToken.None);
                }
                catch
                {
                    // Ignore
                }
            });
        }

        private void SaveMessagesToDb(List<SmsMessage> messages)
        {
            try
            {
                using (var db = new SmsDbContext())
                {
                    foreach (var msg in messages)
                    {
                        // S'assurer que ContactName n'est pas nul pour éviter les contraintes SQLite NOT NULL héritées
                        if (msg.ContactName == null)
                        {
                            msg.ContactName = string.Empty;
                        }

                        var exists = db.Messages.Any(m => m.Id == msg.Id);
                        if (!exists)
                        {
                            db.Messages.Add(msg);
                        }
                        else
                        {
                            var existing = db.Messages.First(m => m.Id == msg.Id);
                            existing.IsSynced = msg.IsSynced;
                            existing.ContactName = msg.ContactName;
                        }
                    }
                    db.SaveChanges();
                }
            }
            catch (Exception ex)
            {
                Log($"Erreur lors de la sauvegarde en base: {ex}");
                throw;
            }
        }

        private string GetLocalIPAddress()
        {
            try
            {
                var ips = new List<string>();
                var host = Dns.GetHostEntry(Dns.GetHostName());
                foreach (var ip in host.AddressList)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork)
                    {
                        var ipStr = ip.ToString();
                        if (!ipStr.StartsWith("127.") && !ipStr.StartsWith("169.254"))
                        {
                            ips.Add(ipStr);
                        }
                    }
                }
                
                if (ips.Count > 0)
                {
                    // Priorité 1 : adresse IP physique locale type 192.168.x.x
                    var preferred = ips.FirstOrDefault(ip => ip.StartsWith("192.168."));
                    if (preferred != null) return preferred;
                    
                    // Priorité 2 : autre adresse IP physique (ex. 10.x.x.x) ou liste complète
                    return string.Join(" / ", ips);
                }
            }
            catch
            {
                // fallback
            }
            return "127.0.0.1";
        }

        private void Log(string message)
        {
            try
            {
                string logLine = $"[{DateTime.Now:HH:mm:ss}] {message}";
                Console.WriteLine(logLine);
                System.Diagnostics.Debug.WriteLine(logLine);
                File.AppendAllText("E:\\Labs\\WinSms\\crash_log.txt", logLine + Environment.NewLine);
            }
            catch {}
        }
    }
}
