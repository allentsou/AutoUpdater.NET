using System;
using System.Windows.Data;
using System.Windows.Media;

namespace AutoUpdater.ZipExtractor
{
    public class ErrorColorConverter : IValueConverter
    {
        static Brush brushRed = new SolidColorBrush(Colors.Red);
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is bool booleanValue)
            {
                if (booleanValue)
                {
                    return brushRed;
                }
                else
                {
                    //return System.Drawing.Color.Blue;
                    return Binding.DoNothing;
                }
            }
            return Binding.DoNothing;
            //return System.Drawing.Color.Blue;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
