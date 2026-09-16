namespace SwiftExport.Core.DatabaseSchema;

/// <summary>声明实体表上的 SQL Server 索引，列名必须使用实体属性名。</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public sealed class DbIndexAttribute(string name, params string[] columns) : Attribute
{
    /// <summary>数据库索引名。</summary>
    public string Name { get; } = name;

    /// <summary>按顺序组成索引键的实体属性名。</summary>
    public IReadOnlyList<string> Columns { get; } = columns;

    /// <summary>是否建立唯一索引。</summary>
    public bool IsUnique { get; set; }

    /// <summary>过滤索引谓词；只能引用当前实体列。</summary>
    public string? Filter { get; set; }

    /// <summary>只作覆盖查询、不参与排序的实体属性名。</summary>
    public string[] IncludeColumns { get; set; } = [];
}

/// <summary>声明只依赖当前行的 SQL Server CHECK 约束。</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public sealed class DbCheckAttribute(string name, string expression) : Attribute
{
    /// <summary>数据库约束名。</summary>
    public string Name { get; } = name;

    /// <summary>使用数据库列名表达的 CHECK 谓词。</summary>
    public string Expression { get; } = expression;
}

/// <summary>声明固定目标实体的逻辑关系；生成器只记录和校验，不建立数据库外键。</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class LogicalReferenceAttribute(
    string localColumn,
    Type principalEntity,
    string principalColumn = "Uid") : Attribute
{
    /// <summary>当前实体中的关系属性名。</summary>
    public string LocalColumn { get; } = localColumn;

    /// <summary>被引用的持久化实体类型。</summary>
    public Type PrincipalEntity { get; } = principalEntity;

    /// <summary>被引用实体中的键属性名。</summary>
    public string PrincipalColumn { get; } = principalColumn;

    /// <summary>该逻辑关系是否为业务必选。</summary>
    public bool IsRequired { get; set; }
}

/// <summary>声明由类型判别列决定目标实体的多态逻辑关系。</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class PolymorphicLogicalReferenceAttribute(
    string discriminatorColumn,
    string discriminatorValue,
    string localColumn,
    Type principalEntity,
    string principalColumn = "Uid") : Attribute
{
    /// <summary>当前实体中的类型判别属性名。</summary>
    public string DiscriminatorColumn { get; } = discriminatorColumn;

    /// <summary>选择目标实体的稳定判别值。</summary>
    public string DiscriminatorValue { get; } = discriminatorValue;

    /// <summary>当前实体中的关系属性名。</summary>
    public string LocalColumn { get; } = localColumn;

    /// <summary>被引用的持久化实体类型。</summary>
    public Type PrincipalEntity { get; } = principalEntity;

    /// <summary>被引用实体中的键属性名。</summary>
    public string PrincipalColumn { get; } = principalColumn;
}

/// <summary>覆盖按 CLR 类型推断的数据库列类型或默认表达式。</summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class DbColumnAttribute : Attribute
{
    /// <summary>显式 SQL Server 列类型；为空时使用生成器的受控约定。</summary>
    public string? StoreType { get; set; }

    /// <summary>SQL Server 默认值表达式；为空时不建立默认约束。</summary>
    public string? DefaultExpression { get; set; }
}

/// <summary>声明由实体拥有的数据库序列。</summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class DbSequenceAttribute(string name) : Attribute
{
    /// <summary>序列名。</summary>
    public string Name { get; } = name;

    /// <summary>序列起始值。</summary>
    public long StartValue { get; set; } = 1;

    /// <summary>每次递增值。</summary>
    public int IncrementBy { get; set; } = 1;
}

/// <summary>声明实体事实只允许新增；生成器会建立统一的更新、删除阻断触发器。</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class DbAppendOnlyAttribute : Attribute
{
}

/// <summary>声明实体在成立后不可修改的身份列；跨表规则仍由 Application 校验。</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class DbImmutableColumnsAttribute(params string[] columns) : Attribute
{
    /// <summary>成立后不可修改的实体属性名。</summary>
    public IReadOnlyList<string> Columns { get; } = columns;
}
