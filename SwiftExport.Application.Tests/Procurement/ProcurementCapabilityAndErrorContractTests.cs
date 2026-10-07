using Microsoft.VisualStudio.TestTools.UnitTesting;
using Refit;
using SwiftExport.Contracts.Apis.Procurement;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderDetailDto;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Infrastructure.Repositories.Procurement.PurchaseOrders.Query;

namespace SwiftExport.Application.Tests.Procurement;

/// <summary>锁定新采购统一结果和稳定错误分类，防止客户端退回解析消息文本。</summary>
[TestClass]
public sealed class ProcurementCapabilityAndErrorContractTests
{
    /// <summary>采购 API 全部使用通用服务结果，且不再暴露全局能力快照端点。</summary>
    [TestMethod]
    public void Procurement_api_uses_only_common_service_results()
    {
        var methods = typeof(IProcurementOrderApi).GetMethods();

        Assert.IsGreaterThan(0, methods.Length);
        foreach (var method in methods)
        {
            Assert.IsTrue(method.ReturnType.IsGenericType, method.Name);
            Assert.AreEqual(typeof(Task<>), method.ReturnType.GetGenericTypeDefinition(), method.Name);
            var resultType = method.ReturnType.GetGenericArguments()[0];
            Assert.IsTrue(resultType.IsGenericType, method.Name);
            Assert.AreEqual(typeof(ServiceResult<>), resultType.GetGenericTypeDefinition(), method.Name);
        }

        Assert.IsFalse(methods.Any(x => x.Name.Contains("Capabilities", StringComparison.Ordinal)));
    }

    /// <summary>通用结果同时保留展示消息和机器可判定错误。</summary>
    [TestMethod]
    public void Service_result_exposes_machine_code_and_field()
    {
        var result = ServiceResult<object>.Fail(
            ServiceErrorCodes.VersionConflict, "版本冲突。", "ExpectedVersion");

        Assert.IsFalse(result.Success);
        Assert.IsNull(result.Data);
        Assert.AreEqual("版本冲突。", result.Message);
        Assert.AreEqual(ServiceErrorCodes.VersionConflict, result.Error.Code);
        Assert.AreEqual("ExpectedVersion", result.Error.Field);
    }

    /// <summary>BOM 指纹变化和字段校验通过类型化异常保留稳定分类。</summary>
    [TestMethod]
    public void Typed_failures_do_not_require_message_parsing()
    {
        var validation = ProcurementOperationException.Validation("命令标识不能为空。", "CommandId");
        var changed = new ProcurementOperationException(
            ProcurementErrorCodes.BomBasisChanged, "确认依据已改变。", "ContextFingerprint");

        Assert.AreEqual(ServiceErrorCodes.Validation, validation.Code);
        Assert.AreEqual("CommandId", validation.Field);
        Assert.AreEqual(ProcurementErrorCodes.BomBasisChanged, changed.Code);
        Assert.AreEqual("ContextFingerprint", changed.Field);
    }

    /// <summary>三个反向操作分别暴露命令和预览，禁止退回旧的未开放占位行为。</summary>
    [TestMethod]
    public void Reversal_routes_are_exposed_as_separate_commands()
    {
        var controller = File.ReadAllText(Path.Combine(
            Root(), "SwiftExport.WebApi", "Controllers", "Procurement", "PurchaseOrders", "Reversal", "ProcurementOrderReversalController.cs"));
        foreach (var name in new[] { "CancelConfirmationAsync", "VoidAsync", "DeleteLinesAsync", "PreviewCancelConfirmationAsync", "PreviewVoidAsync", "PreviewDeleteLinesAsync" })
            Assert.IsNotNull(typeof(IProcurementOrderApi).GetMethod(name));
        foreach (var route in new[] { "cancel-confirmation", "void", "delete-lines" })
            StringAssert.Contains(controller, route);
    }

    /// <summary>新链详情恢复真实采购头行字段，不把跟进、追溯或库存能力并入详情契约。</summary>
    [TestMethod]
    public void Detail_contract_covers_entity_fields_without_absorbing_other_capabilities()
    {
        var method = typeof(IProcurementOrderApi).GetMethod(nameof(IProcurementOrderApi.GetDetailAsync));
        var serviceResultType = method!.ReturnType.GetGenericArguments().Single();
        Assert.AreEqual(typeof(PurchaseOrderDetailDto), serviceResultType.GetGenericArguments().Single());

        var head = typeof(PurchaseOrderDetailDto).GetProperties().Select(x => x.Name).ToArray();
        foreach (var name in new[] { "BoNo", "CustomerPoNo", "DemandSourceNo", "SalesPerson",
                     "PurchasePerson", "InletHoseType", "AeratorType", "PalletType", "BoxType", "LabelType",
                     "InstructionManualType", "CartonType", "LogoType", "ReviewedBy", "PurchaseOrderFolder",
                     "DiscountRemark", "ExpectedDate", "ReplyDate", "ConfirmedDate", "ConfirmedBy", "CreatedDate", "UpdatedDate" })
            Assert.Contains(name, head);
        var line = typeof(PurchaseOrderLineDetailDto).GetProperties().Select(x => x.Name).ToArray();
        foreach (var name in new[] { "CustomerSku", "LinkSku", "FinishZh", "FeaturesZh", "MaterialType", "SupplyType",
                     "SourceType", "SourceNo", "SourceUid", "SourceLineId", "SourceLineNumber" })
            Assert.Contains(name, line);
        foreach (var forbidden in new[] { "LatestProgress", "ProgressHistory", "InventoryLedger", "InboundLines", "Operations", "Trace" })
            Assert.DoesNotContain(forbidden, line.Concat(head).ToArray());
    }

    /// <summary>查询 SQL 必须直接选择详情契约所需的真实采购头行字段。</summary>
    [TestMethod]
    public void Detail_sql_has_real_sources_for_restored_fields()
    {
        var source = File.ReadAllText(Path.Combine(Root(), "SwiftExport.Infrastructure", "Repositories", "Procurement", "PurchaseOrders", "Query", "PostgreSqlProcurementOrderQueryRepository.cs"));
        foreach (var field in new[] { ("BoNo", "BoNo", "bo_no"), ("CustomerPoNo", "CustomerPoNo", "customer_po_no"),
                     ("SoPerson", "SalesPerson", "so_person"), ("InletrHoseType", "InletHoseType", "inletr_hose_type") })
        {
            StringAssert.Contains(source, "h.\"" + field.Item3 + "\" AS \"" + field.Item2 + "\"");
        }
        foreach (var field in new[] { ("CustomerSku", "customer_sku"), ("LinkSku", "link_sku"), ("FinishZh", "finish_zh"),
                     ("FeaturesZh", "features_zh"), ("MaterialType", "material_type"), ("SupplyType", "supply_type"),
                     ("SourceUid", "source_uid"), ("SourceLineId", "source_line_id") })
        {
            StringAssert.Contains(source, "l.\"" + field.Item2 + "\" AS \"" + field.Item1 + "\"");
        }
    }

    private static string Root()
    {
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) return directory.FullName;
        Assert.Fail("无法定位仓库根目录。"); return string.Empty;
    }
}



