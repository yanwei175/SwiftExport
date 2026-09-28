using System.Windows.Controls;

namespace SwiftExportClient.Features.SystemSettings.Dictionaries.Views;

/// <summary>承载可复用的字典类型和值编辑界面。</summary>
public partial class DictionaryManagerView : UserControl
{
    /// <summary>创建字典管理内容视图。</summary>
    public DictionaryManagerView()
    {
        InitializeComponent();
    }

    /// <summary>提交当前单元格和行编辑，使窗口关闭前的未保存检查读取到最新输入。</summary>
    public bool CommitPendingEdits()
    {
        return DictionaryGrid.CommitEdit(DataGridEditingUnit.Cell, exitEditingMode: true)
            && DictionaryGrid.CommitEdit(DataGridEditingUnit.Row, exitEditingMode: true);
    }
}
