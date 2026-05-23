using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SmsSyncMaui.Data;
using SmsSyncMaui.Models;
using SmsSyncMaui.Services;

namespace SmsSyncMaui.ViewModels
{
    public class MainViewModel
    {
        private readonly WebSocketServerManager _serverManager;

        public ObservableCollection<Conversation> Conversations { get; } = new();

        private Conversation? _selectedConversation;
        public Conversation? SelectedConversation
        {
            get => _selectedConversation;
            set
            {
                _selectedConversation = value;
                NotifyStateChanged();
            }
        }

        public bool HasSelectedConversation => SelectedConversation != null;

        private string _statusMessage = "Initialisation...";
        public string StatusMessage
        {
            get => _statusMessage;
            private set { _statusMessage = value; NotifyStateChanged(); }
        }

        private bool _isConnected;
        public bool IsConnected
        {
            get => _isConnected;
            private set { _isConnected = value; NotifyStateChanged(); }
        }

        private string _newMessageText = string.Empty;
        public string NewMessageText
        {
            get => _newMessageText;
            set { _newMessageText = value; }
        }

        public string ServerIpPort { get; private set; } = string.Empty;

        private bool _isSyncing;
        public bool IsSyncing
        {
            get => _isSyncing;
            private set { _isSyncing = value; NotifyStateChanged(); }
        }

        private double _syncProgressValue;
        public double SyncProgressValue
        {
            get => _syncProgressValue;
            private set { _syncProgressValue = value; NotifyStateChanged(); }
        }

        private string _syncStatusText = string.Empty;
        public string SyncStatusText
        {
            get => _syncStatusText;
            private set { _syncStatusText = value; NotifyStateChanged(); }
        }

        /// <summary>Raised whenever state changes so Blazor components can call StateHasChanged().</summary>
        public event Action? StateChanged;

        private void NotifyStateChanged() => StateChanged?.Invoke();

        public MainViewModel()
        {
            using (var db = new SmsDbContext())
            {
                db.Database.EnsureCreated();
                try
                {
                    db.Database.ExecuteSqlRaw("ALTER TABLE Messages ADD COLUMN IsDeleted INTEGER NOT NULL DEFAULT 0;");
                }
                catch
                {
                    // La colonne existe déjà
                }
                try
                {
                    db.Database.ExecuteSqlRaw("ALTER TABLE Messages ADD COLUMN AttachmentBase64 TEXT;");
                }
                catch
                {
                    // La colonne existe déjà
                }
                try
                {
                    db.Database.ExecuteSqlRaw("ALTER TABLE Messages ADD COLUMN AttachmentMimeType TEXT;");
                }
                catch
                {
                    // La colonne existe déjà
                }
            }

            _serverManager = new WebSocketServerManager();
            _serverManager.StatusChanged += OnServerStatusChanged;
            _serverManager.NewSmsReceived += OnNewSmsReceived;
            _serverManager.HistoryReceived += OnHistoryReceived;
            _serverManager.SendSmsStatusReceived += OnSendSmsStatusReceived;
            _serverManager.FavoritesReceived += OnFavoritesReceived;
            _serverManager.SyncStarted += OnSyncStarted;
            _serverManager.SyncProgressChanged += OnSyncProgressChanged;
            _serverManager.SyncCompleted += OnSyncCompleted;

            ServerIpPort = $"ws://{_serverManager.ServerIpAddress}:8888";

            LoadConversationsFromDb();
            _serverManager.Start();
        }

        public void Cleanup() => _serverManager.Stop();

        public void RequestSync() => _serverManager.RequestSync();

        // ──────────────────────────────────────────────
        // Event handlers from WebSocketServerManager
        // ──────────────────────────────────────────────

