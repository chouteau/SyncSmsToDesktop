using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SmsSyncWindows.Models;

namespace SmsSyncWindows.Helpers
{
    public class MessageTemplateSelector : DataTemplateSelector
    {
        public DataTemplate IncomingTemplate { get; set; }
        public DataTemplate OutgoingTemplate { get; set; }

        protected override DataTemplate SelectTemplateCore(object item, DependencyObject container)
        {
            if (item is SmsMessage message)
            {
                return message.Type == 1 ? IncomingTemplate : OutgoingTemplate;
            }
            return base.SelectTemplateCore(item, container);
        }
    }
}
