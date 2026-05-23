using System;
using System.Collections.Specialized;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using SmsSyncWindows.Models;
using SmsSyncWindows.ViewModels;

namespace SmsSyncWindows
{
    public sealed partial class MainPage : Page
    {
        public MainPageViewModel ViewModel { get; }

        public MainPage()
        {
            this.InitializeComponent();
            ViewModel = new MainPageViewModel();
            this.DataContext = ViewModel;

            // Détecter les changements de conversation sélectionnée pour gérer le scroll
            ViewModel.PropertyChanged += ViewModel_PropertyChanged;
            
            this.Unloaded += MainPage_Unloaded;
        }

        private void MainPage_Unloaded(object sender, RoutedEventArgs e)
        {
            ViewModel.Cleanup();
        }

        private void ViewModel_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ViewModel.SelectedConversation))
            {
                if (ViewModel.SelectedConversation != null)
                {
                    // S'abonner aux nouveaux messages de cette conversation pour scroller automatiquement
                    ViewModel.SelectedConversation.Messages.CollectionChanged += Messages_CollectionChanged;
                    ScrollToBottom();
                }
            }
        }

        private void Messages_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            ScrollToBottom();
        }

        private void ScrollToBottom()
        {
            // Défiler vers le bas après que le layout ait été mis à jour
            DispatcherQueue.TryEnqueue(async () =>
            {
                // Un léger délai permet au contrôle ListView de générer les conteneurs d'items
                await Task.Delay(50);
                if (ViewModel.SelectedConversation != null && ViewModel.SelectedConversation.Messages.Count > 0)
                {
                    var lastIndex = ViewModel.SelectedConversation.Messages.Count - 1;
                    var lastItem = ViewModel.SelectedConversation.Messages[lastIndex];
                    MessagesList.ScrollIntoView(lastItem, ScrollIntoViewAlignment.Default);
                }
            });
        }

        private async void OnSendClicked(object sender, RoutedEventArgs e)
        {
            await SendMessageAsync();
        }

        private async void OnNewMessageTextBoxKeyDown(object sender, KeyRoutedEventArgs e)
        {
            // Envoyer avec Enter, mais permettre Shift+Enter pour un saut de ligne
            if (e.Key == Windows.System.VirtualKey.Enter && !Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down))
            {
                e.Handled = true;
                await SendMessageAsync();
            }
        }

        private async Task SendMessageAsync()
        {
            await ViewModel.SendMessageAsync();
            ScrollToBottom();
        }

        private void OnRefreshClicked(object sender, RoutedEventArgs e)
        {
            ViewModel.RequestSync();
        }

        private void OnStartNewConversationClicked(object sender, RoutedEventArgs e)
        {
            StartNewConversation();
        }

        private void OnNewConversationNumberBoxKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                e.Handled = true;
                StartNewConversation();
            }
        }

        private void StartNewConversation()
        {
            var number = NewConversationNumberBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(number)) return;

            ViewModel.StartOrSelectConversation(number);
            NewConversationNumberBox.Text = string.Empty;
        }
    }
}
