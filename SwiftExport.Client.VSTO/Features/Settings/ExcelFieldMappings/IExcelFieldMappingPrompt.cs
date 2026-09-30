namespace SwiftExport.Client.VSTO.Features.Settings.ExcelFieldMappings
{
    /// <summary>分组切换前未保存修改的处理决定。</summary>
    public enum UnsavedMappingChoice { Save, Discard, Cancel }

    /// <summary>把确认界面隔离在 ViewModel 外，便于纯托管测试。</summary>
    public interface IExcelFieldMappingPrompt
    {
        /// <summary>返回保存、放弃或取消切换的选择。</summary>
        UnsavedMappingChoice ResolveUnsavedChanges();
    }
}
