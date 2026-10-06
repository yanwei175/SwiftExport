namespace SwiftExport.Core.Common;

/// <summary>数据库历史检查已明确发现入库反向证据不足；仅承接既有收货未知类别，不代表普通查询或结构失败。</summary>
public sealed class UnverifiedReceiptHistoryException : Exception
{
    /// <summary>保留现有入库历史门禁说明；由仓储完成明确检查后抛出，服务仍决定对外提示。</summary>
    public UnverifiedReceiptHistoryException()
        : base("入库反向历史不完整或存在旧负数反向单，净收货量尚未核实。")
    {
    }
}
