using System.Collections;
using SwiftExport.Wpf.EnterpriseGrid.Core.Internal;

namespace SwiftExport.Wpf.EnterpriseGrid.Columns.Lookup.Internal;

internal static class LookupValueAccessor
{
    public static object? GetValue(object? source, string? path)
    {
        return PropertyPathAccessor.GetValue(source, path);
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
