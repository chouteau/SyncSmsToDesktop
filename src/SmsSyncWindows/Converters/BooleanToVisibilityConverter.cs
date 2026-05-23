using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace SmsSyncWindows.Converters
{
    public class BooleanToVisibilityConverter : IValueConverter
    {
        public bool IsReversed { get; set; }

        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is bool boolValue)
            {
                var result = IsReversed ? !boolValue : boolValue;
                return result ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            if (value is Visibility visibility)
            {
                var result = visibility == Visibility.Visible;
                return IsReversed ? !result : result;
            }
            return false;
        }
    }
}
