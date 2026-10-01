using CommunityToolkit.Mvvm.ComponentModel;
namespace SwiftExport.Client.VSTO.Features.Settings.ExcelReadProfiles.Models
{
    /// <summary>字段地址的页面编辑状态，实际业务值与参考预览不保存到服务器。</summary>
    public sealed class ExcelReadFieldModel : ObservableObject
    {
        private string address = "", preview = "";
        private bool downward;
        public string FieldKey { get; set; }
        public string DisplayName { get; set; }
        public bool IsHeader { get; set; }
        public bool IsKnown { get; set; } = true;
        public string CellAddress { get => address; set { if (SetProperty(ref address, value)) Preview = ""; } }
        public bool IsDownward { get => downward; set => SetProperty(ref downward, value); }
        public string Preview { get => preview; set => SetProperty(ref preview, value); }
    }
}
