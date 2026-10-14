using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.Wpf.Tests.Controls;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Features.Products.Components;
using SwiftExportClient.Features.Products.Components.Models;
using SwiftExportClient.Features.Products.ProductList.Views;
using SwiftExportClient.Resources.Controls.Filter;
using SwiftExportClient.Resources.Controls;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;

namespace SwiftExport.Client.Wpf.Tests.Features.Products;

/// <summary>用真实 STA 物料详情验证原生数值提交、公共标签交互、只读来源与视图释放。</summary>
[TestClass]
[DoNotParallelize]
public sealed class ProductDetailViewInteractionTests
{
    /// <summary>详情没有任何中性候选时仍能保存任意型号，关闭候选和失焦均不丢文本。</summary>
    [TestMethod]
    public void FreeModelText_ShouldSaveWithoutCandidatesAndKeepOtherFields() => DatePickerAssistTests.RunWithApplication(async () =>
    {
        LoadResources();
        var detail = ProductDetailViewModelTests.Detail();
        detail.Data.LinkSku = "物料原型号";
        SaveProductDetailRequest? sent = null;
        using var vm = ProductDetailViewModelTests.Create(detail, null, save: request =>
        { sent = request; return Task.FromResult(ServiceResult<SaveProductDetailResponse>.Fail("测试保留草稿")); });
        await ProductDetailViewModelTests.Open(vm, detail.Uid);
        var view = new ProductDetailView { DataContext = vm };
        var window = new Window { Content = view, Width = 1480, Height = 900, ShowInTaskbar = false };
        try
        {
            window.Show(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var control = Descendants(view).OfType<ProductNeutralLookupTextBox>().Single();
            var editor = (LookupTextBox)control.FindName("LookupEditor");
            Assert.IsFalse(editor.RequireItemMatch);
            Assert.IsTrue(editor.ClearSelectionOnEdit);
            Assert.AreEqual(0, vm.ProductNeutralCandidates.Count);
            editor.FocusEditor();
            var input = (TextBox)editor.FindName("InputTextBox");
            input.Text = "非候选/独立物料型号";
            await Dispatcher.Yield(DispatcherPriority.DataBind);
            Descendants(view).OfType<TextBox>().Single(text => AutomationProperties.GetName(text) == "备注").Focus();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.AreEqual("非候选/独立物料型号", vm.Header.LinkSku);
            Assert.AreEqual(detail.Data.ProductMasterId, vm.Header.ProductMasterId);
            Assert.AreEqual(detail.Data.ColorId, vm.Header.ColorId);
            await vm.SaveCommand.ExecuteAsync(null);
            Assert.IsNotNull(sent);
            Assert.AreEqual("非候选/独立物料型号", sent.Data.LinkSku);
            Assert.AreEqual(detail.Data.ProductMasterId, sent.Data.ProductMasterId);
            Assert.IsTrue(vm.HasUnsavedChanges);
        }
        finally { window.Close(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); }
    });

    /// <summary>真实编辑器保存主型号 Uid 与自身型号文本，编码建议和清空不改变其他资料。</summary>
    [TestMethod]
    public void SourceLookups_ShouldCommitIndependentIdentities() => DatePickerAssistTests.RunWithApplication(async () =>
    {
        LoadResources();
        var detail = ProductDetailViewModelTests.Detail();
        detail.Data.ColorId = Guid.NewGuid();
        using var vm = ProductDetailViewModelTests.Create(detail, ProductMasterTagSelectionTests.Definition());
        await ProductDetailViewModelTests.Open(vm, detail.Uid);
        var view = new ProductDetailView { DataContext = vm };
        var window = new Window { Content = view, Width = 1480, Height = 900, ShowInTaskbar = false };
        try
        {
            window.Show(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.IsFalse(vm.HasUnsavedChanges);
            var masterControl = Descendants(view).OfType<ProductMasterLookupTextBox>().Single();
            var masterLookup = (LookupTextBox)masterControl.FindName("LookupEditor");
            var neutralControl = Descendants(view).OfType<ProductNeutralLookupTextBox>().Single();
            var neutralLookup = (LookupTextBox)neutralControl.FindName("LookupEditor");
            var masterId = Guid.NewGuid(); var neutralId = Guid.NewGuid();
            var master = new ProductMasterLookupItemModel(new() { Uid = masterId, Data = new() { MasterSku = "主型号候选", ProductAttributeProfileId = detail.ProductAttributeProfileId } });
            var neutral = new ProductNeutralLookupItemModel(new() { Uid = neutralId, Data = new() { ProductMasterId = Guid.NewGuid(), ProductCode = "独立中性型号" } });
            masterLookup.CommitExternalSelection(master, LookupCommitReason.Mouse);
            neutralLookup.CommitExternalSelection(neutral, LookupCommitReason.Mouse);
            await Dispatcher.Yield(DispatcherPriority.DataBind);
            Assert.AreEqual(masterId, vm.Header.ProductMasterId);
            Assert.AreEqual("独立中性型号", vm.Header.LinkSku);
            Assert.AreEqual(detail.Data.ColorId, vm.Header.ColorId);
            var clear = (Button)neutralLookup.FindName("BtnClear");
            ((IInvokeProvider)new ButtonAutomationPeer(clear).GetPattern(PatternInterface.Invoke)!).Invoke();
            // Automation Invoke 按 Input 优先级排队，等待到 ApplicationIdle 才能断言点击及绑定已完成。
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.AreEqual(string.Empty, vm.Header.LinkSku);
            Assert.AreEqual(masterId, vm.Header.ProductMasterId);
        }
        finally { window.Close(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); }
    });

    /// <summary>失效主型号有明确清除入口，页面加载或清除引用不改变物料自身型号。</summary>
    [TestMethod]
    public void MissingSourceReferences_ShouldHaveExplicitClearButtons() => DatePickerAssistTests.RunWithApplication(async () =>
    {
        LoadResources();
        var detail = ProductDetailViewModelTests.Detail();
        detail.MasterSku = null; detail.Data.LinkSku = "自由物料型号";
        using var vm = ProductDetailViewModelTests.Create(detail, null);
        await ProductDetailViewModelTests.Open(vm, detail.Uid);
        var view = new ProductDetailView { DataContext = vm };
        var window = new Window { Content = view, Width = 1480, Height = 900, ShowInTaskbar = false };
        try
        {
            window.Show(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.IsFalse(vm.HasUnsavedChanges);
            var masterClear = Descendants(view).OfType<Button>().Single(control => AutomationProperties.GetName(control) == "清除失效主型号引用");
            Assert.AreEqual(Visibility.Visible, masterClear.Visibility);
            ((IInvokeProvider)new ButtonAutomationPeer(masterClear).GetPattern(PatternInterface.Invoke)!).Invoke();
            // DataBind 优先级早于 Input，不能把等待绑定当作等待真实按钮点击。
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.IsNull(vm.Header.ProductMasterId);
            Assert.AreEqual("自由物料型号", vm.Header.LinkSku);
        }
        finally { window.Close(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); }
    });

    /// <summary>未知旧单位保留，候选更新不产生假脏状态，只有真实选择确认和清除才写入代码。</summary>
    [TestMethod]
    public void UnitLookup_ShouldPreserveLegacyUntilExplicitCommitOrClear() => DatePickerAssistTests.RunWithApplication(async () =>
    {
        LoadResources();
        var detail = ProductDetailViewModelTests.Detail();
        detail.Data.Unit = "LEGACY-UNIT";
        IReadOnlyList<SysDictDataDto> units = [];
        SaveProductDetailRequest? sent = null;
        using var vm = ProductDetailViewModelTests.Create(detail, null, units: () => units, save: request =>
        { sent = request; return Task.FromResult(ServiceResult<SaveProductDetailResponse>.Fail("测试保留草稿")); });
        await ProductDetailViewModelTests.Open(vm, detail.Uid);
        var view = new ProductDetailView { DataContext = vm };
        var window = new Window { Content = view, Width = 1480, Height = 900, ShowInTaskbar = false };
        try
        {
            window.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var lookup = Descendants(view).OfType<LookupTextBox>().Single(control => AutomationProperties.GetName(control) == "单位");
            Assert.AreEqual("LEGACY-UNIT", lookup.Text);
            Assert.IsFalse(vm.HasUnsavedChanges);
            var candidate = new SysDictDataDto { Uid = Guid.NewGuid(), DictCode = DictCodes.Unit, DataNameEn = "PCS", DataNameZh = "件", IsEnabled = true };
            units = [candidate];
            await vm.RefreshProductAttributesCommand.ExecuteAsync(null);
            await Dispatcher.Yield(DispatcherPriority.DataBind);
            Assert.AreEqual("LEGACY-UNIT", vm.Header.Unit);
            Assert.AreEqual("LEGACY-UNIT", lookup.Text);
            Assert.IsFalse(vm.HasUnsavedChanges);
            lookup.CommitExternalSelection(candidate, LookupCommitReason.Mouse);
            await Dispatcher.Yield(DispatcherPriority.DataBind);
            Assert.AreEqual("PCS", vm.Header.Unit);
            Assert.IsTrue(vm.Header.IsDirty);
            var clear = (Button)lookup.FindName("BtnClear");
            ((IInvokeProvider)new ButtonAutomationPeer(clear).GetPattern(PatternInterface.Invoke)!).Invoke();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.AreEqual(string.Empty, vm.Header.Unit);
            Assert.IsNull(vm.SelectedUnit);
            await vm.SaveCommand.ExecuteAsync(null);
            Assert.IsNotNull(sent);
            Assert.AreEqual(string.Empty, sent.Data.Unit);
        }
        finally { window.Close(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); }
    });

    /// <summary>无效数值阻止保存并保留文本；纠正和清空按实际绑定文化提交，卸载解除编辑桥接。</summary>
    [TestMethod]
    [DataRow("zh-HK", "1.25")]
    [DataRow("de-DE", "1,25")]
    public void NumericEditors_ShouldValidateCommitAndClear(string culture, string numeric) => DatePickerAssistTests.RunWithApplication(async () =>
    {
        LoadResources();
        var detail = ProductDetailViewModelTests.Detail();
        SaveProductDetailRequest? sent = null;
        using var vm = ProductDetailViewModelTests.Create(detail, null, save: request =>
        { sent = request; return Task.FromResult(ServiceResult<SaveProductDetailResponse>.Fail("测试保留草稿")); });
        await ProductDetailViewModelTests.Open(vm, detail.Uid);
        var view = new ProductDetailView { DataContext = vm, Language = XmlLanguage.GetLanguage(culture) };
        var window = new Window { Content = view, Width = 1480, Height = 900, ShowInTaskbar = false };
        try
        {
            window.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.IsNotNull(vm.CommitPendingEdits);
            var weight = Descendants(view).OfType<TextBox>().Single(control => AutomationProperties.GetName(control) == "单品重量 (KG)");
            var volume = Descendants(view).OfType<TextBox>().Single(control => AutomationProperties.GetName(control) == "单品体积 (CBM)");
            weight.Language = XmlLanguage.GetLanguage(culture);
            vm.Header.Note = "字段草稿";
            weight.SetCurrentValue(TextBox.TextProperty, "非法数值");
            Assert.IsFalse(vm.CommitPendingEdits());
            Assert.IsTrue(Validation.GetHasError(weight));
            await vm.SaveCommand.ExecuteAsync(null);
            Assert.IsNull(sent);
            Assert.AreEqual("非法数值", weight.Text);
            Assert.AreEqual(0.5m, vm.Header.UnitWeight);
            weight.SetCurrentValue(TextBox.TextProperty, numeric);
            volume.SetCurrentValue(TextBox.TextProperty, string.Empty);
            Assert.IsTrue(vm.CommitPendingEdits());
            Assert.AreEqual(1.25m, vm.Header.UnitWeight);
            Assert.IsNull(vm.Header.UnitCbm);
            await vm.SaveCommand.ExecuteAsync(null);
            Assert.IsNotNull(sent);
            Assert.AreEqual(1.25m, sent.Data.UnitWeight);
            Assert.IsNull(sent.Data.UnitCBM);
            weight.SetCurrentValue(TextBox.TextProperty, "仍是非法");
            vm.RejectDetailChangesCommand.Execute(null);
            Assert.AreEqual(0.5m, vm.Header.UnitWeight);
            Assert.AreNotEqual("仍是非法", weight.Text);
            Assert.IsFalse(vm.HasUnsavedChanges);
        }
        finally
        {
            window.Close();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.IsNull(vm.CommitPendingEdits);
            Assert.IsNull(vm.RefreshEditorTargets);
        }
    });

    /// <summary>详情直接消费物料私有工作区，没有模板选择入口；多选关闭取消、确定更新自己的值。</summary>
    [TestMethod]
    public void AttributeEditor_ShouldUseOwnValuesAndKeepTemplateReadOnly() => DatePickerAssistTests.RunWithApplication(async () =>
    {
        LoadResources();
        var definition = ProductMasterTagSelectionTests.Definition();
        var detail = ProductDetailViewModelTests.Detail();
        detail.SelectedOptionIds = [definition.Options[0].Uid];
        using var vm = ProductDetailViewModelTests.Create(detail, definition);
        await ProductDetailViewModelTests.Open(vm, detail.Uid);
        var view = new ProductDetailView { DataContext = vm };
        var window = new Window { Content = view, Width = 1480, Height = 900, ShowInTaskbar = false };
        try
        {
            window.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var editor = Descendants(view).OfType<ProductAttributeEditor>().Single();
            Assert.AreSame(vm.Tags, editor.Workspace);
            Assert.AreEqual(0, Descendants(view).OfType<ProductAttributeProfileLookupTextBox>().Count());
            var source = Descendants(view).OfType<TextBlock>().Single(control => AutomationProperties.GetName(control) == "只读物料属性模板");
            Assert.AreEqual("面盆龙头模板", source.Text);
            var multi = Descendants(view).OfType<MultiSelectLookupButton>().Single();
            var popup = (Popup)multi.FindName("PART_Popup");
            popup.IsOpen = true;
            await Dispatcher.Yield(DispatcherPriority.Loaded);
            var list = (ListBox)multi.FindName("PART_ListBox");
            list.UpdateLayout();
            var row = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(1);
            Descendants(row).OfType<CheckBox>().Single().SetCurrentValue(ToggleButton.IsCheckedProperty, true);
            await Dispatcher.Yield(DispatcherPriority.DataBind);
            popup.IsOpen = false;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.IsFalse(vm.Tags.IsDirty);
            CollectionAssert.AreEqual(new[] { definition.Options[0].Uid }, vm.Tags.GetSelectedOptionIds()!);
            popup.IsOpen = true;
            await Dispatcher.Yield(DispatcherPriority.Loaded);
            list.UpdateLayout();
            row = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(1);
            Descendants(row).OfType<CheckBox>().Single().SetCurrentValue(ToggleButton.IsCheckedProperty, true);
            await Dispatcher.Yield(DispatcherPriority.DataBind);
            var confirm = Descendants(popup.Child).OfType<Button>().Single(button => Equals(button.Content, "确定"));
            ((IInvokeProvider)new ButtonAutomationPeer(confirm).GetPattern(PatternInterface.Invoke)!).Invoke();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.IsTrue(vm.Tags.IsDirty);
            CollectionAssert.AreEquivalent(definition.Options.Select(option => option.Uid).ToArray(), vm.Tags.GetSelectedOptionIds()!);
            Assert.AreEqual(detail.ProductAttributeProfileId, vm.Source!.ProductAttributeProfileId);
            Assert.IsFalse(vm.Header.IsDirty);
        }
        finally { window.Close(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); }
    });

    /// <summary>加载产品实际命名资源，真实控件遵循当前主题及图片样式。</summary>
    private static void LoadResources()
    {
        Application.Current.Resources.MergedDictionaries.Add(new global::Wpf.Ui.Markup.ThemesDictionary());
        Application.Current.Resources.MergedDictionaries.Add(new global::Wpf.Ui.Markup.ControlsDictionary());
        foreach (var file in new[] { "UserWpfUiThemeResources.xaml", "Variables.xaml", "Colors.xaml", "Typography.xaml", "ListBox.xaml", "Image.xaml" })
            Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"pack://application:,,,/SwiftExport.Client.Wpf;component/Resources/{file}") });
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i); yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
