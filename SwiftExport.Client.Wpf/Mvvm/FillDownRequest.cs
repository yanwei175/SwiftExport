namespace SwiftExportClient.Mvvm;

/// <summary>视图提交的向下填充请求；只携带源行、目标行和业务字段标识，不持有控件或负责赋值。</summary>
public sealed class FillDownRequest(object source, IEnumerable<object> targets, IEnumerable<string> fields)
{
    /// <summary>当前视图选区的首行。</summary>
    public object Source { get; } = source;

    /// <summary>按视图顺序排列的目标行快照。</summary>
    public IReadOnlyList<object> Targets { get; } = targets.ToArray();

    /// <summary>显式开放填充的业务字段；其含义由页面命令解释。</summary>
    public IReadOnlyList<string> Fields { get; } = fields.ToArray();

    /// <summary>核对请求中的所有行仍属于当前页面，拒绝外来行、重复目标及空字段请求。</summary>
    public bool TryGetRows<T>(IEnumerable<T> rows, out T source, out IReadOnlyList<T> targets) where T : class
    {
        source = Source as T ?? null!;
        targets = Targets.OfType<T>().ToArray();
        var owned = new HashSet<object>(rows, ReferenceEqualityComparer.Instance);
        return source is not null && owned.Contains(source)
            && Fields.Count > 0 && Fields.All(field => !string.IsNullOrWhiteSpace(field))
            && targets.Count > 0 && targets.Count == Targets.Count
            && Targets.Distinct(ReferenceEqualityComparer.Instance).Count() == Targets.Count
            && Targets.All(target => !ReferenceEquals(target, Source) && owned.Contains(target));
    }
}
