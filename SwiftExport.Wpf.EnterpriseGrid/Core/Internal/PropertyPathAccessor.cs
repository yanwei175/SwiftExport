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

    public static bool CanSetValue(object? source, string? path)
    {
        return TryGetTarget(source, path, out var target, out var segment) &&
               (target is IDictionary || segment.Descriptor is { IsReadOnly: false });
    }

    public static bool TrySetValue(object? source, string? path, object? value)
    {
        if (!TryGetTarget(source, path, out var target, out var segment))
        {
            return false;
        }

        if (target is IDictionary dictionary)
        {
            dictionary[segment.Name] = value;
            return true;
        }

        if (segment.Descriptor is null || segment.Descriptor.IsReadOnly)
        {
            return false;
        }

        segment.Descriptor.SetValue(target, value);
        return true;
    }

    private static bool TryGetTarget(
        object? source,
        string? path,
        out object target,
        out WritablePropertySegment segment)
    {
        target = null!;
        segment = default;
        if (source is null || string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var parts = Segments.GetOrAdd(path, static value => value.Split('.'));
        var current = source;
        for (var index = 0; index < parts.Length - 1; index++)
        {
            current = GetValue(current, parts[index]);
            if (current is null)
            {
                return false;
            }
        }

        target = current;
        var name = parts[^1];
        var descriptor = current is IDictionary
            ? null
            : Properties.GetOrAdd(
                (current.GetType(), name),
                static key => new PropertySegment(TypeDescriptor.GetProperties(key.Type).Find(key.Name, false)))
                .Descriptor;
        segment = new WritablePropertySegment(name, descriptor);
        return current is IDictionary || descriptor is not null;
    }

    private sealed record PropertySegment(PropertyDescriptor? Descriptor);

    private readonly record struct WritablePropertySegment(string Name, PropertyDescriptor? Descriptor);
}
