using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.Errors;

namespace SwiftExport.Application.Tests.Files.ExportTemplates;

/// <summary>验证完整模板快照、精确契约、版本选择及安全错误出口。</summary>
[TestClass]
public sealed class ExportTemplateCatalogServiceTests
{
    /// <summary>活动身份拒绝先于输入和存储检查，拒绝路径不会读文件。</summary>
    [TestMethod]
    [DataRow(ServiceErrorCodes.Unauthenticated)]
    [DataRow(ServiceErrorCodes.Forbidden)]
    public async Task Authorization_ShouldPrecedeStorageAndInputValidation(string code)
    {
        using var scope = new ExportTemplateTestScope();
        scope.Policy.DeniedCode = code;
        Assert.AreEqual(code, (await scope.Service.ListAsync("", "", 0)).Error.Code);
        Assert.AreEqual(code, (await scope.Service.OpenReadAsync("../unsafe", 0)).Error.Code);
        Assert.AreEqual(2, scope.Policy.Reads);
        Assert.AreEqual(0, scope.Provider.Calls);
        Assert.AreEqual(0, Directory.GetFiles(scope.Root).Length);
    }

    /// <summary>禁用分发服务时返回服务不可用，不要求清单存在。</summary>
    [TestMethod]
    public async Task DisabledService_ShouldReturnUnavailable()
    {
        using var scope = new ExportTemplateTestScope();
        scope.Provider.Context = null;
        Assert.AreEqual(ServiceErrorCodes.ServiceUnavailable, (await scope.Service.ListAsync("SO", "SalesOrder.Export", 1)).Error.Code);
        Assert.AreEqual(ServiceErrorCodes.ServiceUnavailable, (await scope.Service.OpenReadAsync("sales-standard", 1)).Error.Code);
    }

    /// <summary>单据、契约与版本精确筛选；停用文件可缺失，合法无匹配和空目录均成功。</summary>
    [TestMethod]
    public async Task List_ShouldFilterPreciselyAndAllowEmptyResults()
    {
        using var scope = new ExportTemplateTestScope();
        await scope.PublishAsync(scope.Item(), scope.Item("purchase", "PO"),
            scope.Item("contract-case", contractKey: "salesorder.export"),
            scope.Item("contract-version", contractVersion: 2), scope.Item("disabled", enabled: false));
        var result = await scope.Service.ListAsync("SO", "SalesOrder.Export", 1);
        Assert.IsTrue(result.Success);
        Assert.HasCount(1, result.Data);
        var item = result.Data[0];
        Assert.AreEqual("sales-standard", item.TemplateId);
        Assert.AreEqual("标准销售单", item.DisplayName);
        Assert.AreEqual("SO", item.DocumentType);
        Assert.AreEqual("SalesOrder.Export", item.DataContractKey);
        Assert.AreEqual(1, item.DataContractVersion);
        Assert.AreEqual(1, item.TemplateVersion);
        Assert.AreEqual(scope.Xlsx.LongLength, item.FileLength);
        Assert.AreEqual(64, item.Sha256.Length);
        var noMatch = await scope.Service.ListAsync("so", "SalesOrder.Export", 1);
        Assert.IsTrue(noMatch.Success);
        Assert.HasCount(0, noMatch.Data);
        await scope.WriteManifestAsync();
        var empty = await scope.Service.ListAsync("CUSTOM", "Custom.Export", 1);
        Assert.IsTrue(empty.Success);
        Assert.HasCount(0, empty.Data);
    }

    /// <summary>查询输入缺失、版本非法或字符串超限均拒绝，不能转为目录故障。</summary>
    [TestMethod]
    [DataRow("", "Key", 1)]
    [DataRow("SO", " ", 1)]
    [DataRow("SO", "Key", 0)]
    [DataRow(" SO", "Key", 1)]
    [DataRow("SO", "Key ", 1)]
    public async Task InvalidListInput_ShouldReturnValidation(string type, string key, int version)
    {
        using var scope = new ExportTemplateTestScope();
        Assert.AreEqual(ServiceErrorCodes.Validation, (await scope.Service.ListAsync(type, key, version)).Error.Code);
        Assert.AreEqual(ServiceErrorCodes.Validation, (await scope.Service.ListAsync(new string('x', 65), "key", 1)).Error.Code);
        Assert.AreEqual(ServiceErrorCodes.Validation, (await scope.Service.ListAsync("SO", new string('x', 129), 1)).Error.Code);
        Assert.AreEqual(0, scope.Provider.Calls);
    }

