using System;
using System.Windows.Forms;
using SwiftExport.Client.VSTO.Hosting.Windows;

namespace SwiftExport.Client.VSTO.Features.Settings.ExcelFieldMappings.Views
{
    /// <summary>在所属 Excel 窗口显示未保存修改确认。</summary>
    public sealed class ExcelFieldMappingPrompt : IExcelFieldMappingPrompt
    {
        private readonly ModelessWindowContext context;
        /// <summary>借用当前设置窗体的 Owner。</summary>
        public ExcelFieldMappingPrompt(ModelessWindowContext context) { this.context = context; }
        /// <inheritdoc />
        public UnsavedMappingChoice ResolveUnsavedChanges()
        {
            if (context.Handle == IntPtr.Zero) return UnsavedMappingChoice.Cancel;
            var result = MessageBox.Show(context, "当前分组有未保存的修改。是：保存后切换；否：放弃修改；取消：留在当前分组。",
                "字段映射设置", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button3);
            return result == DialogResult.Yes ? UnsavedMappingChoice.Save
                : result == DialogResult.No ? UnsavedMappingChoice.Discard : UnsavedMappingChoice.Cancel;
        }
    }
}
