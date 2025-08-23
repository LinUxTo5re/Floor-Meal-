using System;
using System.Globalization;
using Microsoft.Maui.Controls;

namespace ClientLedgerApp;

public class StringNullOrWhiteSpaceToBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var s = value as string;
        var isNullOrWhiteSpace = string.IsNullOrWhiteSpace(s);
        
        // Check if we need to invert the result
        if (parameter?.ToString() == "Invert")
        {
            return !isNullOrWhiteSpace;
        }
        
        return isNullOrWhiteSpace;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class DoubleZeroToEmptyConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null) return "";
        if (value is double d && Math.Abs(d) < double.Epsilon)
            return "";
        return System.Convert.ToString(value, culture) ?? "";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var s = value as string;
        if (string.IsNullOrWhiteSpace(s)) return 0d;
        if (double.TryParse(s, NumberStyles.Float, culture, out var d)) return d;
        return 0d;
    }
}

public class ItemNameListConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is IEnumerable<ItemPrice> list)
            return list.Select(i => i.Name).Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
        return new List<string>();
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) 
        => throw new NotImplementedException();
}

public class ProfileBackgroundConverter : IValueConverter
{
    private static readonly Color[] BackgroundColors = new[]
    {
        Color.FromArgb("#FF6B6B"), // Red
        Color.FromArgb("#4ECDC4"), // Teal
        Color.FromArgb("#45B7D1"), // Blue
        Color.FromArgb("#96CEB4"), // Green
        Color.FromArgb("#FFEAA7"), // Yellow
        Color.FromArgb("#DDA0DD"), // Plum
        Color.FromArgb("#98D8C8"), // Mint
        Color.FromArgb("#F7DC6F"), // Light Yellow
        Color.FromArgb("#BB8FCE"), // Light Purple
        Color.FromArgb("#85C1E9")  // Light Blue
    };

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is ClientSummary client && !string.IsNullOrWhiteSpace(client.Name))
        {
            // Generate a consistent color based on the client name
            var hash = client.Name.GetHashCode();
            var index = Math.Abs(hash) % BackgroundColors.Length;
            return BackgroundColors[index];
        }
        
        // Default color if no name
        return Color.FromArgb("#CCCCCC");
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}