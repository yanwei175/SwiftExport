using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SwiftExport.Application.Tests.Procurement;

/// <summary>验证需求采购草稿的客户身份、代码和名称快照在写入、读取与追加链中保持完整。</summary>
[TestClass]
public sealed class PurchaseDraftCustomerSnapshotContractTests
{
    /// <summary>Dapper 新建与读取必须显式保留完整客户快照。</summary>
    [TestMethod]
    public void DraftRepositoriesMustPersistAndProjectCompleteCustomerSnapshot()
    {
        var draftRepository = Read("SwiftExport.Infrastructure", "Repositories", "Procurement", "PurchaseOrderDraftRepository.cs");

        StringAssert.Contains(draftRepository, "CustomerPoNo,CustomerId,CustomerCode,CustomerNameSnapshot,Currency");
        StringAssert.Contains(draftRepository, "@CustomerPoNo,@CustomerId,@CustomerCode,@CustomerNameSnapshot,@Currency");
        StringAssert.Contains(draftRepository, "h.CustomerPoNo,h.CustomerId,h.CustomerCode,h.CustomerNameSnapshot,h.Currency");
    }

    /// <summary>追加路径只创建行并推进版本，不得用当前客户名称或代码覆盖原采购头快照。</summary>
    [TestMethod]
    public void AppendMustRequireSameCustomerIdentityWithoutUpdatingCustomerSnapshots()
    {
        var service = Read("SwiftExport.Application", "Services", "Procurement", "PurchaseDraftCreationService.cs");
        var repository = Read("SwiftExport.Infrastructure", "Repositories", "Procurement", "PurchaseOrderDraftRepository.cs");
        var appendSection = Slice(repository, "private Task<int> AdvanceDraftVersionAsync", "private static void ValidateHeadKey");

        StringAssert.Contains(service, "head.CustomerId != document.CustomerId");
        StringAssert.Contains(service, "writeModelBuilder.Build(");
        StringAssert.Contains(service, "targetMode == PurchaseDraftTargetModeEnum.CreateNew");
        Assert.IsFalse(appendSection.Contains("CustomerId", StringComparison.Ordinal));
        Assert.IsFalse(appendSection.Contains("CustomerCode", StringComparison.Ordinal));
        Assert.IsFalse(appendSection.Contains("CustomerNameSnapshot", StringComparison.Ordinal));
    }

    /// <summary>写模型的客户名称来自权威预览文档，创建时不读取可编辑 Header 伪造客户身份。</summary>
    [TestMethod]
    public void WriteModelMustUseDocumentCustomerFactsAndLeaveConfirmationBatchEmpty()
    {
        var builder = Read("SwiftExport.Application", "Services", "Procurement", "PurchaseDraftWriteModelBuilder.cs");
        var creation = Read("SwiftExport.Application", "Services", "Procurement", "PurchaseDraftCreationService.cs");
        var preview = Read("SwiftExport.Application", "Services", "Procurement", "PurchaseDraftPreviewBuilder.cs");

        StringAssert.Contains(creation, "customerRepository.GetByIdListAsync(customerIds, withUpdLock: true)");
        StringAssert.Contains(creation, "CustomerName = customer?.CustomerName ?? string.Empty");
        StringAssert.Contains(preview, "CustomerName = first.CustomerName");
        StringAssert.Contains(builder, "CustomerId = document.CustomerId");
        StringAssert.Contains(builder, "CustomerCode = document.CustomerCode");
        StringAssert.Contains(builder, "CustomerNameSnapshot = document.CustomerName");
        Assert.IsFalse(builder.Contains("document.Header.CustomerId", StringComparison.Ordinal));
        Assert.IsFalse(builder.Contains("document.Header.CustomerCode", StringComparison.Ordinal));
        Assert.IsFalse(builder.Contains("document.Header.CustomerName", StringComparison.Ordinal));
        Assert.IsFalse(builder.Contains("ActiveConfirmationId =", StringComparison.Ordinal));
    }

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
