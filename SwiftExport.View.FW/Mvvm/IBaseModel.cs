namespace SwiftExport.View.FW.Mvvm
{
    // 所有单表字典模型必须实现这个接口
    public interface IBaseModel
    {
        int Id { get; set; }
        ModelState State { get; set; }
        bool IsChecked { get; set; }
        bool IsValid { get; } // 配合 IDataErrorInfo 的校验状态
    }
}
