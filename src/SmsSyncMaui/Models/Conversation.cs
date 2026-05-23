using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SmsSyncMaui.Models
{
    public class Conversation : INotifyPropertyChanged
    {
        private string _address = string.Empty;
        private string? _contactName;
        private string _latestMessage = string.Empty;
        private DateTime _latestMessageTime;
        private bool _isFavorite;

        public bool IsFavorite
        {
            get => _isFavorite;
            set { _isFavorite = value; OnPropertyChanged(); }
        }

        public string Address
        {
            get => _address;
            set { _address = value; OnPropertyChanged(); }
        }

        public string? ContactName
        {
            get => string.IsNullOrEmpty(_contactName) ? _address : _contactName;
            set { _contactName = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayName)); }
        }

        public string DisplayName => string.IsNullOrEmpty(ContactName) ? Address : ContactName;

        public string LatestMessage
        {
            get => _latestMessage;
            set { _latestMessage = value; OnPropertyChanged(); }
        }

        public DateTime LatestMessageTime
        {
            get => _latestMessageTime;
            set { _latestMessageTime = value; OnPropertyChanged(); OnPropertyChanged(nameof(FormattedLatestMessageTime)); }
        }

        public string FormattedLatestMessageTime =>
            LatestMessageTime == DateTime.MinValue ? string.Empty : LatestMessageTime.ToString("g");

        public ObservableCollection<SmsEntry> Messages { get; set; } = new ObservableCollection<SmsEntry>();

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
