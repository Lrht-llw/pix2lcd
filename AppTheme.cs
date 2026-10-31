using System;
using System.Windows;
using Microsoft.Win32;

namespace pix2lcd;

public enum AppThemeMode
{
    Light,
    Dark,
}

/// <summary>按 Windows 系统的应用主题选择浅色 / 深色资源，并在启动时应用。</summary>
public static class AppTheme
{
    private static readonly Uri DarkUri = new("Themes/Dark.xaml", UriKind.Relative);
    private static readonly Uri LightUri = new("Themes/Light.xaml", UriKind.Relative);

    public static AppThemeMode Current { get; private set; } = AppThemeMode.Light;

    public static bool IsDark => Current == AppThemeMode.Dark;

    public static void Initialize() => Apply(DetectSystemTheme());

    public static void Apply(AppThemeMode mode)
    {
        Current = mode;
        var app = Application.Current;
        if (app is null) return;

        var theme = new ResourceDictionary { Source = mode == AppThemeMode.Dark ? DarkUri : LightUri };
        var merged = app.Resources.MergedDictionaries;
        if (merged.Count > 0) merged[0] = theme;
        else merged.Add(theme);
    }

    private static AppThemeMode DetectSystemTheme()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key?.GetValue("AppsUseLightTheme") is int value)
                return value == 0 ? AppThemeMode.Dark : AppThemeMode.Light;
        }
        catch
        {
            // 读不到系统设置时退回浅色。
        }
        return AppThemeMode.Light;
    }
}
