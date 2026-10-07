using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Infrastructure.Repositories.Procurement.PurchaseOrders.Draft;

namespace SwiftExport.Application.Tests.Procurement;

/// <summary>验证需求采购草稿的客户身份、代码和名称快照在写入、读取与追加链中保持完整。</summary>
[TestClass]
public sealed class PurchaseDraftCustomerSnapshotContractTests
{
    /// <summary>Dapper 新建与读取必须显式保留完整客户快照。</summary>
    [TestMethod]
    public void DraftRepositoriesMustPersistAndProjectCompleteCustomerSnapshot()
    {
        var draftRepository = Read("SwiftExport.Infrastructure", "Repositories", "Procurement", "PurchaseOrders", "Draft", "PostgreSqlPurchaseOrderDraftRepository.cs");

        // 核对生产实际使用的 PG SQL 常量，不保留已退出的字段数组展开器。
        foreach (var field in new[] { ("CustomerPoNo", "customer_po_no"), ("CustomerId", "customer_id"),
                     ("CustomerCode", "customer_code"), ("CustomerNameSnapshot", "customer_name_snapshot"), ("Currency", "currency") })
        {
            StringAssert.Contains(Sql("HeadInsertSql"), "\"" + field.Item2 + "\"");
            StringAssert.Contains(Sql("HeadInsertSql"), "@" + field.Item1);
            StringAssert.Contains(Sql("HeadProjection"), "h.\"" + field.Item2 + "\" AS \"" + field.Item1 + "\"");
        }
        StringAssert.Contains(draftRepository, "SELECT {HeadProjection}");
        StringAssert.Contains(draftRepository, "HeadInsertSql,");
        StringAssert.Contains(draftRepository, "PostgreSqlParameters.Entity(head, Head)");
    }

    /// <summary>追加路径只创建行并推进版本，不得用当前客户名称或代码覆盖原采购头快照。</summary>
    [TestMethod]
    public void AppendMustRequireSameCustomerIdentityWithoutUpdatingCustomerSnapshots()
    {
        var service = Read("SwiftExport.Application", "Services", "Procurement", "PurchaseOrders", "Draft", "FromDemand", "PurchaseDraftCreationService.cs");
        var repository = Read("SwiftExport.Infrastructure", "Repositories", "Procurement", "PurchaseOrders", "Draft", "PostgreSqlPurchaseOrderDraftRepository.cs");
        var appendSection = Slice(repository, "public Task<int> AdvanceDraftVersionAfterAppendAsync", "private static void ValidateHeadKey");

        StringAssert.Contains(service, "head.CustomerId != document.CustomerId");
        StringAssert.Contains(service, "writeModelBuilder.Build(");
        StringAssert.Contains(service, "targetMode == PurchaseDraftTargetModeEnum.CreateNew");
        Assert.IsFalse(appendSection.Contains("CustomerId", StringComparison.Ordinal));
        Assert.IsFalse(appendSection.Contains("CustomerCode", StringComparison.Ordinal));
        Assert.IsFalse(appendSection.Contains("CustomerNameSnapshot", StringComparison.Ordinal));
        Assert.IsFalse(appendSection.Contains("customer_id", StringComparison.Ordinal));
        Assert.IsFalse(appendSection.Contains("customer_code", StringComparison.Ordinal));
        Assert.IsFalse(appendSection.Contains("customer_name_snapshot", StringComparison.Ordinal));
    }

    /// <summary>写模型的客户名称来自权威预览文档，创建时不读取可编辑 Header 伪造客户身份。</summary>
    [TestMethod]
    public void WriteModelMustUseDocumentCustomerFactsAndLeaveConfirmationBatchEmpty()
    {
        var builder = Read("SwiftExport.Application", "Services", "Procurement", "PurchaseOrders", "Draft", "FromDemand", "PurchaseDraftWriteModelBuilder.cs");
        var creation = Read("SwiftExport.Application", "Services", "Procurement", "PurchaseOrders", "Draft", "FromDemand", "PurchaseDraftCreationService.cs");
        var preview = Read("SwiftExport.Application", "Services", "Procurement", "PurchaseOrders", "Draft", "FromDemand", "PurchaseDraftPreviewBuilder.cs");

        var previewService = Read("SwiftExport.Application", "Services", "Procurement", "PurchaseOrders", "Draft", "FromDemand", "PurchaseDraftPreviewService.cs");
        StringAssert.Contains(previewService, "customerRepository.GetByIdListAsync(customerIds, withUpdLock: true)");
        StringAssert.Contains(previewService, "CustomerName = customer?.CustomerName ?? string.Empty");
        StringAssert.Contains(preview, "CustomerName = first.CustomerName");
        StringAssert.Contains(builder, "CustomerId = document.CustomerId");
        StringAssert.Contains(builder, "CustomerCode = document.CustomerCode");
        StringAssert.Contains(builder, "CustomerNameSnapshot = document.CustomerName");
        Assert.IsFalse(builder.Contains("document.Header.CustomerId", StringComparison.Ordinal));
        Assert.IsFalse(builder.Contains("document.Header.CustomerCode", StringComparison.Ordinal));
        Assert.IsFalse(builder.Contains("document.Header.CustomerName", StringComparison.Ordinal));
        Assert.IsFalse(builder.Contains("ActiveConfirmationId =", StringComparison.Ordinal));
    }

    /// <summary>读取当前仓储实际执行的 SQL 常量，核对固定写列及投影事实。</summary>
    private static string Sql(string name) =>
        (string)typeof(PostgreSqlPurchaseOrderDraftRepository).GetField(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetValue(null)!;

    private static string Slice(string source, string startToken, string endToken)
    {
        var start = source.IndexOf(startToken, StringComparison.Ordinal);
        var end = source.IndexOf(endToken, start, StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, start);
        Assert.IsGreaterThan(start, end);
        return source[start..end];
    }

    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { Root() }.Concat(parts).ToArray()));

    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SwiftExport-WebAPI.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("无法定位 SwiftExport 仓库根目录。");
    }
}
