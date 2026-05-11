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

/// <summary>
/// ★ 新增：将 字段名 和 条件 绑定在一起的实体，代替 Dictionary 的 KeyValuePair
/// </summary>
public class QueryItem(string field, QueryCondition condition)
{
    public string Field { get; set; } = field;
    public QueryCondition Condition { get; set; } = condition;
}

public class QueryCriteria
{
    /// <summary>
    /// And区域条件
    /// </summary>
    public List<QueryItem> Items { get; set; } = [];

    // 2. 【OR 区域】换成 List<List<QueryItem>>！同理，解决 OR 组内的字段冲突！ 
    // 逻辑: (Main AND Main) AND (Group1_A OR Group1_B) AND (Group2_C OR Group2_D)
    /// <summary>
    /// Or区域条件
    /// </summary>
    public List<List<QueryItem>> OrGroups { get; set; } = [];


    /// <summary>
    /// 快捷添加条件的方法
    /// </summary>
    /// <param name="field">字段名</param>
    /// <param name="condition">条件对象</param>
    public void Add(string field, QueryCondition condition)
    {
        // 直接往 List 塞，绝对不会覆盖！
        Items.Add(new QueryItem(field, condition));
    }

    /// <summary>
    /// 快捷添加条件的语法糖方法，简化调用者的代码。
    /// </summary>
    /// <param name="field">字段名</param>
    /// <param name="op">查询操作符</param>
    /// <param name="v1">值1</param>
    /// <param name="v2">值2(如果有Between条件,否则留空)</param>
    public void Add(string field, 查询操作符 op, object v1, object? v2 = null)
    {
        this.Add(field, new QueryCondition { 操作符 = op, 值1 = v1, 值2 = v2 });
    }

    /// <summary>
    /// 添加一组 OR 条件。
    /// 场景：搜索框同时搜 SKU 或 描述，甚至 (SKU包含A OR SKU包含B)
    /// </summary>
    public void AddOrGroup(params (string field, 查询操作符 op, object value)[] conditions)
    {
        var groupList = new List<QueryItem>();

        foreach (var (field, op, val) in conditions)
        {
            // 直接往组 List 里塞，即使 field 重名也完美支持！
            groupList.Add(new QueryItem(field, new QueryCondition { 操作符 = op, 值1 = val }));
        }

        OrGroups.Add(groupList);
    }





}