    /// <summary>下载不接受文件路径、危险标识或非正整数版本。</summary>
    [TestMethod]
    [DataRow("../sales", 1)]
    [DataRow("Sales", 1)]
    [DataRow("sales//bad", 1)]
    [DataRow("sales--bad", 1)]
    [DataRow("sales", 0)]
    public async Task InvalidDownloadInput_ShouldReturnValidation(string id, int version)
    {
        using var scope = new ExportTemplateTestScope();
        Assert.AreEqual(ServiceErrorCodes.Validation, (await scope.Service.OpenReadAsync(id, version)).Error.Code);
        Assert.AreEqual(0, scope.Provider.Calls);
    }

    /// <summary>下载返回校验后的同一快照；磁盘替换不影响结果，旧版本不静默替换为新版本。</summary>
    [TestMethod]
    public async Task Download_ShouldUseSnapshotAndDistinguishVersionFromMissing()
    {
        using var scope = new ExportTemplateTestScope();
        await scope.PublishAsync(scope.Item());
        var result = await scope.Service.OpenReadAsync("sales-standard", 1);
        Assert.IsTrue(result.Success);
        using var content = result.Data.Content;
        Assert.AreEqual("sales-standard-v1.xlsx", result.Data.FileName);
        Assert.AreEqual(0L, content.Position);
        await scope.WriteAsync("sales-standard/v1/模板.xlsx", [1, 2]);
        using var copy = new MemoryStream();
        await content.CopyToAsync(copy);
        CollectionAssert.AreEqual(scope.Xlsx, copy.ToArray());
        await scope.PublishAsync(scope.Item(version: 2));
        Assert.AreEqual(ServiceErrorCodes.VersionConflict, (await scope.Service.OpenReadAsync("sales-standard", 1)).Error.Code);
        Assert.AreEqual(ServiceErrorCodes.NotFound, (await scope.Service.OpenReadAsync("not-found", 1)).Error.Code);
        await scope.WriteManifestAsync(scope.Item(enabled: false));
        Assert.AreEqual(ServiceErrorCodes.NotFound, (await scope.Service.OpenReadAsync("sales-standard", 1)).Error.Code);
    }

    /// <summary>每次读取新清单，管理员明确把清单指向旧版时支持回退。</summary>
    [TestMethod]
    public async Task ManifestChanges_ShouldPermitExplicitRollback()
    {
        using var scope = new ExportTemplateTestScope();
        await scope.PublishAsync(scope.Item());
        await scope.PublishAsync(scope.Item(version: 2));
        Assert.AreEqual(2, (await scope.Service.ListAsync("SO", "SalesOrder.Export", 1)).Data[0].TemplateVersion);
        await scope.WriteManifestAsync(scope.Item());
        Assert.AreEqual(1, (await scope.Service.ListAsync("SO", "SalesOrder.Export", 1)).Data[0].TemplateVersion);
    }

    /// <summary>任意启用引用损坏都会阻止整个快照，包括查询不匹配的其他单据模板。</summary>
    [TestMethod]
    public async Task CorruptUnmatchedFile_ShouldFailWholeSnapshotWithoutPathDisclosure()
    {
        using var scope = new ExportTemplateTestScope();
        await scope.PublishAsync(scope.Item(), scope.Item("purchase", "PO"));
        await scope.WriteAsync("purchase/v1/模板.xlsx", [1]);
        var result = await scope.Service.ListAsync("SO", "SalesOrder.Export", 1);
        Assert.AreEqual(ServiceErrorCodes.Unexpected, result.Error.Code);
        Assert.IsFalse(result.Message.Contains(scope.Root, StringComparison.OrdinalIgnoreCase));
        Assert.AreEqual(ServiceErrorCodes.Unexpected, (await scope.Service.OpenReadAsync("sales-standard", 1)).Error.Code);
    }

