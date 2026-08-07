using Microsoft.Win32;
using System.Windows;
using System.Windows.Media;

namespace CodexTempo;

internal static class ThemeService
{
    public static bool IsDarkMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch
        {
            return false;
        }
    }

    public static bool Apply(ResourceDictionary resources, bool? forceDark = null)
    {
        var dark = forceDark ?? IsDarkMode();
        Set(resources, "Ink", dark ? "#F5F5F7" : "#1D1D1F");
        Set(resources, "Muted", dark ? "#A3A3AA" : "#6E6E73");
        Set(resources, "Hairline", dark ? "#3F3F44" : "#E5E5EA");
        Set(resources, "Accent", "#007AFF");
        Set(resources, "Track", dark ? "#3A3A3C" : "#E5E5EA");
        Set(resources, "PanelBackground", dark ? "#1C1C1E" : "#F5F5F7");
        Set(resources, "CardBackground", dark ? "#2C2C2E" : "#FFFFFF");
        Set(resources, "CardBorder", dark ? "#414146" : "#E5E5EA");
        Set(resources, "ChromeHover", dark ? "#18FFFFFF" : "#12000000");
        Set(resources, "ChromePressed", dark ? "#2AFFFFFF" : "#22000000");
        Set(resources, "FooterMuted", dark ? "#77777E" : "#8E8E93");
        return dark;
    }

    private static void Set(ResourceDictionary resources, string key, string color) =>
        resources[key] = new SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color));
}
