using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Mitarbeiter.Kalender.App.Domain.Enums;

namespace Mitarbeiter.Kalender.App.Controls;

public sealed class StatusToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not AppointmentStatus s)
            return new SolidColorBrush(Color.FromArgb(0xFF, 0x00, 0xA3, 0xE0)); // default accent

        return s switch
        {
            AppointmentStatus.Fixed => new SolidColorBrush(Color.FromArgb(0xFF, 0x2E, 0x7D, 0x32)),
            AppointmentStatus.Tentative => new SolidColorBrush(Color.FromArgb(0xFF, 0xEF, 0x6C, 0x00)),
            AppointmentStatus.Cancelled => new SolidColorBrush(Color.FromArgb(0xFF, 0xC6, 0x28, 0x28)),
            _ => new SolidColorBrush(Color.FromArgb(0xFF, 0x00, 0xA3, 0xE0)),
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
