using System;
using System.Globalization;
using System.Windows.Data;

namespace EMGFeedbackSystem.Converters
{
    public class PercentToOffsetConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < 2)
            {
                return 0.0;
            }

            if (values[0] is not double width || values[1] is not double percent)
            {
                return 0.0;
            }

            if (width <= 0)
            {
                return 0.0;
            }

            double clampedPercent = Math.Max(0, Math.Min(100, percent));
            double offset = (clampedPercent / 100.0) * width;

            // Center a 2px marker on the computed position.
            return Math.Max(0, Math.Min(width - 1, offset - 1));
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
