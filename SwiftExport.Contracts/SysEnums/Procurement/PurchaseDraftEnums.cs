namespace SwiftExport.Contracts.SysEnums
{
    /// <summary>
    /// 转采购草稿入口。三个入口只影响选量规则，不改变采购分组和追溯规则。
    /// </summary>
    public enum PurchaseDraftEntryModeEnum
    {
        ManualSelection = 0,
        SourceOneClick = 1,
        PurchaseWorkspace = 2
    }

    /// <summary>采购草稿预览行的组织方式。</summary>
    public enum PurchaseDraftLineModeEnum
    {
        KeepSourceLines = 0,
        MergeCompatibleLines = 1
    }

    /// <summary>采购草稿写入目标。来源一键入口只允许 CreateNew。</summary>
    public enum PurchaseDraftTargetModeEnum
    {
        CreateNew = 0,
        AppendExisting = 1
    }
}
