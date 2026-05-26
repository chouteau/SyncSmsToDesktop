using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.Maui.Storage;
using MobileSmsSync.Services;

namespace MobileSmsSync.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly ISmsSyncPlatformService _platformService;
        
        public event PropertyChangedEventHandler? PropertyChanged;
        public static event Action? StateChanged;

        private static bool _isConnected;
        private static string _statusMessage = "Service arrêté. Entrez l'IP du PC.";
        private bool _isNotificationAccessGranted;
        private bool _areSmsPermissionsGranted;

        public static MainViewModel? Instance { get; private set; }

        public MainViewModel(ISmsSyncPlatformService platformService)
        {
            _platformService = platformService;
            Instance = this;
            
            // Load saved IP
            IpAddress = Preferences.Default.Get("server_ip", string.Empty);
            
            // Listen to static connection status changes
            StateChanged += OnStateChanged;
            
            CheckStatus();
        }

        public string IpAddress { get; set; } = string.Empty;

        public bool IsConnected
        {
            get => _isConnected;
            set
            {
                if (_isConnected != value)
                {
                    _isConnected = value;
                    OnPropertyChanged();
                }
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set
            {
                if (_statusMessage != value)
                {
                    _statusMessage = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsNotificationAccessGranted
        {
            get => _isNotificationAccessGranted;
            set
            {
                if (_isNotificationAccessGranted != value)
                {
                    _isNotificationAccessGranted = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool AreSmsPermissionsGranted
        {
            get => _areSmsPermissionsGranted;
            set
            {
                if (_areSmsPermissionsGranted != value)
                {
                    _areSmsPermissionsGranted = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsSmsSupported => _platformService.IsSmsSupported;

        public static void UpdateStatus(bool connected, string message)
        {
            _isConnected = connected;
            _statusMessage = message;
            StateChanged?.Invoke();
        }

        public void CheckStatus()
        {
            AreSmsPermissionsGranted = _platformService.ArePermissionsGranted();
            IsNotificationAccessGranted = _platformService.IsNotificationAccessGranted();
        }

        public void SaveIpAddress(string newIp)
        {
            IpAddress = newIp;
            Preferences.Default.Set("server_ip", newIp);
        }

        public async Task RequestPermissionsAsync()
        {
            var result = await _platformService.RequestPermissionsAsync();
            AreSmsPermissionsGranted = result;
        }

        public void OpenNotificationSettings()
        {
            _platformService.OpenNotificationSettings();
        }

        public void StartSync()
        {
            if (string.IsNullOrWhiteSpace(IpAddress))
            {
                StatusMessage = "Erreur: L'adresse IP ne peut pas être vide.";
                return;
            }

            SaveIpAddress(IpAddress);
            StatusMessage = "Démarrage du service...";
            _platformService.StartSyncService(IpAddress);
        }

        public void StopSync()
        {
            _platformService.StopSyncService();
            UpdateStatus(false, "Service arrêté.");
        }

        private void OnStateChanged()
        {
            OnPropertyChanged(nameof(IsConnected));
            OnPropertyChanged(nameof(StatusMessage));
        }

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
