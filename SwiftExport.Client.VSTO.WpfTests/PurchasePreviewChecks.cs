using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
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
                    CheckPermanentHeader(view, grid, check);
                    check(view.Content is Grid && grid.ActualHeight > 0
                        && grid.TransformToAncestor(view).Transform(new Point(0, grid.ActualHeight)).Y <= view.ActualHeight,
                        "采购明细在有限窗体高度中显示，不依赖顶层滚动");
                    var previousHeight = grid.ActualHeight;
                    window.Height += 120; Pump(); view.UpdateLayout();
                    check(grid.ActualHeight > previousHeight + 30, "采购窗体增高时明细区域随之伸展");
                    window.Height -= 120; Pump(); view.UpdateLayout();
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
                    var rate = (TextBox)view.FindName("DiscountRateBox");
                    rate.Text = "bad"; rate.GetBindingExpression(TextBox.TextProperty).UpdateSource(); Pump();
                    check(model.ValidationSummary.Contains("优惠") && model.Header.DiscountRateText == "bad", "常驻优惠控件保留非法原文并反馈错误");
                    rate.Text = ""; rate.GetBindingExpression(TextBox.TextProperty).UpdateSource(); Pump();
                    var date=Descendants<DatePicker>(view).Single(d=>d.GetBindingExpression(DatePicker.SelectedDateProperty)?.ParentBinding.Path?.Path=="Header.DeliveryDate");
                    date.SelectedDate=new DateTime(2026,12,1); check(model.Header.DeliveryDate==new DateTime(2026,12,1),"单头日期双向回写");
                    var line=model.Lines[0]; check(!line.CanEditUnit,"标准单位禁止手工伪造");
                    CheckDeliverySupplier(fixture, grid, line, check);
                    model.AddNonStandardLineCommand.Execute(null); var nonstandard=model.Lines[1];
                    check(nonstandard.CanEditUnit && nonstandard.PlannedDeliveryDate==model.Header.DeliveryDate,"非标单位允许编辑，新行复制当前交期");
                    Pump(); grid.UpdateLayout(); Pump();
                    check(ReferenceEquals(grid.SelectedItem,nonstandard) && ReferenceEquals(grid.CurrentCell.Item,nonstandard),"新增非标行后选中并定位到实际新行");
                    model.Header.DeliveryDate=new DateTime(2026,12,2); check(nonstandard.PlannedDeliveryDate==new DateTime(2026,12,1),"后续单头交期不覆盖行");
                    var unitColumn=(DataGridTemplateColumn)grid.Columns.Single(c=>(string)c.Header=="单位");
                    check(unitColumn.CellEditingTemplate==null,"采购单位不依赖进入单元格编辑模式");
                    grid.ScrollIntoView(line,unitColumn); grid.UpdateLayout(); Pump();
                    var unitEditor=Descendants<ComboBox>(unitColumn.GetCellContent(line)).Single();
                    check(!unitEditor.IsEnabled,"实际标准行的常显单位下拉禁用输入");
                    grid.ScrollIntoView(nonstandard,unitColumn); grid.UpdateLayout(); Pump();
                    unitEditor=Descendants<ComboBox>(unitColumn.GetCellContent(nonstandard)).Single();
                    check(unitEditor.IsEnabled,"实际非标行的常显单位下拉开放输入");
                    unitEditor.Text="BOX"; Pump();
                    check(nonstandard.Unit=="BOX","非标单位不进入编辑模式也能即时回写");
                    grid.SelectedItems.Clear();
                    grid.SelectedItems.Add(line); grid.SelectedItems.Add(nonstandard); Pump();
                    check(model.DeleteSelectedLineCommand.CanExecute(null),"视图多选桥接删除命令");
                    model.DeleteSelectedLineCommand.Execute(null); check(model.Lines.Count==0,"多选删除全部目标行");
                    model.AddStandardLineCommand.Execute(null); line=model.Lines[0]; line.CustomerSku="AQ1"; line.QuantityText="1"; line.UnitPriceText="2";
                    Wait(model.CheckImportAsync()); Pump(); check(model.CanSaveDraft && line.Sku=="REAL-SKU","匹配后系统 SKU 及保存资格回显");
                    CheckMatchPopup(view, grid, line, check);
                    CheckSortingAndRowType(fixture, grid, line, view, check);
                    line.QuantityText="0"; Pump();
                    var quantityColumn=grid.Columns.OfType<DataGridTextColumn>().Single(c=>Equals(c.Header,"数量"));
                    var quantityText=(TextBlock)quantityColumn.GetCellContent(line);
                    check((quantityText.Foreground as SolidColorBrush)?.Color == Colors.Red,"零数量真实显示红色");
                    line.QuantityText="1"; Pump();
                    check((quantityText.Foreground as SolidColorBrush)?.Color != Colors.Red,"正数量恢复默认颜色");
                    line.UnitPriceText="bad"; Pump(); check(!model.CanSaveDraft && model.ValidationSummary.Contains("不完整"),"非法价格立即阻断保存并提示统计不完整");
                    fixture.Base.Confirm=false; check(!view.CanClose(),"取消普通关闭保留内存编辑");
                    CheckConfigurationGroups(fixture,check);
                }
                finally { window.Close(); }
            }
        }

        /// <summary>实际供应商选择与命令清空必须同步模型身份和界面选择。</summary>
        private static void CheckDeliverySupplier(PurchaseImportTestFixture fixture, DataGrid grid,
            PurchaseOrderImportLine line, Action<bool,string> check)
        {
            var supplier = new SwiftExport.Contracts.DTOs.Suppliers.SupplierDto { Uid=Guid.NewGuid(), SupplierName="送达工厂" };
            fixture.Base.Cache.SupplierItems.Add(supplier);
            var column=(DataGridTemplateColumn)grid.Columns.Single(c=>Equals(c.Header,"客供件送至供应商"));
            grid.ScrollIntoView(line, column); grid.UpdateLayout(); Pump();
            var content=column.GetCellContent(line);
            var combo=Descendants<ComboBox>(content).Single();
            combo.SelectedItem=supplier; Pump();
            check(line.DeliverySupplierId==supplier.Uid,"送达供应商选择回写真实 ID；ID="+line.DeliverySupplierId+"，选择="+combo.SelectedValue+"，候选数="+combo.Items.Count+"，绑定="+combo.GetBindingExpression(Selector.SelectedValueProperty)?.Status);
            var clear=Descendants<Button>(content).Single();
            check(clear.IsEnabled && ReferenceEquals(clear.CommandParameter,line),"清空命令绑定当前行且可执行");
            clear.Command.Execute(clear.CommandParameter); Pump();
            check(!line.DeliverySupplierId.HasValue && combo.SelectedItem==null && combo.SelectedValue==null,
                "清空后送达供应商 ID、下拉选择同步为空；ID="+line.DeliverySupplierId+"，选择="+combo.SelectedValue);
            line.DeliverySupplierId=supplier.Uid; Pump();
            check(ReferenceEquals(combo.SelectedItem,supplier),"模型的送达供应商身份能回显");
            fixture.Model.ClearDeliverySupplierCommand.Execute(line); Pump();
            check(combo.SelectedItem==null && !line.DeliverySupplierId.HasValue,"重新选择后再次清空不保留旧供应商");
        }

        /// <summary>表头全部常驻且横向分组；校验列表有高度上限，不挤掉明细。</summary>
        private static void CheckPermanentHeader(PurchaseOrderImportView view, DataGrid grid, Action<bool,string> check)
        {
            check(view.FindName("OtherPopup") == null && view.FindName("OtherToggleButton") == null,"采购表头已退出折叠弹层");
            var fields=Descendants<TextBox>(view).Select(b=>b.GetBindingExpression(TextBox.TextProperty)?.ParentBinding.Path?.Path).ToArray();
            foreach(var field in new[]{"CustomerPoNo","BoNo","SalesPerson","Remark","PurchasePerson","InletHoseType","AeratorType","PalletType","BoxType","LabelType","InstructionManualType","CartonType","LogoType","DiscountRateText","DiscountAmountText","DiscountRemark"})
                check(fields.Contains("Header."+field),"常驻表头双向绑定："+field);
            var required=Descendants<TextBlock>(view).Where(b=>b.Text.EndsWith(" *",StringComparison.Ordinal)).ToArray();
            check(required.Length==3 && required.All(b=>(b.Foreground as SolidColorBrush)?.Color==Colors.Red),"客户、供应商、订单日期红星必填提示");
            var toggle=(Wpf.Ui.Controls.ToggleSwitch)view.FindName("ExtraFieldsSwitch");
            var optional=grid.Columns.Where(c=>System.Windows.Data.BindingOperations.IsDataBound(c,DataGridColumn.VisibilityProperty)).ToArray();
            check(optional.Length>0 && optional.All(c=>c.Visibility==Visibility.Collapsed),"默认隐藏次要明细字段");
            toggle.IsChecked=true; Pump(); check(optional.All(c=>c.Visibility==Visibility.Visible),"更多字段开关能显示全部次要列");
            toggle.IsChecked=false; Pump();
            var previousHeight=grid.ActualHeight;
            var model=(SwiftExport.Client.VSTO.Features.Procurement.PurchaseOrders.ViewModels.PurchaseOrderImportViewModel)view.DataContext;
            for(var i=0;i<150;i++) model.AddStandardLineCommand.Execute(null);
            Pump(); view.UpdateLayout();
            check(grid.ActualHeight>view.ActualHeight*0.35 && grid.ActualHeight>=previousHeight-84,"大量错误仅在有上限的提示区域滚动，明细仍有可用高度");
            model.SetSelectedLines(model.Lines.Skip(1).ToArray()); model.DeleteSelectedLineCommand.Execute(null); Pump();
            var status=Descendants<TextBlock>(view).First(b=>b.GetBindingExpression(TextBlock.TextProperty)?.ParentBinding.Path?.Path=="ValidationSummary");
            check((status.Foreground as SolidColorBrush)?.Color==Colors.Red,"顶部校验提示使用红字");
            check(grid.Columns.Single(c=>Equals(c.Header,"校验")).SortMemberPath=="IsCheckPassed","校验列按通过状态排序");
            check(grid.Columns.Any(c=>Equals(c.Header,"非标件")) && !grid.Columns.Any(c=>Equals(c.Header,"行类型")),"单一非标复选列取代重复行类型");
        }
        /// <summary>真实行模板共享行和页面命令，弹层关闭不覆盖编辑值。</summary>
        private static void CheckMatchPopup(PurchaseOrderImportView view, DataGrid grid, PurchaseOrderImportLine line, Action<bool,string> check)
        {
            Pump(); grid.ScrollIntoView(line); grid.UpdateLayout(); Pump();
            var column=(DataGridTemplateColumn)grid.Columns.Single(c=>Equals(c.Header,"校验"));
            var content=column.GetCellContent(line);
            var entry=Descendants<ToggleButton>(content).Single(b=>b.Name=="MatchActionsEntry");
            var popup=Descendants<Popup>(content).Single(); entry.IsChecked=true; Pump();
            check(popup.IsOpen && ReferenceEquals(popup.DataContext,line),"行内匹配弹层保持当前行上下文");
            var button=Descendants<Button>(popup.Child).Single(b=>Equals(b.Content,"匹配本行 SKU"));
            check(button.Command!=null && ReferenceEquals(button.CommandParameter,line),"行内弹层匹配命令绑定本行");
            var close=Descendants<Button>(popup.Child).Single(b=>ReferenceEquals(b.Command,ApplicationCommands.Close));
            ApplicationCommands.Close.Execute(null,close); Pump();
            check(!popup.IsOpen && entry.IsChecked==false,"行内关闭按钮同步复位入口");
            var lineColumn=(DataGridTemplateColumn)grid.Columns.Single(c=>Equals(c.Header,"行号"));
            check(lineColumn.SortMemberPath=="LineNumber" && grid.RowHeaderTemplate==null && grid.RowHeaderWidth==0,"行号列可恢复导入顺序，状态不再占行头");
            var ellipse=Descendants<System.Windows.Shapes.Ellipse>(lineColumn.GetCellContent(line)).Single();
            check((ellipse.Fill as SolidColorBrush)?.Color == ((SolidColorBrush)view.FindResource("UserBusinessTextSuccessBrush")).Color
                && line.IsCheckPassed,"通过行显示绿色状态圆点");
            var image=(SwiftExport.Client.VSTO.Resources.Controls.HttpsImage)((DataGridTemplateColumn)grid.Columns.Single(c=>Equals(c.Header,"系统图片"))).CellTemplate.LoadContent();
            image.DataContext=new { MainImage="https://example.invalid/test.png" }; Pump();
            check(image.SourcePath=="https://example.invalid/test.png" && !image.IsLoaded,"图片列保留 HTTPS 路径绑定，未加载模板不下载");
        }
        /// <summary>真实视图可将未通过行集中排序，非标复选框使用当前行命令而不依赖选中项。</summary>
        private static void CheckSortingAndRowType(PurchaseImportTestFixture fixture,
            DataGrid grid, PurchaseOrderImportLine passed, PurchaseOrderImportView view, Action<bool,string> check)
        {
            var model=fixture.Model;
            model.AddStandardLineCommand.Execute(null); var pending=model.Lines.Last();
            var collection=System.Windows.Data.CollectionViewSource.GetDefaultView(grid.ItemsSource);
            collection.SortDescriptions.Add(new SortDescription(nameof(PurchaseOrderImportLine.IsCheckPassed),ListSortDirection.Ascending));
            Pump(); grid.ScrollIntoView(pending); grid.UpdateLayout(); Pump();
            check(ReferenceEquals(grid.Items[0],pending) && ReferenceEquals(grid.Items[1],passed),"按校验排序将未通过行放在一起");
            check(ReferenceEquals(grid.SelectedItem,pending) && ReferenceEquals(grid.CurrentCell.Item,pending),"按校验排序后新增标准行仍定位到正确对象");
            var lineColumn=(DataGridTemplateColumn)grid.Columns.Single(c=>Equals(c.Header,"行号"));
            var ellipse=Descendants<System.Windows.Shapes.Ellipse>(lineColumn.GetCellContent(pending)).Single();
            check((ellipse.Fill as SolidColorBrush)?.Color==((SolidColorBrush)view.FindResource("UserBusinessTextCriticalBrush")).Color,"未通过行显示红色状态圆点");
            var typeColumn=(DataGridTemplateColumn)grid.Columns.Single(c=>Equals(c.Header,"非标件"));
            var checkbox=Descendants<CheckBox>(typeColumn.GetCellContent(pending)).Single();
            checkbox.Command.Execute(checkbox.CommandParameter); Pump();
            check(pending.IsNonStandard && checkbox.IsChecked==true && ReferenceEquals(checkbox.CommandParameter,pending),"非标复选命令只改变当前行并回显");
            collection.SortDescriptions.Clear();
            collection.SortDescriptions.Add(new SortDescription(nameof(PurchaseOrderImportLine.LineNumber),ListSortDirection.Ascending)); Pump();
            check(ReferenceEquals(grid.Items[0],passed) && ReferenceEquals(grid.Items[1],pending),"按行号排序恢复新增前后的原始顺序");
            // 异步检查会冻结页面，常显下拉不能绕过原来的编辑门禁。
            var response=new TaskCompletionSource<SwiftExport.Contracts.DTOs.ForExcel.PurchaseOrders.CheckPurchaseOrderImportResponse>();
            fixture.Transport.CheckOverride=request=>response.Task;
            var task=model.MatchAllSkusCommand.ExecuteAsync(null); Pump();
            var unitColumn=(DataGridTemplateColumn)grid.Columns.Single(c=>Equals(c.Header,"单位"));
            grid.ScrollIntoView(pending,unitColumn); grid.UpdateLayout(); Pump();
            var unit=Descendants<ComboBox>(unitColumn.GetCellContent(pending)).Single();
            check(pending.CanEditUnit && !unit.IsEnabled && grid.IsReadOnly,"非标单位常显下拉仍遵守异步检查期间的页面冻结");
            response.SetResult(fixture.Transport.Checked(fixture.Transport.LastCheck)); Wait(task); Pump();
            check(unit.IsEnabled,"检查结束后非标单位下拉恢复编辑");
            fixture.Transport.CheckOverride=null;
            collection.SortDescriptions.Clear(); model.Lines.Remove(pending); Wait(model.CheckImportAsync()); Pump();
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
