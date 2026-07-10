using System.Collections;
using System.ComponentModel;

namespace SwiftExport.Wpf.EnterpriseGrid.Columns.Lookup.Internal;

internal static class LookupValueAccessor
{
    public static object? GetValue(object? source, string? path)
    {
        if (source is null || string.IsNullOrWhiteSpace(path))
        {
            return source;
        }

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

    public static object? FindBySelectedValue(IEnumerable? items, object? selectedValue, string? selectedValuePath)
    {
        if (items is null)
        {
            return null;
        }

        foreach (var item in items)
        {
            if (Equals(GetValue(item, selectedValuePath), selectedValue))
            {
                return item;
            }
        }

        return null;
    }
}
