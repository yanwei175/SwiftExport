namespace SwiftExportClient.Mvvm;

/// <summary>描述相对业务行的拖放位置；末尾只用于没有目标行的平面列表空白区域。</summary>
public enum RowDropPosition
{
    /// <summary>插入目标行或业务块之前。</summary>
    Before,
    /// <summary>插入目标行或业务块之后。</summary>
    After,
    /// <summary>追加完整业务列表末尾。</summary>
    End,
}
