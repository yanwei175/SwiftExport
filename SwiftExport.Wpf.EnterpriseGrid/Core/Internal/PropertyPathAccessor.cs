using System.Collections;
using System.Collections.Concurrent;
using System.ComponentModel;

namespace SwiftExport.Wpf.EnterpriseGrid.Core.Internal;

/// <summary>跨筛选、Lookup 与导出复用的属性路径缓存，不持有业务对象实例。</summary>
internal static class PropertyPathAccessor
{
    private static readonly ConcurrentDictionary<string, string[]> Segments =
        new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<(Type Type, string Name), PropertySegment> Properties = [];

    public static object? GetValue(object? source, string? path)
    {
        if (source is null || string.IsNullOrWhiteSpace(path))
        {
            return source;
        }

        var current = source;
        foreach (var segment in Segments.GetOrAdd(path, static value => value.Split('.')))
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

            var property = Properties.GetOrAdd(
                (current.GetType(), segment),
                static key => new PropertySegment(TypeDescriptor.GetProperties(key.Type).Find(key.Name, false)));
            current = property.Descriptor?.GetValue(current);
        }

        return current;
    }

    private sealed record PropertySegment(PropertyDescriptor? Descriptor);
}
