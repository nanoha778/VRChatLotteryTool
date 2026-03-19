using System.Windows;

namespace VRChatLotteryTool.Core.Services;

public enum AppTheme { Dark, Light }

public class ThemeService
{
    public AppTheme CurrentTheme { get; private set; } = AppTheme.Dark;

    public void Apply(AppTheme theme)
    {
        CurrentTheme = theme;
        var res = Application.Current.Resources;

        if (theme == AppTheme.Dark)
        {
            res["BgDark"]        = Brush("#1A1A2E");
            res["BgMid"]         = Brush("#16213E");
            res["BgPanel"]       = Brush("#0F3460");
            res["Accent"]        = Brush("#E94560");
            res["AccentHover"]   = Brush("#FF6B81");
            res["TextPrimary"]   = Brush("#EAEAEA");
            res["TextSecondary"] = Brush("#A0A0B0");
            res["Border"]        = Brush("#2A4080");
            res["TextBoxBg"]     = Brush("#0D1B3A");
            res["ListHover"]     = Brush("#1A3060");
            res["ListSelected"]  = Brush("#1E3A70");
            res["HeaderBg"]      = Brush("#0D1B3A");
            res["StatusBadgeBg"] = Brush("#2A4080");
            res["SecondaryBtn"]  = Brush("#2A4080");
        }
        else
        {
            res["BgDark"]        = Brush("#F0F2F5");
            res["BgMid"]         = Brush("#FFFFFF");
            res["BgPanel"]       = Brush("#E8ECF0");
            res["Accent"]        = Brush("#D32F2F");
            res["AccentHover"]   = Brush("#EF5350");
            res["TextPrimary"]   = Brush("#1A1A2E");
            res["TextSecondary"] = Brush("#555577");
            res["Border"]        = Brush("#B0BECC");
            res["TextBoxBg"]     = Brush("#F8FAFC");
            res["ListHover"]     = Brush("#E3EAF2");
            res["ListSelected"]  = Brush("#D0DCF0");
            res["HeaderBg"]      = Brush("#DDE5EF");
            res["StatusBadgeBg"] = Brush("#B0BECC");
            res["SecondaryBtn"]  = Brush("#7A8FAF");
        }
    }

    private static System.Windows.Media.SolidColorBrush Brush(string hex)
    {
        var c = (System.Windows.Media.Color)
            System.Windows.Media.ColorConverter.ConvertFromString(hex);
        return new System.Windows.Media.SolidColorBrush(c);
    }
}
