using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Contracts.DTOs.QualityControl.FlowManagement
{
    /// <summary>整组保存成功后的头部回执与最新完整详情，用于获得服务器分配的行身份、快照和版本。</summary>
    public sealed class FlowSaveResponse
    {
        public SaveItemResult HeaderResult { get; set; }
        public FlowDetailDto Detail { get; set; }
    }
}

