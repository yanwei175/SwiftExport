using System.Reflection;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Refit;
using SwiftExport.Contracts.Apis.Shipping;
using SwiftExport.Contracts.DTOs.Shipping.PackingLists;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Client.Wpf.Tests.Features.Shipping;

/// <summary>验证装箱单草稿、过账及追踪路由、取消和序列化边界。</summary>
[TestClass]
public sealed class PackingListApiContractTests
{
    /// <summary>批准的十一条路由、请求体与返回契约保持完整一致。</summary>
    [TestMethod]
    public void RoutesAndSignaturesMatchHandoff()
    {
        var specs = new (string Method, string Path, Type Attribute, Type? Body, Type Data)[]
        {
            ("PostPreviewAsync", "/{uid}/post-preview", typeof(PostAttribute), typeof(PackingListPostPreviewRequest), typeof(PackingListPostingPreviewDto)),
            ("PostAsync", "/{uid}/post", typeof(PostAttribute), typeof(PostPackingListCommand), typeof(PackingListPostingCommandResult)),
            ("ReversePreviewAsync", "/{uid}/reverse-preview", typeof(PostAttribute), typeof(PackingListReversePreviewRequest), typeof(PackingListPostingPreviewDto)),
            ("ReverseAsync", "/{uid}/reverse", typeof(PostAttribute), typeof(ReversePackingListCommand), typeof(PackingListPostingCommandResult)),
            ("TraceAsync", "/{uid}/trace", typeof(GetAttribute), null, typeof(PackingListTraceDto)),
            ("SearchAsync", "/search", typeof(PostAttribute), typeof(PackingListManagerSearchRequest), typeof(PagedResponseBase<PackingListManagerListItemDto>)),
            ("GetAsync", "/{uid}", typeof(GetAttribute), null, typeof(PackingListDetailDto)),
            ("SearchSourcesAsync", "/sources/search", typeof(PostAttribute), typeof(PackingListSourceSearchRequest), typeof(PagedResponseBase<PackingListSourceCandidateDto>)),
            ("CreateAsync", "/drafts", typeof(PostAttribute), typeof(CreatePackingListDraftCommand), typeof(PackingListDraftCommandResult)),
            ("UpdateAsync", "/drafts/{uid}", typeof(PutAttribute), typeof(UpdatePackingListDraftCommand), typeof(PackingListDraftCommandResult)),
            ("DeleteAsync", "/drafts/{uid}/delete", typeof(PostAttribute), typeof(DeletePackingListDraftCommand), typeof(PackingListDraftCommandResult))
        };
        Assert.AreEqual(specs.Length, typeof(IPackingListApi).GetMethods().Length);
        foreach (var spec in specs)
        {
            var method = typeof(IPackingListApi).GetMethod(spec.Method)!;
            var attribute = method.GetCustomAttributes(spec.Attribute, false).Single();
            Assert.AreEqual(spec.Path, spec.Attribute.GetProperty("Path")!.GetValue(attribute));
            Assert.AreEqual(typeof(Task<>).MakeGenericType(typeof(ServiceResult<>).MakeGenericType(spec.Data)), method.ReturnType);
            Assert.AreEqual(typeof(CancellationToken), method.GetParameters().Last().ParameterType);
            var body = method.GetParameters().Where(p => p.GetCustomAttribute<BodyAttribute>() != null).ToArray();
            Assert.AreEqual(spec.Body is null ? 0 : 1, body.Length);
            if (spec.Body != null) Assert.AreEqual(spec.Body, body[0].ParameterType);
        }
    }

    /// <summary>整单更新保留有效行和显式删除身份，未知包装不变为零。</summary>
    [TestMethod]
    public void UpdateRoundTripsIdentityVersionsAndUnknownValues()
    {
        var request = new UpdatePackingListDraftCommand
        {
            CommandId = Guid.NewGuid(), ExpectedVersion = 8,
            Header = new PackingListDraftHeaderInput { CustomerId = Guid.NewGuid(), InvoiceDate = new DateTime(2026, 9, 15) },
            Lines = new() { new() { Uid = Guid.NewGuid(), ExpectedVersion = 3, GrossDemandId = Guid.NewGuid(), Qty = 2.3456m, PcsPerPackage = null } },
            DeletedLines = new() { new() { Uid = Guid.NewGuid(), ExpectedVersion = 4 } }
        };
        var copy = JsonSerializer.Deserialize<UpdatePackingListDraftCommand>(JsonSerializer.Serialize(request))!;
        Assert.AreEqual(request.CommandId, copy.CommandId);
        Assert.AreEqual(8, copy.ExpectedVersion);
        Assert.AreEqual(request.Lines[0].Uid, copy.Lines[0].Uid);
        Assert.AreEqual(2.3456m, copy.Lines[0].Qty);
        Assert.IsNull(copy.Lines[0].PcsPerPackage);
        Assert.AreEqual(request.DeletedLines[0].Uid, copy.DeletedLines[0].Uid);
        Assert.AreEqual(4, copy.DeletedLines[0].ExpectedVersion);
    }

    /// <summary>查询快照和写命令分离，客户端不能提交过账或审计字段。</summary>
    [TestMethod]
    public void WriteInputsExcludeAuthoritativeAndUiFields()
    {
        foreach (var type in new[] { typeof(PackingListDraftHeaderInput), typeof(PackingListDraftLineInput) })
        {
            var names = type.GetProperties().Select(x => x.Name).ToHashSet();
            foreach (var name in new[] { "LifecycleStatus", "PostingStatus", "PostingDate", "PostedDate", "CreatedBy", "UpdatedBy", "IsDeleted", "IsSelected", "ImagePath", "InvoiceNosSummary", "SubTotalNw", "SubTotalGw", "SubTotalCbm", "PurchaseOrderLineId", "NetDemandAllocationId" })
                Assert.IsFalse(names.Contains(name), $"{type.Name}.{name}");
        }
        var source = JsonSerializer.Deserialize<PackingListSourceCandidateDto>("{}")!;
        Assert.IsNull(source.DeliveredQty);
        Assert.IsNull(source.RemainingQty);
        Assert.IsNull(source.PcsPerPackage);
    }
}