        private void OnServerStatusChanged(object? sender, string status)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                _statusMessage = status;
                _isConnected = _serverManager.IsClientConnected;
                NotifyStateChanged();
            });
        }

        private void OnSyncStarted(object? sender, int total)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                _isSyncing = true;
                _syncProgressValue = 0;
                _syncStatusText = $"Démarrage de la synchronisation... (0/{total})";
                NotifyStateChanged();
            });
        }

        private void OnSyncProgressChanged(object? sender, SyncProgressPayload progress)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                _isSyncing = true;
                double percentage = progress.Total > 0 ? ((double)progress.Current / progress.Total) * 100 : 0;
                _syncProgressValue = percentage;
                _syncStatusText = $"Synchronisation : {progress.Current} / {progress.Total} messages";
                NotifyStateChanged();
            });
        }

        private void OnSyncCompleted(object? sender, EventArgs e)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                _isSyncing = false;
                _syncProgressValue = 100;
                _syncStatusText = "Synchronisation terminée";
                NotifyStateChanged();
            });
        }

        private void OnNewSmsReceived(object? sender, SmsEntry sms)
        {
            MainThread.BeginInvokeOnMainThread(() => AddMessageToUI(sms));
        }

        private void OnHistoryReceived(object? sender, List<SmsEntry> messages)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                List<string> deletedIds;
                using (var db = new SmsDbContext())
                {
                    var incomingIds = messages.Select(m => m.Id).ToList();
                    deletedIds = db.Messages
                        .Where(m => incomingIds.Contains(m.Id) && m.IsDeleted)
                        .Select(m => m.Id)
                        .ToList();
                }

                var filteredMessages = messages.Where(m => !deletedIds.Contains(m.Id)).ToList();
                if (!filteredMessages.Any()) return;

                var selectedAddress = SelectedConversation?.Address;
                var groups = filteredMessages.GroupBy(m => NormalizePhoneNumber(m.Address));

                foreach (var group in groups)
                {
                    var conversation = Conversations.FirstOrDefault(c => c.Address != null && NormalizePhoneNumber(c.Address) == group.Key);
                    bool isNew = false;
                    if (conversation == null)
                    {
                        var latestMsg = group.OrderBy(m => m.DateTimestamp).Last();
                        conversation = new Conversation
                        {
                            Address = latestMsg.Address,
                            ContactName = latestMsg.ContactName,
                            LatestMessage = latestMsg.Body,
                            LatestMessageTime = latestMsg.LocalDateTime
                        };
                        isNew = true;
                    }

                    foreach (var sms in group.OrderBy(m => m.DateTimestamp))
                    {
                        if (!conversation.Messages.Any(m => m.Id == sms.Id))
                        {
                            int insertIdx = 0;
                            while (insertIdx < conversation.Messages.Count && conversation.Messages[insertIdx].DateTimestamp < sms.DateTimestamp)
                            {
                                insertIdx++;
                            }
                            conversation.Messages.Insert(insertIdx, sms);

                            if (sms.LocalDateTime > conversation.LatestMessageTime)
                            {
                                conversation.LatestMessage = sms.Body;
                                conversation.LatestMessageTime = sms.LocalDateTime;
                                conversation.ContactName = sms.ContactName;
                            }
                        }
                    }

                    if (isNew)
                    {
                        Conversations.Add(conversation);
                    }
                }

                SortConversations(selectedAddress);
            });
        }

        private void OnSendSmsStatusReceived(object? sender, SendSmsStatusPayload status)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                using (var db = new SmsDbContext())
                {
                    var msg = db.Messages.FirstOrDefault(m => m.Id == status.RequestId);
                    if (msg != null)
                    {
                        msg.IsSynced = status.Success;
                        db.SaveChanges();
                    }
                }

                if (SelectedConversation != null)
                {
                    var activeMsg = SelectedConversation.Messages.FirstOrDefault(m => m.Id == status.RequestId);
                    if (activeMsg != null)
                    {
                        activeMsg.IsSynced = status.Success;
                    }
                }

                if (!status.Success)
                {
                    _statusMessage = $"Échec de l'envoi du SMS : {status.ErrorMessage}";
                }

                NotifyStateChanged();
            });
        }

        private void OnFavoritesReceived(object? sender, List<FavoriteContact> favorites)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                var selectedAddress = SelectedConversation?.Address;

                foreach (var fav in favorites)
                {
                    if (string.IsNullOrWhiteSpace(fav.Number))
                    {
                        continue;
                    }

                    var normalizedFavNumber = NormalizePhoneNumber(fav.Number);
                    var conversation = Conversations.FirstOrDefault(c => NormalizePhoneNumber(c.Address) == normalizedFavNumber);

                    if (conversation != null)
                    {
                        conversation.IsFavorite = true;
                        if (!string.IsNullOrEmpty(fav.Name))
                        {
                            conversation.ContactName = fav.Name;
                        }
                    }
                    else
                    {
                        conversation = new Conversation
                        {
                            Address = fav.Number,
                            ContactName = fav.Name,
                            LatestMessage = "Contact favori",
                            LatestMessageTime = DateTime.MinValue,
                            IsFavorite = true
                        };
                        Conversations.Add(conversation);
                    }
                }

                SortConversations(selectedAddress);
            });
        }

        // ──────────────────────────────────────────────
        // Private helpers
        // ──────────────────────────────────────────────

        private void LoadConversationsFromDb()
        {
            using var db = new SmsDbContext();
            var allMessages = db.Messages.Where(m => !m.IsDeleted).ToList();
            var groups = allMessages.GroupBy(m => NormalizePhoneNumber(m.Address));
            var tempConversations = new List<Conversation>();

            foreach (var group in groups)
            {
                var sortedMsgs = group.OrderBy(m => m.DateTimestamp).ToList();
                var latestMsg = sortedMsgs.Last();

                var conversation = new Conversation
                {
                    Address = latestMsg.Address,
                    ContactName = latestMsg.ContactName,
                    LatestMessage = latestMsg.Body,
                    LatestMessageTime = latestMsg.LocalDateTime
                };

                foreach (var msg in sortedMsgs)
                {
                    conversation.Messages.Add(msg);
                }

                tempConversations.Add(conversation);
            }

            var sorted = tempConversations
                .OrderByDescending(c => c.IsFavorite)
                .ThenByDescending(c => c.LatestMessageTime);

            foreach (var c in sorted)
            {
                Conversations.Add(c);
            }
        }

        private void AddMessageToUI(SmsEntry sms)
        {
            using (var db = new SmsDbContext())
            {
                var isDeleted = db.Messages.Any(m => m.Id == sms.Id && m.IsDeleted);
                if (isDeleted) return;
            }

            var conversation = Conversations.FirstOrDefault(c => NormalizePhoneNumber(c.Address) == NormalizePhoneNumber(sms.Address));
            if (conversation == null)
            {
                conversation = new Conversation
                {
                    Address = sms.Address,
                    ContactName = sms.ContactName,
                    LatestMessage = sms.Body,
                    LatestMessageTime = sms.LocalDateTime
                };
                Conversations.Add(conversation);
            }

            if (!conversation.Messages.Any(m => m.Id == sms.Id))
            {
                int insertIdx = 0;
                while (insertIdx < conversation.Messages.Count && conversation.Messages[insertIdx].DateTimestamp < sms.DateTimestamp)
                {
                    insertIdx++;
                }
                conversation.Messages.Insert(insertIdx, sms);

                if (sms.LocalDateTime > conversation.LatestMessageTime)
                {
                    conversation.LatestMessage = sms.Body;
                    conversation.LatestMessageTime = sms.LocalDateTime;
                    conversation.ContactName = sms.ContactName;
                }
            }

            SortConversations(SelectedConversation?.Address);
        }

        private void SortConversations(string? selectedAddress)
        {
            var sorted = Conversations
                .OrderByDescending(c => c.IsFavorite)
                .ThenByDescending(c => c.LatestMessageTime)
                .ToList();

            Conversations.Clear();
            foreach (var c in sorted)
            {
                Conversations.Add(c);
            }

            if (selectedAddress != null)
            {
                _selectedConversation = Conversations.FirstOrDefault(c => NormalizePhoneNumber(c.Address) == NormalizePhoneNumber(selectedAddress));
            }

            NotifyStateChanged();
        }

        public async Task DeleteMessageAsync(string messageId)
        {
            if (string.IsNullOrEmpty(messageId)) return;

            SmsEntry? smsToDeleted = null;
            using (var db = new SmsDbContext())
            {
                var dbMsg = db.Messages.FirstOrDefault(m => m.Id == messageId);
                if (dbMsg != null)
                {
                    dbMsg.IsDeleted = true;
                    db.SaveChanges();
                    smsToDeleted = dbMsg;
                }
            }

            if (smsToDeleted == null) return;

            var conversation = Conversations.FirstOrDefault(c => NormalizePhoneNumber(c.Address) == NormalizePhoneNumber(smsToDeleted.Address));
            if (conversation != null)
            {
                var uiMsg = conversation.Messages.FirstOrDefault(m => m.Id == messageId);
                if (uiMsg != null)
                {
                    conversation.Messages.Remove(uiMsg);
                }

                if (conversation.Messages.Count == 0)
                {
                    Conversations.Remove(conversation);
                    if (SelectedConversation == conversation)
                    {
                        SelectedConversation = null;
                    }
                }
                else
                {
                    var latest = conversation.Messages.OrderBy(m => m.DateTimestamp).Last();
                    conversation.LatestMessage = latest.Body;
                    conversation.LatestMessageTime = latest.LocalDateTime;
                    conversation.ContactName = latest.ContactName;
                }
            }

            NotifyStateChanged();
        }

        public async Task SendMessageAsync()
        {
            if (SelectedConversation == null || string.IsNullOrWhiteSpace(NewMessageText))
            {
                return;
            }

            var body = NewMessageText.Trim();
            var address = SelectedConversation.Address;
            var requestId = Guid.NewGuid().ToString();

            NewMessageText = string.Empty;

            var newSms = new SmsEntry
            {
                Id = requestId,
                Address = address,
                ContactName = SelectedConversation.ContactName,
                Body = body,
                DateTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Type = 2, // SENT
                IsSynced = false
            };

            using (var db = new SmsDbContext())
            {
                db.Messages.Add(newSms);
                db.SaveChanges();
            }

            AddMessageToUI(newSms);

            bool sentToPhone = await _serverManager.SendSmsAsync(address, body, requestId);
            if (!sentToPhone)
            {
                _statusMessage = "Erreur : Impossible de joindre le téléphone (non connecté).";
                NotifyStateChanged();
            }
        }

        private static string NormalizePhoneNumber(string number)
        {
            if (string.IsNullOrEmpty(number))
            {
                return string.Empty;
            }
            var clean = new string(number.Where(char.IsDigit).ToArray());
            if (clean.Length >= 9)
            {
                return clean.Substring(clean.Length - 9);
            }
            return clean;
        }
    }
}
