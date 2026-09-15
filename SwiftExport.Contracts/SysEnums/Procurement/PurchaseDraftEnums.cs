namespace SwiftExport.Contracts.SysEnums
{
    /// <summary>
    /// 转采购草稿入口。三个入口只影响选量规则，不改变采购分组和追溯规则。
    /// </summary>
    public enum PurchaseDraftEntryModeEnum
    {
        [System.ComponentModel.Description("手工选择")]
        ManualSelection = 0,
        [System.ComponentModel.Description("来源一键转采购")]
        SourceOneClick = 1,
        [System.ComponentModel.Description("采购工作台")]
        PurchaseWorkspace = 2
    }

    /// <summary>采购草稿预览行的组织方式。</summary>
    public enum PurchaseDraftLineModeEnum
    {
        [System.ComponentModel.Description("保留来源行")]
        KeepSourceLines = 0,
        [System.ComponentModel.Description("合并兼容行")]
        MergeCompatibleLines = 1
    }

    /// <summary>采购草稿写入目标。来源一键入口只允许 CreateNew。</summary>
    public enum PurchaseDraftTargetModeEnum
    {
        [System.ComponentModel.Description("新建采购单")]
        CreateNew = 0,
        [System.ComponentModel.Description("追加到现有采购单")]
        AppendExisting = 1
    }
}