    /// <summary>清单缺失、格式错误、重复属性或必填列表缺失均作为服务器故障。</summary>
    [TestMethod]
    [DataRow("missing")]
    [DataRow("{broken")]
    [DataRow("{\"formatVersion\":2,\"templates\":[]}")]
    [DataRow("{\"formatVersion\":1}")]
    [DataRow("{\"formatVersion\":1,\"templates\":null}")]
    [DataRow("{\"formatVersion\":1,\"formatVersion\":1,\"templates\":[]}")]
    public async Task InvalidManifest_ShouldReturnUnexpected(string json)
    {
        using var scope = new ExportTemplateTestScope();
        if (json != "missing") await scope.WriteAsync("模板目录.json", Encoding.UTF8.GetBytes(json));
        var result = await scope.Service.ListAsync("SO", "SalesOrder.Export", 1);
        Assert.AreEqual(ServiceErrorCodes.Unexpected, result.Error.Code);
        Assert.IsFalse(result.Message.Contains(scope.Root, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>重复标识、缺失必填值或错误元数据会使整个清单失败，包括停用条目。</summary>
    [TestMethod]
    [DataRow("duplicate")]
    [DataRow("bad-id")]
    [DataRow("bad-hash")]
    [DataRow("missing-enabled")]
    [DataRow("long-name")]
    [DataRow("long-type")]
    [DataRow("long-key")]
    [DataRow("spaced-name")]
    [DataRow("spaced-type")]
    [DataRow("spaced-key")]
    public async Task InvalidMetadata_ShouldRejectEntireManifest(string mode)
    {
        using var scope = new ExportTemplateTestScope();
        var first = scope.Item();
        var second = scope.Item("second", enabled: false);
        switch (mode)
        {
            case "duplicate": second["templateId"] = "sales-standard"; break;
            case "bad-id": second["templateId"] = "../bad"; break;
            case "bad-hash": second["sha256"] = new string('Z', 64); break;
            case "missing-enabled": second.Remove("enabled"); break;
            case "long-name": second["displayName"] = new string('名', 201); break;
            case "long-type": second["documentType"] = new string('x', 65); break;
            case "long-key": second["dataContractKey"] = new string('x', 129); break;
            case "spaced-name": second["displayName"] = " 标准销售单"; break;
            case "spaced-type": second["documentType"] = "SO "; break;
            case "spaced-key": second["dataContractKey"] = " SalesOrder.Export"; break;
        }
        await scope.PublishAsync(first);
        await scope.WriteManifestAsync(first, second);
        Assert.AreEqual(ServiceErrorCodes.Unexpected, (await scope.Service.ListAsync("SO", "SalesOrder.Export", 1)).Error.Code);
    }

    /// <summary>长度和摘要独立验证，正确摘要的非 XLSX 内容也不能通过容器检查。</summary>
    [TestMethod]
    [DataRow("length")]
    [DataRow("hash")]
    [DataRow("not-xlsx")]
    public async Task InvalidFile_ShouldRejectLengthHashAndPackage(string mode)
    {
        using var scope = new ExportTemplateTestScope();
        var item = scope.Item();
        await scope.PublishAsync(item);
        if (mode == "length") item["fileLength"] = scope.Xlsx.Length + 1;
        if (mode == "hash") item["sha256"] = new string('0', 64);
        if (mode == "not-xlsx")
        {
            byte[] bytes = [1, 2, 3, 4];
            await scope.WriteAsync("sales-standard/v1/模板.xlsx", bytes);
            item["fileLength"] = bytes.Length;
            item["sha256"] = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
        }
        await scope.WriteManifestAsync(item);
        Assert.AreEqual(ServiceErrorCodes.Unexpected, (await scope.Service.ListAsync("SO", "SalesOrder.Export", 1)).Error.Code);
    }

    /// <summary>取消保留 OperationCanceledException 语义，不转换为服务器错误。</summary>
    [TestMethod]
    public async Task Cancellation_ShouldRemainCancellation()
    {
        using var scope = new ExportTemplateTestScope();
        using var source = new CancellationTokenSource();
        source.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => scope.Service.ListAsync("SO", "SalesOrder.Export", 1, source.Token));
        Assert.AreEqual(0, scope.Provider.Calls);
    }

    /// <summary>发布工具可写 UTF-8 BOM，服务器必须正确读取同一协议。</summary>
    [TestMethod]
    public async Task Utf8BomManifest_ShouldBeAccepted()
    {
        using var scope = new ExportTemplateTestScope();
        await scope.PublishAsync(scope.Item());
        using var source = await scope.Storage.OpenReadAsync("模板目录.json");
        using var buffer = new MemoryStream();
        await source.CopyToAsync(buffer);
        await scope.WriteAsync("模板目录.json", [0xEF, 0xBB, 0xBF, .. buffer.ToArray()]);
        var result = await scope.Service.ListAsync("SO", "SalesOrder.Export", 1);
        Assert.IsTrue(result.Success);
        Assert.HasCount(1, result.Data);
    }

    /// <summary>坏 UTF-8 字节不能悄悄替换为其他字符后成为有效目录。</summary>
    [TestMethod]
    public async Task MalformedUtf8Manifest_ShouldFail()
    {
        using var scope = new ExportTemplateTestScope();
        var item = scope.Item();
        item["displayName"] = "INVALID";
        await scope.PublishAsync(item);
        using var source = await scope.Storage.OpenReadAsync("模板目录.json");
        using var buffer = new MemoryStream();
        await source.CopyToAsync(buffer);
        var bytes = buffer.ToArray();
        var index = Encoding.UTF8.GetString(bytes).IndexOf("INVALID", StringComparison.Ordinal);
        Assert.IsTrue(index >= 0);
        bytes[index] = 0xFF;
        await scope.WriteAsync("模板目录.json", bytes);
        Assert.AreEqual(ServiceErrorCodes.Unexpected, (await scope.Service.ListAsync("SO", "SalesOrder.Export", 1)).Error.Code);
    }
}
