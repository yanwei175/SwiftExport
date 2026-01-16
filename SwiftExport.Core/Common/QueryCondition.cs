namespace SwiftExport.Core.Common;

public enum 查询操作符
{
    等于,        // Equal
    模糊匹配,    // Like
    大于,        // GreaterThan
    小于,        // LessThan
    大于等于,    // GreaterOrEqual
    小于等于,    // LessOrEqual
    区间,         // Between
    包含某值      // In
}
/// <summary>
/// 表示单个字段的查询条件配置对象。
/// </summary>
/// <remarks>
/// 该对象用于定义在查询时，针对某个特定字段应采用的操作符、单个值或值区间。
/// 通常作为字典的值被使用，字典的Key为待查询的数据库字段名。
/// </remarks>
public class QueryCondition
{
    /// <summary>
    /// 获取或设置用于该字段的查询操作符（例如：等于、大于、Like、Between）。
    /// </summary>
    public 查询操作符 操作符 { get; set; }
    /// <summary>
    /// 获取或设置查询值。
    /// </summary>
    /// <value>
    /// 当操作符为“等于”、“大于”等单值操作时，该属性用于传入比较值。
    /// 当操作符为“Between”（区间）时，该属性用于传入区间的起始值。
    /// </value>
    public object? 值1 { get; set; }
    /// <summary>
    /// 获取或设置区间的结束值。
    /// </summary>
    /// <value>
    /// 仅当 <see cref="操作符"/> 设置为“Between”等区间操作时使用，表示区间的结束值。
    /// </value>
    public object? 值2 { get; set; } // 区间用第二个值
}


public class QueryCriteria
{
    // 1. 【AND 区域】所有的 Key 必须同时满足 (原有逻辑)
    // 例如: CustomerCode = "C001" AND Status = "Active"
    public Dictionary<string, QueryCondition> Items { get; set; } = [];
    // 2. 【OR 区域】列表中的每一项代表一个"OR分组"
    // 逻辑: (Main AND Main) AND (Group1_A OR Group1_B) AND (Group2_C OR Group2_D)
    public List<Dictionary<string, QueryCondition>> OrGroups { get; set; } = [];


    // 方便添加条件的辅助方法
    public void Add(string field, QueryCondition condition)
    {
        if (!Items.TryAdd(field, condition)) Items[field] = condition;
    }

    // 快捷添加方法 (语法糖)
    public void Add(string field, 查询操作符 op, object v1, object? v2 = null)
    {
        this.Add(field, new QueryCondition { 操作符 = op, 值1 = v1, 值2 = v2! });
    }

    /// <summary>
    /// 添加一组 OR 条件。这组条件内部是用 OR 连接，但这组条件整体与外部是用 AND 连接。
    /// 场景：搜索框同时搜 SKU 或 描述。
    /// </summary>
    public void AddOrGroup(params (string field, 查询操作符 op, object value)[] conditions)
    {
        var groupDict = new Dictionary<string, QueryCondition>();

        foreach (var (field, op, val) in conditions)
        {
            // 注意：同一个 OR 分组内字段名也不能重复(这是字典限制)，但在搜索不同列时没问题
            groupDict[field] = new QueryCondition { 操作符 = op, 值1 = val };
        }

        OrGroups.Add(groupDict);
    }





}
