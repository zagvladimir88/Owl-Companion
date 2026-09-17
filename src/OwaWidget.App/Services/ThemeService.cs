using System.Windows;
using Microsoft.Win32;
using Wpf.Ui.Appearance;
using Application = System.Windows.Application;

namespace OwaWidget.App.Services;

public enum ThemeChoice
{
    Light,
    Dark,
    System
}

public static class ThemeService
{
    private const string PersonalizeKey =
        @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private static ResourceDictionary? _palette;
    private static ThemeChoice _mode = ThemeChoice.System;
    private static bool _dark;
    private static bool _taskbarDark;

    public static event Action? Changed;

    public static event Action? TaskbarChanged;

    public static ThemeChoice Mode => _mode;

    public static bool IsDark => _dark;

    public static bool IsTaskbarDark => _taskbarDark;

    public static void Start(ThemeChoice mode)
    {
        _taskbarDark = !ReadFlag("SystemUsesLightTheme");
        SystemEvents.UserPreferenceChanged += OnPreferenceChanged;
        Apply(mode);
    }

    public static void Stop()
    {
        SystemEvents.UserPreferenceChanged -= OnPreferenceChanged;
    }

    public static void Apply(ThemeChoice mode)
    {
        _mode = mode;

        var dark = mode switch
        {
            ThemeChoice.Light => false,
            ThemeChoice.Dark => true,
            _ => !ReadFlag("AppsUseLightTheme")
        };

        if (_palette is not null && dark == _dark)
        {
            return;
        }

        _dark = dark;
        Swap(dark);

        Log.Info($"theme applied: mode={Name(mode)}, dark={dark}, taskbarDark={_taskbarDark}");
        Changed?.Invoke();
    }

    public static ThemeChoice Parse(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "light" => ThemeChoice.Light,
            "dark" => ThemeChoice.Dark,
            _ => ThemeChoice.System
        };
    }

    public static string Name(ThemeChoice mode)
    {
        return mode switch
        {
            ThemeChoice.Light => "light",
            ThemeChoice.Dark => "dark",
            _ => "system"
        };
    }

    private static void Swap(bool dark)
    {
        ApplicationThemeManager.Apply(dark ? ApplicationTheme.Dark : ApplicationTheme.Light);

        var merged = Application.Current.Resources.MergedDictionaries;

        if (_palette is not null)
        {
            merged.Remove(_palette);
        }

        var next = new ResourceDictionary
        {
            Source = new Uri(
                dark
                    ? "pack://application:,,,/OwaWidget;component/Themes/Dark.xaml"
                    : "pack://application:,,,/OwaWidget;component/Themes/Light.xaml",
                UriKind.Absolute)
        };

        merged.Add(next);
        _palette = next;
    }

    private static void OnPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is not (UserPreferenceCategory.General or UserPreferenceCategory.VisualStyle
            or UserPreferenceCategory.Color))
        {
            return;
        }

        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            var taskbarDark = !ReadFlag("SystemUsesLightTheme");

            if (taskbarDark != _taskbarDark)
            {
                _taskbarDark = taskbarDark;
                TaskbarChanged?.Invoke();
            }

            if (_mode == ThemeChoice.System)
            {
                Apply(ThemeChoice.System);
            }
        });
    }

    private static bool ReadFlag(string name)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue(name) is not int value || value != 0;
        }
        catch (Exception exception)
        {
            Log.Error($"reading {name} failed", exception);
            return true;
        }
    }
}