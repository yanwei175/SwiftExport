using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExport.Contracts.DTOs.Products.ProductMasters;
using SwiftExportClient.Features.Products.Components.Models;
using SwiftExportClient.Features.Products.ProductList.Models;
using SwiftExportClient.Mvvm;

namespace SwiftExport.Client.Wpf.Tests.Features.Products;

/// <summary>验证父级默认装率的可空校验、映射、编辑回滚和 WPF 空文本绑定。</summary>
[TestClass]
public sealed class ProductMasterNullablePcsTests
{
    /// <summary>两个普通编辑模型都允许空值和正整数，并阻断零及负数。</summary>
    [TestMethod]
    [DataRow(null, true)]
    [DataRow(1, true)]
    [DataRow(12, true)]
    [DataRow(0, false)]
    [DataRow(-1, false)]
    public void Validation_ShouldAcceptOnlyUnsetOrPositivePcs(int? pcs, bool valid)
    {
        var line = new ProductMasterLineModel { MasterSku = "MASTER", DefaultPcsPerPackage = pcs };
        var detail = new ProductMasterEditModel { MasterSku = "MASTER", DefaultPcsPerPackage = pcs };
        Assert.AreEqual(valid, line.ValidateRow() is null);
        Assert.AreEqual(valid, detail.ValidateRow() is null);
    }

    /// <summary>普通新建模型保留初始装率 1，传输模型不把未提供的装率伪装为默认值。</summary>
    [TestMethod]
    public void NewModels_ShouldKeepUiDefaultWithoutInventingDtoValue()
    {
        Assert.AreEqual(1, new ProductMasterLineModel().DefaultPcsPerPackage);
        Assert.AreEqual(1, new ProductMasterEditModel().DefaultPcsPerPackage);
        Assert.AreEqual(1, new ProductMasterListModel().AddNewMaster().DefaultPcsPerPackage);
        Assert.IsNull(new ProductMasterWriteDto().DefaultPcsPerPackage);
    }

    /// <summary>列表加载和复用已有行必须接纳持久化空值；构造请求及回滚后仍为空。</summary>
    [TestMethod]
    public void ListMapping_ShouldPreserveNullThroughReloadWriteAndRollback()
    {
        var line = new ProductMasterLineModel();
        var dto = new ProductMasterListItemDto
        {
            Uid = Guid.NewGuid(), Version = 1,
            Data = new ProductMasterWriteDto { MasterSku = "MASTER", DefaultPcsPerPackage = null }
        };
        line.Load(dto);
        Assert.IsNull(line.DefaultPcsPerPackage);
        Assert.IsNull(line.ToWriteDto().DefaultPcsPerPackage);
        Assert.IsNull(line.ToDto().Data.DefaultPcsPerPackage);

        line.DefaultPcsPerPackage = 6;
        Assert.AreEqual(LineState.Modified, line.State);
        line.RejectChanges();
        Assert.IsNull(line.DefaultPcsPerPackage);
        Assert.AreEqual(LineState.Unchanged, line.State);

        line.DefaultPcsPerPackage = 12;
        line.Load(dto);
        Assert.IsNull(line.DefaultPcsPerPackage);
        line.DefaultPcsPerPackage = 2;
        line.AcceptChanges();
        line.DefaultPcsPerPackage = null;
        Assert.IsNull(line.ToWriteDto().DefaultPcsPerPackage);
        Assert.AreEqual(LineState.Modified, line.State);
        line.RejectChanges();
        Assert.AreEqual(2, line.DefaultPcsPerPackage);
    }

    /// <summary>详情头部空装率必须覆盖模型初始值及旧值，并在回滚和写 DTO 时保持为空。</summary>
    [TestMethod]
    public void DetailMapping_ShouldPreserveNullThroughReloadWriteAndRollback()
    {
        var model = new ProductMasterEditModel();
        var dto = new ProductMasterDetailDto
        {
            Uid = Guid.NewGuid(), Version = 1,
            Master = new ProductMasterWriteDto { MasterSku = "MASTER", DefaultPcsPerPackage = null }
        };
        model.Load(dto);
        Assert.IsNull(model.DefaultPcsPerPackage);
        Assert.IsNull(model.ToWriteDto().DefaultPcsPerPackage);
        Assert.IsNull(model.ToDto().Master.DefaultPcsPerPackage);
        model.DefaultPcsPerPackage = 6;
        model.RejectChanges();
        Assert.IsNull(model.DefaultPcsPerPackage);
        Assert.AreEqual(LineState.Unchanged, model.State);
        model.DefaultPcsPerPackage = 12;
        model.Load(dto);
        Assert.IsNull(model.DefaultPcsPerPackage);
    }

    /// <summary>候选字段原样反映已有空值和缺失嵌套数据，不使用 1 兜底。</summary>
    [TestMethod]
    public void Lookup_ShouldPreserveUnsetPcs()
    {
        Assert.IsNull(new ProductMasterLookupItemModel(new ProductMasterListItemDto
        {
            Data = new ProductMasterWriteDto { DefaultPcsPerPackage = null }
        }).DefaultPcsPerPackage);
        Assert.IsNull(new ProductMasterLookupItemModel(new ProductMasterListItemDto { Data = null! }).DefaultPcsPerPackage);
    }

    /// <summary>显式空值展示的真实 WPF 双向绑定允许清空装率；小数输入产生转换错误并保留原模型值。</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void TextBinding_ShouldCommitBlankAsNullAndRejectFractionalPcs(bool numberBox) => StaTest.RunAsync(() =>
    {
        var model = new ProductMasterEditModel { MasterSku = "MASTER", DefaultPcsPerPackage = 6 };
        TextBox editor = numberBox ? new global::Wpf.Ui.Controls.NumberBox() : new TextBox();
        BindingOperations.SetBinding(editor, TextBox.TextProperty, new Binding(nameof(model.DefaultPcsPerPackage))
        {
            Source = model, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.Explicit,
            TargetNullValue = string.Empty, ValidatesOnExceptions = true
        });
        editor.Text = string.Empty;
        editor.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
        Assert.IsNull(model.DefaultPcsPerPackage);
        Assert.IsFalse(Validation.GetHasError(editor));

        editor.Text = "3.5";
        editor.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
        Assert.IsNull(model.DefaultPcsPerPackage);
        Assert.IsTrue(Validation.GetHasError(editor));
        return Task.CompletedTask;
    });
}
