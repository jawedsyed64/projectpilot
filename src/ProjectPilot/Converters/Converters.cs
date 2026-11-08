using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using ProjectPilot.Models;

namespace ProjectPilot.Converters;

/// <summary>"NotStarted" → "Not Started".</summary>
public sealed class EnumDisplayConverter : IValueConverter
{
    public static string Humanize(string s) => Regex.Replace(s, "(?<=[a-z])([A-Z])", " $1");

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is System.Enum e ? Humanize(e.ToString())
        : value is string str ? Regex.Replace(str, @"^\d+\.\s*", "")
        : value?.ToString() ?? "";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

/// <summary>Maps statuses / priorities / resource-key strings to a theme brush.</summary>
public sealed class BrushKeyConverter : IValueConverter
{
    public static string KeyFor(object? value) => value switch
    {
        WorkStatus s => s switch
        {
            WorkStatus.NotStarted => "Neutral",
            WorkStatus.InProgress => "Info",
            WorkStatus.UnderReview => "Purple",
            WorkStatus.OnHold => "Warning",
            WorkStatus.Completed => "Success",
            WorkStatus.Overdue => "Danger",
            _ => "Neutral"
        },
        ProjectStatus p => p switch
        {
            ProjectStatus.Pending => "Neutral",
            ProjectStatus.Active => "Info",
            ProjectStatus.OnHold => "Warning",
            ProjectStatus.Completed => "Success",
            ProjectStatus.Cancelled => "Danger",
            _ => "Neutral"
        },
        Priority pr => pr switch
        {
            Priority.Low => "Neutral",
            Priority.Medium => "Info",
            Priority.High => "Warning",
            Priority.Critical => "Danger",
            _ => "Neutral"
        },
        string str => str,
        _ => "Neutral"
    };

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Application.Current.TryFindResource(KeyFor(value)) as Brush ?? Brushes.Gray;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

public sealed class InverseBoolToVisConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

/// <summary>Visible when the bound number is zero / the collection count is zero.</summary>
public sealed class ZeroToVisibleConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int i && i == 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
