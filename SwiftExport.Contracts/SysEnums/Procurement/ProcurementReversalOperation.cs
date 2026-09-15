namespace SwiftExport.Contracts.SysEnums
{
    /// <summary>采购反向操作类型；共用资格预览，不混用命令或幂等回执。</summary>
    public enum ProcurementReversalOperation { CancelConfirmation = 0, DeleteLines = 1, Void = 2 }
}
