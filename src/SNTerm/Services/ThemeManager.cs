using System;
using System.Windows;
using System.Windows.Media;

namespace SNTerm.Services;

public static class ThemeManager
{
    public static void ApplyTheme(string theme)
    {
        bool isDark = !string.Equals(theme, "Light", StringComparison.OrdinalIgnoreCase);
        var resources = Application.Current?.Resources;
        if (resources == null) return;

        if (isDark)
        {
            resources["ThemeWindowBackground"] = new SolidColorBrush(Color.FromRgb(30, 30, 30));       // #1E1E1E
            resources["ThemeToolbarBackground"] = new SolidColorBrush(Color.FromRgb(37, 37, 38));      // #252526
            resources["ThemeSidebarBackground"] = new SolidColorBrush(Color.FromRgb(37, 37, 38));      // #252526
            resources["ThemeBorderBrush"] = new SolidColorBrush(Color.FromRgb(60, 60, 60));           // #3C3C3C
            resources["ThemeForeground"] = new SolidColorBrush(Color.FromRgb(220, 220, 220));          // #DCDCDC
            resources["ThemeForegroundMuted"] = new SolidColorBrush(Color.FromRgb(160, 160, 160));     // #A0A0A0
            resources["ThemeInputBackground"] = new SolidColorBrush(Color.FromRgb(45, 45, 48));        // #2D2D30
            resources["ThemeButtonBackground"] = new SolidColorBrush(Color.FromRgb(45, 49, 57));       // #2D3139
            resources["ThemeButtonHover"] = new SolidColorBrush(Color.FromRgb(58, 63, 75));            // #3A3F4B
            resources["ThemeButtonHoverBorder"] = new SolidColorBrush(Color.FromRgb(97, 175, 239));    // #61AFEF
            resources["ThemeButtonHoverForeground"] = new SolidColorBrush(Color.FromRgb(255, 255, 255));
            resources["ThemeItemHover"] = new SolidColorBrush(Color.FromRgb(9, 71, 113));              // #094771 (VS Code blue highlight)
            resources["ThemeItemSelected"] = new SolidColorBrush(Color.FromRgb(14, 99, 156));          // #0E639C
            resources["ThemeStatusBackground"] = new SolidColorBrush(Color.FromRgb(30, 30, 30));       // #1E1E1E
            resources["ThemeCardBackground"] = new SolidColorBrush(Color.FromRgb(37, 37, 38));         // #252526
            resources["ThemePopupBackground"] = new SolidColorBrush(Color.FromRgb(37, 37, 38));        // #252526
            resources["ThemeScrollBarThumb"] = new SolidColorBrush(Color.FromRgb(85, 85, 85));         // #555555
            resources["ThemeScrollBarThumbHover"] = new SolidColorBrush(Color.FromRgb(115, 115, 115)); // #737373
            resources["ThemeConnectButtonBackground"] = new SolidColorBrush(Color.FromRgb(14, 99, 156));// #0E639C
            resources["ThemeConnectButtonForeground"] = new SolidColorBrush(Color.FromRgb(255, 255, 255));
            resources["ThemeConnectButtonBorder"] = new SolidColorBrush(Color.FromRgb(17, 119, 187));
            resources["ThemeConnectButtonHoverBackground"] = new SolidColorBrush(Color.FromRgb(17, 119, 187)); // #1177BB
            resources["ThemeConnectButtonHoverBorder"] = new SolidColorBrush(Color.FromRgb(40, 150, 224));
            resources["ThemeConnectButtonActiveBackground"] = new SolidColorBrush(Color.FromRgb(11, 79, 125));
            resources["ThemeTabSelectedBackground"] = new SolidColorBrush(Color.FromRgb(30, 30, 30));
            resources["ThemeTabUnselectedBackground"] = new SolidColorBrush(Color.FromRgb(45, 45, 48));
            resources["ThemeTabSelectedForeground"] = new SolidColorBrush(Color.FromRgb(255, 255, 255));
            resources["ThemeTabUnselectedForeground"] = new SolidColorBrush(Color.FromRgb(170, 170, 170));
        }
        else
        {
            resources["ThemeWindowBackground"] = new SolidColorBrush(Color.FromRgb(255, 255, 255));
            resources["ThemeToolbarBackground"] = new SolidColorBrush(Color.FromRgb(248, 249, 250));
            resources["ThemeSidebarBackground"] = new SolidColorBrush(Color.FromRgb(250, 250, 250));
            resources["ThemeBorderBrush"] = new SolidColorBrush(Color.FromRgb(208, 213, 221));
            resources["ThemeForeground"] = new SolidColorBrush(Color.FromRgb(16, 24, 40));
            resources["ThemeForegroundMuted"] = new SolidColorBrush(Color.FromRgb(102, 112, 133));
            resources["ThemeInputBackground"] = new SolidColorBrush(Color.FromRgb(255, 255, 255));
            resources["ThemeButtonBackground"] = new SolidColorBrush(Color.FromRgb(242, 244, 247));
            resources["ThemeButtonHover"] = new SolidColorBrush(Color.FromRgb(228, 231, 236));
            resources["ThemeButtonHoverBorder"] = new SolidColorBrush(Color.FromRgb(152, 162, 179));
            resources["ThemeButtonHoverForeground"] = new SolidColorBrush(Color.FromRgb(16, 24, 40));
            resources["ThemeItemHover"] = new SolidColorBrush(Color.FromRgb(235, 243, 255));
            resources["ThemeItemSelected"] = new SolidColorBrush(Color.FromRgb(209, 233, 255));
            resources["ThemeStatusBackground"] = new SolidColorBrush(Color.FromRgb(242, 244, 247));
            resources["ThemeCardBackground"] = new SolidColorBrush(Color.FromRgb(255, 255, 255));
            resources["ThemePopupBackground"] = new SolidColorBrush(Color.FromRgb(255, 255, 255));
            resources["ThemeScrollBarThumb"] = new SolidColorBrush(Color.FromRgb(193, 193, 193));
            resources["ThemeScrollBarThumbHover"] = new SolidColorBrush(Color.FromRgb(168, 168, 168));
            resources["ThemeConnectButtonBackground"] = new SolidColorBrush(Color.FromRgb(21, 112, 239)); // #1570EF
            resources["ThemeConnectButtonForeground"] = new SolidColorBrush(Color.FromRgb(255, 255, 255));
            resources["ThemeConnectButtonBorder"] = new SolidColorBrush(Color.FromRgb(21, 112, 239));
            resources["ThemeConnectButtonHoverBackground"] = new SolidColorBrush(Color.FromRgb(23, 92, 211));
            resources["ThemeConnectButtonHoverBorder"] = new SolidColorBrush(Color.FromRgb(23, 92, 211));
            resources["ThemeConnectButtonActiveBackground"] = new SolidColorBrush(Color.FromRgb(24, 73, 169));
            resources["ThemeTabSelectedBackground"] = new SolidColorBrush(Color.FromRgb(255, 255, 255));
            resources["ThemeTabUnselectedBackground"] = new SolidColorBrush(Color.FromRgb(242, 244, 247));
            resources["ThemeTabSelectedForeground"] = new SolidColorBrush(Color.FromRgb(16, 24, 40));
            resources["ThemeTabUnselectedForeground"] = new SolidColorBrush(Color.FromRgb(102, 112, 133));
        }
    }
}