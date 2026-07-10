using System.Collections;
using System.ComponentModel;
using System.Globalization;

namespace SwiftExport.Wpf.EnterpriseGrid.Filtering.Internal;

internal static class GridFilterPredicate
{
    public static bool Matches(object item, GridFilterSet filterSet)
    {
        if (filterSet.Descriptors.Count == 0)
        {
            return true;
        }

        return filterSet.LogicalOperator == GridFilterLogicalOperator.And
            ? filterSet.Descriptors.All(descriptor => Matches(item, descriptor))
            : filterSet.Descriptors.Any(descriptor => Matches(item, descriptor));
    }

    private static bool Matches(object item, GridFilterDescriptor descriptor)
    {
        var actual = GetValue(item, descriptor.FieldPath);
        return descriptor.Operator switch
        {
            GridFilterOperator.IsNull => actual is null,
            GridFilterOperator.IsNotNull => actual is not null,
            GridFilterOperator.IsEmpty => string.IsNullOrEmpty(actual?.ToString()),
            GridFilterOperator.IsNotEmpty => !string.IsNullOrEmpty(actual?.ToString()),
            GridFilterOperator.Contains => CompareText(actual, descriptor, (value, filter) => value.Contains(filter)),
            GridFilterOperator.DoesNotContain => !CompareText(actual, descriptor, (value, filter) => value.Contains(filter)),
            GridFilterOperator.StartsWith => CompareText(actual, descriptor, (value, filter) => value.StartsWith(filter)),
            GridFilterOperator.EndsWith => CompareText(actual, descriptor, (value, filter) => value.EndsWith(filter)),
            GridFilterOperator.Equals => Compare(actual, descriptor.Value, descriptor.IsCaseSensitive) is 0,
            GridFilterOperator.NotEquals => Compare(actual, descriptor.Value, descriptor.IsCaseSensitive) is int notEqual && notEqual != 0,
            GridFilterOperator.GreaterThan => Compare(actual, descriptor.Value, descriptor.IsCaseSensitive) is int greater && greater > 0,
            GridFilterOperator.GreaterThanOrEqual => Compare(actual, descriptor.Value, descriptor.IsCaseSensitive) is int greaterOrEqual && greaterOrEqual >= 0,
            GridFilterOperator.LessThan => Compare(actual, descriptor.Value, descriptor.IsCaseSensitive) is int less && less < 0,
            GridFilterOperator.LessThanOrEqual => Compare(actual, descriptor.Value, descriptor.IsCaseSensitive) is int lessOrEqual && lessOrEqual <= 0,
            _ => true
        };
    }

    private static bool CompareText(
        object? actual,
        GridFilterDescriptor descriptor,
        Func<string, string, bool> comparison)
    {
        var actualText = actual?.ToString() ?? string.Empty;
        var filterText = descriptor.Value?.ToString() ?? string.Empty;
        if (!descriptor.IsCaseSensitive)
        {
            actualText = actualText.ToUpper(CultureInfo.CurrentCulture);
            filterText = filterText.ToUpper(CultureInfo.CurrentCulture);
        }

        return comparison(actualText, filterText);
    }

    private static int? Compare(object? actual, object? expected, bool isCaseSensitive)
    {
        if (actual is null)
        {
            return expected is null ? 0 : -1;
        }

        if (actual is string actualText)
        {
            return string.Compare(
                actualText,
                expected?.ToString(),
                isCaseSensitive ? StringComparison.CurrentCulture : StringComparison.CurrentCultureIgnoreCase);
        }

        object? converted;
        try
        {
            converted = ConvertTo(expected, actual.GetType());
        }
        catch (Exception exception) when (exception is FormatException or InvalidCastException or NotSupportedException or OverflowException)
        {
            return null;
        }
        if (actual is IComparable comparable)
        {
            try
            {
                return comparable.CompareTo(converted);
            }
            catch (ArgumentException)
            {
            }
        }

        return string.Compare(
            actual.ToString(),
            converted?.ToString(),
            StringComparison.CurrentCulture);
    }

    private static object? ConvertTo(object? value, Type targetType)
    {
        if (value is null || targetType.IsInstanceOfType(value))
        {
            return value;
        }

        var nonNullableType = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (nonNullableType.IsEnum && value is string enumText)
        {
            return System.Enum.Parse(nonNullableType, enumText, true);
        }

        if (nonNullableType == typeof(Guid) && Guid.TryParse(value.ToString(), out var guid))
        {
            return guid;
        }

        var converter = TypeDescriptor.GetConverter(nonNullableType);
        return converter.CanConvertFrom(value.GetType())
            ? converter.ConvertFrom(null, CultureInfo.CurrentCulture, value)
            : Convert.ChangeType(value, nonNullableType, CultureInfo.CurrentCulture);
    }

    private static object? GetValue(object? source, string path)
    {
        var current = source;
        foreach (var segment in path.Split('.'))
        {
            if (current is null)
            {
                return null;
            }

            if (current is IDictionary dictionary && dictionary.Contains(segment))
            {
                current = dictionary[segment];
                continue;
            }

            current = TypeDescriptor.GetProperties(current).Find(segment, false)?.GetValue(current);
        }

        return current;
    }
}
