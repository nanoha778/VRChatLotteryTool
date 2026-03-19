using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using VRChatLotteryTool.Core.Models;

namespace VRChatLotteryTool.UI.Converters;

/// <summary>bool → Visibility</summary>
public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
        => value is true ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type t, object p, CultureInfo c)
        => value is Visibility.Visible;
}

/// <summary>LogLevel → Foreground ブラシ</summary>
public class LogLevelToColorConverter : IValueConverter
{
    private static readonly SolidColorBrush Info    = new(Color.FromRgb(0xA0, 0xA0, 0xB0));
    private static readonly SolidColorBrush Success = new(Color.FromRgb(0x4C, 0xAF, 0x50));
    private static readonly SolidColorBrush Warning = new(Color.FromRgb(0xFF, 0xC1, 0x07));
    private static readonly SolidColorBrush Error   = new(Color.FromRgb(0xF4, 0x43, 0x36));

    public object Convert(object value, Type t, object p, CultureInfo c)
        => value is LogLevel level ? level switch
        {
            LogLevel.Success => Success,
            LogLevel.Warning => Warning,
            LogLevel.Error   => Error,
            _                => Info
        } : Info;

    public object ConvertBack(object value, Type t, object p, CultureInfo c)
        => throw new NotImplementedException();
}

/// <summary>当選フラグ → 行背景ブラシ</summary>
public class WinnerToBackgroundConverter : IValueConverter
{
    private static readonly SolidColorBrush WinBrush
        = new(Color.FromArgb(0x30, 0x4C, 0xAF, 0x50));
    private static readonly SolidColorBrush NormalBrush
        = new(Colors.Transparent);

    public object Convert(object value, Type t, object p, CultureInfo c)
        => value is true ? WinBrush : NormalBrush;
    public object ConvertBack(object value, Type t, object p, CultureInfo c)
        => throw new NotImplementedException();
}
