using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using SmsSyncWindows.Data;
using SmsSyncWindows.Models;
using SmsSyncWindows.Services;

namespace SmsSyncWindows.ViewModels
{
    public class MainPageViewModel : INotifyPropertyChanged
    {
        private readonly WebSocketServerManager _serverManager;
        private readonly DispatcherQueue _dispatcherQueue;

        private ObservableCollection<Conversation> _conversations = new ObservableCollection<Conversation>();
        private Conversation _selectedConversation;
        private string _statusMessage = "Initialisation...";
        private bool _isConnected;
        private string _newMessageText;
        private string _serverIpPort;
        private bool _isSyncing;
        private double _syncProgressValue;
        private string _syncStatusText = string.Empty;

        public event PropertyChangedEventHandler PropertyChanged;

        public ObservableCollection<Conversation> Conversations
        {
            get => _conversations;
            set
            {
                if (_conversations == value) return;
                _conversations = value;
                OnPropertyChanged();
            }
        }

        public Conversation SelectedConversation
        {
            get => _selectedConversation;
            set
            {
                if (_selectedConversation == value) return;
                _selectedConversation = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasSelectedConversation));
            }
        }

        public bool HasSelectedConversation => SelectedConversation != null;

        public string StatusMessage
        {
            get => _statusMessage;
            set
            {
                if (_statusMessage == value) return;
                _statusMessage = value;
                OnPropertyChanged();
            }
        }

        public bool IsConnected
        {
            get => _isConnected;
            set
            {
                if (_isConnected == value) return;
                _isConnected = value;
                OnPropertyChanged();
            }
        }

        public string NewMessageText
        {
            get => _newMessageText;
            set
            {
                if (_newMessageText == value) return;
                _newMessageText = value;
                OnPropertyChanged();
            }
        }

        public string ServerIpPort
        {
            get => _serverIpPort;
            set
            {
                if (_serverIpPort == value) return;
                _serverIpPort = value;
                OnPropertyChanged();
            }
        }

        public bool IsSyncing
        {
            get => _isSyncing;
            set
            {
                if (_isSyncing == value) return;
                _isSyncing = value;
                OnPropertyChanged();
            }
        }

        public double SyncProgressValue
        {
            get => _syncProgressValue;
            set
            {
                if (_syncProgressValue == value) return;
                _syncProgressValue = value;
                OnPropertyChanged();
            }
        }

        public string SyncStatusText
        {
            get => _syncStatusText;
            set
            {
                if (_syncStatusText == value) return;
                _syncStatusText = value;
                OnPropertyChanged();
            }
        }

        public MainPageViewModel()
        {
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
            
            // Initialiser la base de données (création si elle n'existe pas)
            using (var db = new SmsDbContext())
            {
                db.Database.EnsureCreated();
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

        public void Cleanup()
        {
            _serverManager.Stop();
        }

        public void RequestSync()
        {
            _serverManager.RequestSync();
        }

        private void OnServerStatusChanged(object sender, string status)
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                StatusMessage = status;
                IsConnected = _serverManager.IsClientConnected;
            });
        }

        private void OnSyncStarted(object sender, int total)
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                IsSyncing = true;
                SyncProgressValue = 0;
                SyncStatusText = $"Démarrage de la synchronisation... (0/{total})";
            });
        }

        private void OnSyncProgressChanged(object sender, SyncProgressPayload progress)
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                IsSyncing = true;
                double percentage = progress.Total > 0 ? ((double)progress.Current / progress.Total) * 100 : 0;
                SyncProgressValue = percentage;
                SyncStatusText = $"Synchronisation : {progress.Current} / {progress.Total} messages";
            });
        }

        private void OnSyncCompleted(object sender, EventArgs e)
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                IsSyncing = false;
                SyncProgressValue = 100;
                SyncStatusText = "Synchronisation terminée";
            });
        }

        private void OnNewSmsReceived(object sender, SmsMessage sms)
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                AddMessageToUI(sms);
            });
        }

        private void OnHistoryReceived(object sender, List<SmsMessage> messages)
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                var selectedAddress = SelectedConversation?.Address;
                var groups = messages.GroupBy(m => m.Address);
                
                foreach (var group in groups)
                {
                    var conversation = Conversations.FirstOrDefault(c => c.Address == group.Key);
                    bool isNew = false;
                    if (conversation == null)
                    {
                        var latestMsg = group.OrderBy(m => m.DateTimestamp).Last();
                        conversation = new Conversation
                        {
                            Address = group.Key,
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

                // Trier toute la liste globale une seule fois à la fin (favoris en haut, puis par date)
                var sorted = Conversations.OrderByDescending(c => c.IsFavorite)
                                           .ThenByDescending(c => c.LatestMessageTime)
                                           .ToList();
                Conversations.Clear();
                foreach (var c in sorted)
                {
                    Conversations.Add(c);
                }

                if (selectedAddress != null)
                {
                    SelectedConversation = Conversations.FirstOrDefault(c => c.Address == selectedAddress);
                }
            });
        }

        private void OnSendSmsStatusReceived(object sender, SendSmsStatusPayload status)
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                // Trouver le message envoyé en attente de synchro pour mettre à jour son statut
                using (var db = new SmsDbContext())
                {
                    var msg = db.Messages.FirstOrDefault(m => m.Id == status.RequestId);
                    if (msg != null)
                    {
                        msg.IsSynced = status.Success;
                        db.SaveChanges();
                    }
                }

                // Mettre à jour l'état visuel du message dans la conversation active
                if (SelectedConversation != null)
                {
                    var activeMsg = SelectedConversation.Messages.FirstOrDefault(m => m.Id == status.RequestId);
                    if (activeMsg != null)
                    {
                        activeMsg.IsSynced = status.Success;
                        // Forcer la mise à jour visuelle en rechargeant la conversation si nécessaire
                        // (Dans une vraie appli, SmsMessage implémenterait INotifyPropertyChanged)
                    }
                }
                
                if (!status.Success)
                {
                    StatusMessage = $"Échec de l'envoi du SMS : {status.ErrorMessage}";
                }
            });
        }

        private void OnFavoritesReceived(object sender, List<FavoriteContact> favorites)
        {
            Console.WriteLine($"[ViewModel] OnFavoritesReceived appelé avec {favorites?.Count} favoris.");
            _dispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    var selectedAddress = SelectedConversation?.Address;
                    Console.WriteLine($"[ViewModel] Traitement de {favorites?.Count} favoris sur le thread UI. Nombre actuel de conversations: {Conversations.Count}");

                    foreach (var fav in favorites)
                    {
                        if (string.IsNullOrWhiteSpace(fav.Number))
                        {
                            Console.WriteLine($"[ViewModel] Favori ignoré car numéro vide. Nom: {fav.Name}");
                            continue;
                        }

                        var normalizedFavNumber = NormalizePhoneNumber(fav.Number);
                        var conversation = Conversations.FirstOrDefault(c => NormalizePhoneNumber(c.Address) == normalizedFavNumber);
                        
                        if (conversation != null)
                        {
                            Console.WriteLine($"[ViewModel] Favori trouvé dans conversations existantes. Nom: {fav.Name}, Numéro: {fav.Number}, Normalisé: {normalizedFavNumber}");
                            conversation.IsFavorite = true;
                            if (!string.IsNullOrEmpty(fav.Name))
                            {
                                conversation.ContactName = fav.Name;
                            }
                        }
                        else
                        {
                            Console.WriteLine($"[ViewModel] Favori non trouvé, création d'un placeholder. Nom: {fav.Name}, Numéro: {fav.Number}, Normalisé: {normalizedFavNumber}");
                            // Créer un placeholder pour le contact favori
                            conversation = new Conversation
                            {
                                Address = fav.Number,
                                ContactName = fav.Name,
                                LatestMessage = "Contact favori",
                                LatestMessageTime = DateTime.MinValue, // Sortir au plus bas parmi les favoris s'il n'y a pas de message
                                IsFavorite = true
                            };
                            Conversations.Add(conversation);
                        }
                    }

                    // Réordonner la liste avec les favoris en haut
                    var sorted = Conversations.OrderByDescending(c => c.IsFavorite)
                                               .ThenByDescending(c => c.LatestMessageTime)
                                               .ToList();
                    Console.WriteLine($"[ViewModel] Réordonner la liste. Total après traitement: {sorted.Count}. Favoris: {sorted.Count(c => c.IsFavorite)}");
                    Conversations.Clear();
                    foreach (var c in sorted)
                    {
                        Conversations.Add(c);
                    }

                    if (selectedAddress != null)
                    {
                        SelectedConversation = Conversations.FirstOrDefault(c => c.Address == selectedAddress);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ViewModel] ERREUR dans OnFavoritesReceived (UI thread): {ex}");
                }
            });
        }

        private string NormalizePhoneNumber(string number)
        {
            if (string.IsNullOrEmpty(number)) return string.Empty;
            // Ne garder que les chiffres
            var clean = new string(number.Where(char.IsDigit).ToArray());
            if (clean.Length >= 9)
            {
                return clean.Substring(clean.Length - 9);
            }
            return clean;
        }

        private void LoadConversationsFromDb()
        {
            using (var db = new SmsDbContext())
            {
                var allMessages = db.Messages.ToList();
                var groups = allMessages.GroupBy(m => m.Address);
                var tempConversations = new List<Conversation>();

                foreach (var group in groups)
                {
                    var sortedMsgs = group.OrderBy(m => m.DateTimestamp).ToList();
                    var latestMsg = sortedMsgs.Last();
                    
                    var conversation = new Conversation
                    {
                        Address = group.Key,
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

                var sortedConversations = tempConversations.OrderByDescending(c => c.IsFavorite)
                                                           .ThenByDescending(c => c.LatestMessageTime);
                
                Conversations.Clear();
                foreach (var c in sortedConversations)
                {
                    Conversations.Add(c);
                }
            }
        }

        private void AddMessageToUI(SmsMessage sms)
        {
            var conversation = Conversations.FirstOrDefault(c => c.Address == sms.Address);
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

            // Éviter les messages en doublon dans la liste UI
            if (!conversation.Messages.Any(m => m.Id == sms.Id))
            {
                int msgInsertIndex = 0;
                while (msgInsertIndex < conversation.Messages.Count && conversation.Messages[msgInsertIndex].DateTimestamp < sms.DateTimestamp)
                {
                    msgInsertIndex++;
                }
                conversation.Messages.Insert(msgInsertIndex, sms);

                if (sms.LocalDateTime > conversation.LatestMessageTime)
                {
                    conversation.LatestMessage = sms.Body;
                    conversation.LatestMessageTime = sms.LocalDateTime;
                    conversation.ContactName = sms.ContactName;
                }
            }

            var selectedAddress = SelectedConversation?.Address;
            
            // Re-trier toute la liste
            var sorted = Conversations.OrderByDescending(c => c.IsFavorite)
                                       .ThenByDescending(c => c.LatestMessageTime)
                                       .ToList();
            Conversations.Clear();
            foreach (var c in sorted)
            {
                Conversations.Add(c);
            }

            if (selectedAddress != null)
            {
                SelectedConversation = Conversations.FirstOrDefault(c => c.Address == selectedAddress);
            }
        }

        public async Task SendMessageAsync()
        {
            if (SelectedConversation == null || string.IsNullOrWhiteSpace(NewMessageText))
                return;

            var body = NewMessageText.Trim();
            var address = SelectedConversation.Address;
            var requestId = Guid.NewGuid().ToString(); // ID unique temporaire pour le statut
            
            // Vider le champ de saisie immédiatement
            NewMessageText = string.Empty;

            // Créer un SmsMessage local de type SENT (2)
            var newSms = new SmsMessage
            {
                Id = requestId,
                Address = address,
                ContactName = SelectedConversation.ContactName,
                Body = body,
                DateTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Type = 2, // SENT
                IsSynced = false // Pas encore envoyé/confirmé par le téléphone
            };

            // Enregistrer localement dans la base de données
            using (var db = new SmsDbContext())
            {
                db.Messages.Add(newSms);
                db.SaveChanges();
            }

            // Ajouter à l'interface
            AddMessageToUI(newSms);

            // Tenter d'envoyer le message au téléphone via WebSocket
            bool sentToPhone = await _serverManager.SendSmsAsync(address, body, requestId);
            if (!sentToPhone)
            {
                StatusMessage = "Erreur : Impossible de joindre le téléphone (non connecté).";
            }
        }

        public void StartOrSelectConversation(string address)
        {
            if (string.IsNullOrWhiteSpace(address)) return;

            var conversation = Conversations.FirstOrDefault(c => c.Address.Equals(address, StringComparison.OrdinalIgnoreCase));
            if (conversation == null)
            {
                conversation = new Conversation
                {
                    Address = address,
                    ContactName = null,
                    LatestMessage = "Nouvelle conversation",
                    LatestMessageTime = DateTime.Now
                };
                Conversations.Insert(0, conversation);
            }
            SelectedConversation = conversation;
        }

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
