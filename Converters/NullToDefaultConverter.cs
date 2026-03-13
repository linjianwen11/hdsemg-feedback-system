using System;
using System.Globalization;
using System.Windows.Data;

namespace EMGFeedbackSystem.Converters
{
    public class NullOrEmptyToDefaultConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string defaultText = parameter as string ?? "";
            string actualValue = value?.ToString() ?? "";
            
            return string.IsNullOrEmpty(actualValue) ? defaultText : actualValue;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
