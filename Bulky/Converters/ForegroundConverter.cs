using System;
using System.Windows.Data;
using System.Windows.Media;
using Bulky.ViewModels;

namespace Bulky.Converters
{
    class ForegroundConverter : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            {
                if (value != null)
                {
                    Severity severity = (Severity)value;
                    switch (severity)
                    {
                        case Severity.Error:
                            return new SolidColorBrush(Colors.Red);
                        case Severity.Warn:
                            return new SolidColorBrush(Colors.Orange);
                        default:
                            return new SolidColorBrush(Color.FromRgb(192, 192, 192));                            
                    }
                }
                return new SolidColorBrush(Colors.Green);
            }

            public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            {
                return null;
            }
        }
}
