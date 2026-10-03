using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using SwiftExport.Client.VSTO.Features.Procurement.PurchaseOrders.Views;
using SwiftExport.Client.VSTO.Features.Procurement.PurchaseOrders.Models;
using SwiftExport.Client.VSTO.Testing;
namespace SwiftExport.Client.VSTO.WpfTests
{
    /// <summary>采购实际 STA 页面回归；入口名称保留以兼容已有测试调用。</summary>
    internal static class PurchasePreviewChecks
    {
        /// <summary>验证编辑绑定、标准非标单位、日期、多选、候选和保存门禁。</summary>
        internal static void Run(Action<bool,string> check)
        {
            using(var fixture=new PurchaseImportTestFixture())
            {
                var model=fixture.Model;
                var view=new PurchaseOrderImportView(model);
                var window=new Window { Style=null,Width=1280,Height=820,Content=view,ShowInTaskbar=false };
                fixture.Base.Dispatch=action=>window.Dispatcher.CheckAccess() ? Immediate(action) : window.Dispatcher.InvokeAsync(action).Task;
                Wait(fixture.Prepare()); window.Show();
                try
                {
                    view.UpdateLayout(); Pump();
                    var grid=(DataGrid)view.FindName("OrderLinesGrid");
                    check(ReferenceEquals(view.DataContext,model) && ReferenceEquals(grid.ItemsSource,model.Lines),"采购页绑定真实通知模型和明细");
                    check(!grid.IsReadOnly && grid.SelectionMode==DataGridSelectionMode.Extended && !grid.CanUserAddRows,"原生 DataGrid 可编辑、多选且禁止空白自动行");
                    check(grid.ActualWidth>0 && grid.ActualWidth<window.ActualWidth,"表格横向滚动不会把窗体撑宽");
                    check(grid.Columns.Any(c=>(string)c.Header=="Excel本司型号") && grid.Columns.Any(c=>(string)c.Header=="系统SKU")
                        && grid.Columns.Any(c=>(string)c.Header=="含税单价（可空）"),"原表型号、系统身份及报价列完整");
                    var headerBoxes=Descendants<TextBox>(view).ToArray();
                    var bo=headerBoxes.Single(b=>b.GetBindingExpression(TextBox.TextProperty)?.ParentBinding.Path?.Path=="Header.BoNo");
                    bo.Text="BO-EDIT"; bo.GetBindingExpression(TextBox.TextProperty).UpdateSource();
                    check(model.Header.BoNo=="BO-EDIT","单头编辑双向回写");
                    var remark=headerBoxes.Single(b=>b.GetBindingExpression(TextBox.TextProperty)?.ParentBinding.Path?.Path=="Header.Remark");
                    remark.Text="新的特殊要求"; remark.GetBindingExpression(TextBox.TextProperty).UpdateSource();
                    check(model.Header.Remark=="新的特殊要求","特殊要求绑定 Remark");
                    check(headerBoxes.Any(b=>b.GetBindingExpression(TextBox.TextProperty)?.ParentBinding.Path?.Path=="Header.DiscountRateText")
                        && headerBoxes.Any(b=>b.GetBindingExpression(TextBox.TextProperty)?.ParentBinding.Path?.Path=="Header.DiscountAmountText"),"优惠倍率和整单优惠金额有真实绑定");
                    var date=Descendants<DatePicker>(view).Single(d=>d.GetBindingExpression(DatePicker.SelectedDateProperty)?.ParentBinding.Path?.Path=="Header.DeliveryDate");
                    date.SelectedDate=new DateTime(2026,12,1); check(model.Header.DeliveryDate==new DateTime(2026,12,1),"单头日期双向回写");
                    var line=model.Lines[0]; check(!line.CanEditUnit,"标准单位禁止手工伪造");
                    model.AddNonStandardLineCommand.Execute(null); var nonstandard=model.Lines[1];
                    check(nonstandard.CanEditUnit && nonstandard.PlannedDeliveryDate==model.Header.DeliveryDate,"非标单位允许编辑，新行复制当前交期");
                    model.Header.DeliveryDate=new DateTime(2026,12,2); check(nonstandard.PlannedDeliveryDate==new DateTime(2026,12,1),"后续单头交期不覆盖行");
                    var unitColumn=(DataGridTemplateColumn)grid.Columns.Single(c=>(string)c.Header=="单位");
                    var unitEditor=(ComboBox)unitColumn.CellEditingTemplate.LoadContent(); unitEditor.DataContext=line; Pump();
                    check(!unitEditor.IsEnabled,"实际标准单位编辑模板禁用输入");
                    unitEditor.DataContext=nonstandard; Pump(); check(unitEditor.IsEnabled,"实际非标单位编辑模板开放输入");
                    grid.SelectedItems.Add(line); grid.SelectedItems.Add(nonstandard); Pump();
                    check(model.DeleteSelectedLineCommand.CanExecute(null),"视图多选桥接删除命令");
                    model.DeleteSelectedLineCommand.Execute(null); check(model.Lines.Count==0,"多选删除全部目标行");
                    model.AddStandardLineCommand.Execute(null); line=model.Lines[0]; line.CustomerSku="AQ1"; line.QuantityText="1"; line.UnitPriceText="2";
                    Wait(model.CheckImportAsync()); Pump(); check(model.CanSaveDraft && line.Sku=="REAL-SKU","匹配后系统 SKU 及保存资格回显");
                    line.UnitPriceText="bad"; Pump(); check(!model.CanSaveDraft && model.ValidationSummary.Contains("不完整"),"非法价格立即阻断保存并提示统计不完整");
                    fixture.Base.Confirm=false; check(!view.CanClose(),"取消普通关闭保留内存编辑");
                    CheckConfigurationGroups(fixture,check);
                }
                finally { window.Close(); }
            }
        }
        // 独立配置页面的真实分组下拉须和模型一致，取消不能留下界面假选择。
        private static void CheckConfigurationGroups(PurchaseImportTestFixture fixture,Action<bool,string> check)
        {
            using(var model=new SwiftExport.Client.VSTO.Features.Settings.ExcelReadProfiles.ViewModels.ExcelReadProfileViewModel(
                fixture.Base.ProfileService,fixture.Base.AddressReader,SwiftExport.Client.VSTO.Services.ExcelImport.ExcelImportRegistry.CreateDefault(),
                fixture.Base.Session,fixture.Base,new SwiftExport.Client.VSTO.Api.ApiCallRunner(new SwiftExport.Client.VSTO.Diagnostics.AddInLog()),fixture.Base))
            {
                Wait(model.SelectGroupAsync("PurchaseOrder"));
                var view=new SwiftExport.Client.VSTO.Features.Settings.ExcelReadProfiles.Views.ExcelReadProfileView(model);
                var window=new Window { Style=null,Width=560,Height=800,Content=view,ShowInTaskbar=false }; window.Show();
                try
                {
                    Pump(); var selector=(ComboBox)view.FindName("GroupSelector");
                    check(selector.Items.Count==3 && (string)selector.SelectedValue=="PurchaseOrder","配置实际下拉显示三个单据组并定位采购");
                    check(model.HeaderFields.Any(f=>f.FieldKey=="Header.BoNo") && !model.HeaderFields.Any(f=>f.FieldKey=="Header.InvoiceNo"),"采购配置目录没有混入销售字段");
                    model.ConfigName="未保存采购配置"; fixture.Base.Confirm=false; selector.SelectedValue="SalesOrder"; Pump();
                    check(model.CurrentGroupCode=="PurchaseOrder" && (string)selector.SelectedValue=="PurchaseOrder" && model.ConfigName=="未保存采购配置","取消业务组切换同时恢复下拉与编辑");
                }
                finally { window.Close(); }
            }
        }
        private static Task Immediate(Action action) { action(); return Task.CompletedTask; }
        private static void Wait(Task task)
        {
            while(!task.IsCompleted) Pump();
            task.GetAwaiter().GetResult();
        }
        private static void Pump() { var frame=new DispatcherFrame(); Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>frame.Continue=false)); Dispatcher.PushFrame(frame); }
        private static IEnumerable<T> Descendants<T>(DependencyObject root) where T:DependencyObject
        {
            for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
            {
                var child=VisualTreeHelper.GetChild(root,i); if(child is T match) yield return match;
                foreach(var nested in Descendants<T>(child)) yield return nested;
            }
        }
    }
}
