using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.VSTO.Services.ExcelImport;
using SwiftExport.Client.VSTO.Testing;

namespace SwiftExport.Client.VSTO.Tests
{
    /// <summary>验证本地显示行号与导入、增删和定位通知的边界，不加载 Office。</summary>
    [TestClass]
    public sealed class ImportLinePresentationTests
    {
        /// <summary>采购行号保持原始顺序，删除留空号，新增通知携带已赋号的真实行。</summary>
        [TestMethod]
        public async Task PurchaseNumbersAndAddNotificationsFollowSourceOrder()
        {
            using(var fixture=new PurchaseImportTestFixture())
            {
                await fixture.Prepare(); var model=fixture.Model; var first=model.Lines[0];
                var notifications=new List<int>(); model.LineAdded+=line=> { Assert.AreSame(model.Lines.Last(),line); notifications.Add(line.LineNumber); };
                model.AddNonStandardLineCommand.Execute(null); model.AddStandardLineCommand.Execute(null);
                Assert.AreEqual(1,first.LineNumber); CollectionAssert.AreEqual(new[]{2,3},notifications);
                var removed=model.Lines[1]; model.SetSelectedLines(new[]{removed}); model.DeleteSelectedLineCommand.Execute(null);
                model.AddNonStandardLineCommand.Execute(null);
                CollectionAssert.AreEqual(new[]{1,3,4},model.Lines.Select(l=>l.LineNumber).ToArray());
                var revision=first.EditRevision; first.AssignLineNumber(first.LineNumber);
                Assert.AreEqual(revision,first.EditRevision,"显示行号通知不能使产品检查失效");
                model.Dispose(); model.AddStandardLineCommand.Execute(null); Assert.AreEqual(3,notifications.Count);
            }
        }

        /// <summary>销售新增和删除不重编号，生命周期结束不再发送视图定位请求。</summary>
        [TestMethod]
        public async Task SalesNumbersAndAddNotificationsFollowSourceOrder()
        {
            using(var fixture=new ImportTestFixture())
            {
                await fixture.Start(); var model=fixture.Model; var notifications=new List<int>();
                model.LineAdded+=line=> { Assert.AreSame(model.Lines.Last(),line); notifications.Add(line.LineNumber); };
                model.AddStandardLineCommand.Execute(null); model.AddNonStandardLineCommand.Execute(null); model.AddStandardLineCommand.Execute(null);
                var first=model.Lines[0]; model.SetSelectedLines(new[]{model.Lines[1]}); model.DeleteSelectedLineCommand.Execute(null);
                model.AddNonStandardLineCommand.Execute(null);
                CollectionAssert.AreEqual(new[]{1,3,4},model.Lines.Select(l=>l.LineNumber).ToArray());
                CollectionAssert.AreEqual(new[]{1,2,3,4},notifications);
                var revision=first.EditRevision; first.AssignLineNumber(first.LineNumber); Assert.AreEqual(revision,first.EditRevision);
                model.Dispose(); model.AddNonStandardLineCommand.Execute(null); Assert.AreEqual(4,notifications.Count);
            }
        }

        /// <summary>重新读取采购明细从一编号，不沿用旧手工行号，也不逐行请求视图定位。</summary>
        [TestMethod]
        public async Task PurchaseReadRestartsNumbersWithoutManualAddNotification()
        {
            using(var fixture=new PurchaseImportTestFixture())
            {
                await fixture.Prepare(); await fixture.Model.EnsureReadProfilesAsync();
                fixture.Model.AddStandardLineCommand.Execute(null); fixture.Model.AddNonStandardLineCommand.Execute(null);
                var notifications=0; fixture.Model.LineAdded+=line=>notifications++;
                fixture.Base.AddressReader.Read=detail=>Task.FromResult(TwoRows("Line.Quantity"));
                await fixture.Model.ReadLinesCommand.ExecuteAsync(null);
                CollectionAssert.AreEqual(new[]{1,2},fixture.Model.Lines.Select(l=>l.LineNumber).ToArray());
                CollectionAssert.AreEqual(new[]{"FIRST","SECOND"},fixture.Model.Lines.Select(l=>l.CustomerSku).ToArray()); Assert.AreEqual(0,notifications);
            }
        }

        /// <summary>重新读取销售明细从一编号，不受旧行和列表排序的展示顺序影响。</summary>
        [TestMethod]
        public async Task SalesReadRestartsNumbersWithoutManualAddNotification()
        {
            using(var fixture=new ImportTestFixture())
            {
                await fixture.Start(); await fixture.Model.EnsureReadProfilesAsync();
                fixture.Model.AddStandardLineCommand.Execute(null); fixture.Model.AddNonStandardLineCommand.Execute(null);
                var notifications=0; fixture.Model.LineAdded+=line=>notifications++;
                fixture.AddressReader.Read=detail=>Task.FromResult(TwoRows("Line.Qty"));
                await fixture.Model.ReadLinesCommand.ExecuteAsync(null);
                CollectionAssert.AreEqual(new[]{1,2},fixture.Model.Lines.Select(l=>l.LineNumber).ToArray());
                CollectionAssert.AreEqual(new[]{"FIRST","SECOND"},fixture.Model.Lines.Select(l=>l.CustomerSku).ToArray()); Assert.AreEqual(0,notifications);
            }
        }

        /// <summary>提供两个实际读取行，位置与数量字段可按销售或采购目录指定。</summary>
        private static ExcelAddressSnapshot TwoRows(string quantityField) => new ExcelAddressSnapshot
        {
            Detail=true,WorkbookName="订单.xlsx",WorksheetName="Sheet1",
            Rows=new List<List<ExcelAddressValue>>
            {
                new List<ExcelAddressValue> { new ExcelAddressValue {FieldKey="Line.CustomerSku",Row=18,Column=3,Value="FIRST"},new ExcelAddressValue {FieldKey=quantityField,Row=18,Column=4,Value=2} },
                new List<ExcelAddressValue> { new ExcelAddressValue {FieldKey="Line.CustomerSku",Row=19,Column=3,Value="SECOND"},new ExcelAddressValue {FieldKey=quantityField,Row=19,Column=4,Value=3} }
            }
        };
    }
}
