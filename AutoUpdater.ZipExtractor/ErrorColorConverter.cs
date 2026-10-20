using System;
using System.Windows.Data;
using System.Windows.Media;

namespace AutoUpdater.ZipExtractor
{
    public class ErrorColorConverter : IValueConverter
    {
        static readonly Brush brushRed = Brushes.Red; // Brushes.* 已凍結，可以安全共用
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
