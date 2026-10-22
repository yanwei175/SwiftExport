using SwiftExport.Application.Interfaces.Systems.Numbering;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using FluentValidation;
using Mapster;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Commands;
using SwiftExport.Application.Commands.Validation;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Services.ForExcel;
using SwiftExport.Application.Interfaces;
using SwiftExport.Application.Interfaces.Auth;
using SwiftExport.Application.Interfaces.Auth.Permissions.Evaluation;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.ForExcel.PurchaseOrders;
using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderDetailDto;
using SwiftExport.Contracts.DTOs.SystemDtos.Requests;
using SwiftExport.Contracts.DTOs.SystemDtos.Responses;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Auditing;
using SwiftExport.Core.Entities.Commands;
using SwiftExport.Core.Entities.Procurement;
using SwiftExport.Core.Interfaces.Auditing;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Commands;
using SwiftExport.Core.Interfaces.ForExcel;
using SwiftExport.Core.Interfaces.Procurement;
using SwiftExport.Core.Models.Procurement;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Infrastructure.Data;

namespace SwiftExport.Application.Tests.Procurement;

/// <summary>以可观察事务和仓储替身验证采购草稿入口接入公共管道后的授权、重放与回滚。</summary>
[TestClass]
public sealed class ProcurementOrderCommandOrchestrationTests
{
    /// <summary>手工明确产品身份时由后端读取商务快照，无客户关系或同客户型号有歧义都不触发导入匹配。</summary>
    [TestMethod]
    [DataRow("")]
    [DataRow("DUPLICATE")]
    public async Task DirectProductCreationUsesSnapshotWithoutImportSelection(string customerSku)
    {
        var f=new Fixture{AllowCreation=true,SnapshotCustomerSku=customerSku};f.PrepareConfirmation();
        f.ImportCandidates=[Candidate(f.ProductId,customerSku),Candidate(Guid.NewGuid(),customerSku)];
        var command=f.CreateCommand();
        command.Lines[0].IsNonStandard=false;command.Lines[0].ProductId=f.ProductId;
        var result=await f.Drafts.CreateAsync(command);
        Assert.IsTrue(result.Success,result.Message);
        Assert.AreEqual(f.ProductId,f.CreatedLines.Single().ProductId);
        Assert.AreEqual(string.IsNullOrEmpty(customerSku)?null:customerSku,f.CreatedLines.Single().CustomerSku);
        Assert.AreEqual(1,f.CreateWrites);Assert.AreEqual(1,f.NumberCalls);
    }

    /// <summary>真正导入仍要求型号、选定关联及真实产品一致，不能借手工创建分支绕过歧义或伪造身份。</summary>
    [TestMethod]
    [DataRow("ambiguous")]
    [DataRow("wrong-selection")]
    [DataRow("wrong-product")]
    [DataRow("missing-sku")]
    public async Task ImportCreationKeepsCustomerModelAndProductIdentityChecks(string fault)
    {
        var f=new Fixture{AllowCreation=true};f.PrepareConfirmation();
        var first=Candidate(f.ProductId,"MODEL");
        f.ImportCandidates=fault=="ambiguous"?[first,Candidate(Guid.NewGuid(),"MODEL")]:[first];
        var command=f.CreateCommand();var line=command.Lines[0];
        line.IsNonStandard=false;line.ProductId=f.ProductId;line.CustomerSku="MODEL";
        if(fault=="wrong-selection")line.SelectedCustomerProductId=Guid.NewGuid();
        if(fault=="wrong-product"){f.ImportCandidates=[Candidate(Guid.NewGuid(),"MODEL")];line.SelectedCustomerProductId=f.ImportCandidates[0].CustomerProductId;}
        if(fault=="missing-sku"){line.CustomerSku="";line.SelectedCustomerProductId=first.CustomerProductId;}
        var result=await f.Drafts.CreateAsync(command);
        Assert.IsFalse(result.Success);
        Assert.AreEqual(fault=="ambiguous"?ProcurementErrorCodes.ProductAmbiguous:fault=="wrong-selection"?ProcurementErrorCodes.SelectionInvalid
            :fault=="missing-sku"?ProcurementErrorCodes.ProductNotMatched:ServiceErrorCodes.Validation,result.Error.Code);
        Assert.AreEqual(0,f.CreateWrites);Assert.AreEqual(0,f.NumberCalls);Assert.IsEmpty(f.Receipts.Rows);
    }

    /// <summary>创建用于真实导入匹配的有效候选，不借SKU文本代替产品及关联身份。</summary>
    private static PurchaseOrderImportProductCandidateDto Candidate(Guid productId,string customerSku)=>new()
    {CustomerProductId=Guid.NewGuid(),ProductId=productId,CustomerSku=customerSku,IsUsable=true,Sku="P",Unit="PCS"};

    /// <summary>导入预检通过实际适配调用同一价格规则，字段定位保持单行协议，明确零价可以保存。</summary>
    [TestMethod]
    [DataRow("missing-unit", "UnitPrice")]
    [DataRow("missing-tax", "TaxPrice")]
    [DataRow("precision-unit", "UnitPrice")]
    [DataRow("precision-tax", "TaxPrice")]
    [DataRow("zero", "")]
    public async Task ImportCheckUsesRequiredPricesAndPreservesLineFieldPaths(string fault, string field)
    {
        var customerId = Guid.NewGuid(); var supplierId = Guid.NewGuid();
        var repository = Proxy<IForExcelPurchaseOrderRepository>.New((method, _) => method.Name switch
        {
            "FindCustomersAsync" => Task.FromResult<IReadOnlyList<PurchaseOrderImportCustomerDto>>([new() { Uid = customerId, IsActive = true, CustomerCode = "C" }]),
            "FindSupplierAsync" => Task.FromResult<PurchaseOrderImportSupplierDto?>(new() { Uid = supplierId, IsActive = true, SupplierCode = "S", SupplierName = "供应商" }),
            "MatchProductsAsync" => Task.FromResult<IReadOnlyList<PurchaseOrderImportProductCandidateDto>>([]),
            "FindUnitAsync" => Task.FromResult<string?>("PCS"),
            _ => throw new AssertFailedException(method.Name)
        });
        var permissions = Proxy<IPermissionEvaluationService>.New((_, _) => Task.FromResult(true));
        var orders = Proxy<IProcurementOrderDocumentRepository>.New((method, _) => method.Name == "BoNoExistsAsync"
            ? Task.FromResult(false) : throw new AssertFailedException(method.Name));
        var service = new ForExcelPurchaseOrderService(repository, new UserStub(), permissions, orders);
        var request = new CheckPurchaseOrderImportRequest
        {
            CustomerCode = "C", SupplierId = supplierId, Header = new() { OrderDate = DateTime.Today },
            Lines = [new() { ClientLineId = Guid.NewGuid(), IsNonStandard = true, DescriptionZh = "非标配件", Unit = "PCS", Quantity = 1, UnitPrice = 0, TaxPrice = 0 }]
        };
        var line = request.Lines.Single();
        switch (fault)
        {
            case "missing-unit": line.UnitPrice = null; break;
            case "missing-tax": line.TaxPrice = null; break;
            case "precision-unit": line.UnitPrice = 0.00001m; break;
            case "precision-tax": line.TaxPrice = 0.00001m; break;
        }
        var result = await service.CheckAsync(request);
        Assert.IsTrue(result.Success, result.Message);
        Assert.AreEqual(fault == "zero", result.Data.CanSave);
        if (fault != "zero") Assert.AreEqual(field, result.Data.Lines.Single().Issues.Single().Field);
    }

    /// <summary>预检与正式确认拒绝相同的缺价、非法精度及失真金额，拒绝时不写确认、审计或回执。</summary>
    [TestMethod]
    [DataRow("missing-unit")]
    [DataRow("missing-tax")]
    [DataRow("missing-both")]
    [DataRow("negative-unit")]
    [DataRow("negative-tax")]
    [DataRow("precision-unit")]
    [DataRow("precision-tax")]
    [DataRow("amount-unit")]
    [DataRow("amount-tax")]
    public async Task ConfirmationPreviewAndCommitRejectTheSameInvalidPrices(string fault)
    {
        var f = new Fixture();
        f.PrepareConfirmation();
        var line = f.Facts!.Lines.Single();
        switch (fault)
        {
            case "missing-unit": line.UnitPrice = null; line.UnitAmount = null; break;
            case "missing-tax": line.TaxPrice = null; line.TaxAmount = null; break;
            case "missing-both": line.UnitPrice = null; line.TaxPrice = null; line.UnitAmount = null; line.TaxAmount = null; break;
            case "negative-unit": line.UnitPrice = -1; line.UnitAmount = -1; break;
            case "negative-tax": line.TaxPrice = -1; line.TaxAmount = -1; break;
            case "precision-unit": line.UnitPrice = 0.00001m; break;
            case "precision-tax": line.TaxPrice = 0.00001m; break;
            case "amount-unit": line.UnitAmount = 1; break;
            case "amount-tax": line.TaxAmount = 1; break;
        }
        var preview = await f.Confirmations.GetConfirmationContextAsync(f.Head!.Uid);
        Assert.IsFalse(preview.Success, fault);
        var result = await f.Confirmations.ConfirmAsync(f.Head.Uid, new()
        {
            CommandId = Guid.NewGuid(), ExpectedVersion = 1, ContextFingerprint = "unused-invalid-basis"
        });
        Assert.IsFalse(result.Success, fault);
        Assert.AreEqual(preview.Error!.Code, result.Error!.Code, fault);
        Assert.AreEqual(preview.Error.Field, result.Error.Field, fault);
        Assert.AreEqual(0, f.ConfirmCalls);
        Assert.AreEqual(0, f.LogCalls);
        Assert.IsEmpty(f.Receipts.Rows);
        Assert.IsNull(f.Session.Transaction);
    }

    /// <summary>正式创建在事务内复核 BONO；发现重复时不取号、不写头行和回执。</summary>
    [TestMethod]
    public async Task DuplicateBoNoRejectsCreateBeforeWrites()
    {
        var f = new Fixture { AllowCreation = true, DuplicateBoNo = true }; f.PrepareConfirmation();
        var result = await f.Drafts.CreateAsync(f.CreateCommand());
        Assert.IsFalse(result.Success); Assert.AreEqual(ServiceErrorCodes.Validation, result.Error.Code);
        StringAssert.Contains(result.Error.Message, "BONO");
        Assert.AreEqual(1, f.BoNoChecks); Assert.AreEqual(0, f.NumberCalls); Assert.AreEqual(0, f.CreateWrites);
        Assert.AreEqual(0, f.LogCalls); Assert.AreEqual(0, f.Receipts.Rows.Count);
        Assert.AreEqual(1, f.Connection.Rollbacks); Assert.AreEqual(0, f.Connection.Commits);
    }
    /// <summary>预检查后发生唯一约束冲突也明确拒绝，不能误判成未知结果或提交回执。</summary>
    [TestMethod]
    public async Task ConcurrentBoNoConflictRollsBackCreate()
    {
        var f = new Fixture { AllowCreation = true, CreateWriteConflict = true }; f.PrepareConfirmation();
        var result = await f.Drafts.CreateAsync(f.CreateCommand());
        Assert.AreEqual(ServiceErrorCodes.Validation, result.Error.Code); Assert.AreEqual(1, f.CreateWrites);
        Assert.AreEqual(0, f.LogCalls); Assert.AreEqual(0, f.Receipts.Rows.Count);
        Assert.AreEqual(1, f.Connection.Rollbacks); Assert.IsNull(f.Session.Transaction);
    }
    /// <summary>成功命令重试从回执返回，新增的占号检查不阻止原意图重放。</summary>
    [TestMethod]
    public async Task SuccessfulCreateReplaySkipsBoNoLookup()
    {
        var f = new Fixture { AllowCreation = true }; f.PrepareConfirmation(); var command = f.CreateCommand();
        var first = await f.Drafts.CreateAsync(command); Assert.IsTrue(first.Success, first.Message);
        f.DuplicateBoNo = true;
        var replay = await f.Drafts.CreateAsync(command); Assert.IsTrue(replay.Success, replay.Message);
        Assert.AreEqual(first.Data.PurchaseOrderId, replay.Data.PurchaseOrderId);
        Assert.AreEqual(1, f.BoNoChecks); Assert.AreEqual(1, f.CreateWrites); Assert.AreEqual(1, f.NumberCalls);
    }
    /// <summary>细权限拒绝发生在请求验证、命令锁和取号之前。</summary>
    [TestMethod]
    public async Task DeniedPermission_DoesNotEnterCommandTransaction()
    {
        var f = new Fixture { Allow = false };
        var result = await f.Drafts.CreateAsync(new CreateIndependentPurchaseOrderCommand());
        Assert.AreEqual(ServiceErrorCodes.Forbidden, result.Error.Code);
        Assert.AreEqual(0, f.Connection.Begins);
        Assert.AreEqual(0, f.Receipts.Locks);
        Assert.AreEqual(0, f.NumberCalls);
    }

    /// <summary>相同命令重放既有结果，修改业务输入会冲突，两个路径均不重取单号。</summary>
    [TestMethod]
    public async Task CreateReplayAndConflict_DoNotRunBusinessOrNumbering()
    {
        var f = new Fixture();
        var command = new CreateIndependentPurchaseOrderCommand
        {
            CommandId = Guid.NewGuid(), OrderDate = new DateTime(2026, 9, 24),
            Lines = [new CreateIndependentPurchaseOrderLineCommand()]
        };
        var original = new ProcurementOrderCommandResult { PurchaseOrderId = Guid.NewGuid(), OrderNo = "PO-TEST", Version = 1 };
        await f.Executor.ExecuteAsync(command.CommandId, "Procurement.Order.Create", null,
            IndependentPurchaseOrderInputPolicy.FingerprintInput(command),
            _ => Task.FromResult(original), default, IsolationLevel.Serializable);

        var replay = await f.Drafts.CreateAsync(command);
        Assert.IsTrue(replay.Success);
        Assert.AreEqual(original.PurchaseOrderId, replay.Data.PurchaseOrderId);
        Assert.AreEqual("PO-TEST", replay.Data.OrderNo);
        Assert.AreEqual(0, f.NumberCalls);
        Assert.AreEqual(0, f.BusinessReads);
        Assert.AreEqual(0, f.LogCalls);
        Assert.AreEqual(IsolationLevel.Serializable, f.Connection.LastIsolation);

        command.Remark = "不同内容";
        var conflict = await f.Drafts.CreateAsync(command);
        Assert.AreEqual(ServiceErrorCodes.CommandConflict, conflict.Error.Code);
        Assert.AreEqual(0, f.NumberCalls);
        Assert.AreEqual(0, f.BusinessReads);
        Assert.AreEqual(0, f.LogCalls);
        Assert.AreEqual(2, f.Connection.Commits);
    }

    /// <summary>草稿版本冲突和创建业务失败均由公共执行器回滚，事务指针随后清空。</summary>
    [TestMethod]
    public async Task VersionConflictAndCreateFailure_RollBack()
    {
        var f = new Fixture();
        f.Head = new PurchaseOrderHead { Uid = Guid.NewGuid(), Version = 2 };
        var version = await f.Drafts.SaveAsync(f.Head.Uid, new UpdatePurchaseOrderDraftCommand
        {
            CommandId = Guid.NewGuid(), ExpectedVersion = 1
        });
        Assert.AreEqual(ServiceErrorCodes.VersionConflict, version.Error.Code);
        Assert.AreEqual(1, f.Connection.Rollbacks);
        Assert.IsNull(f.Session.Transaction);

        var failure = await f.Drafts.CreateAsync(new CreateIndependentPurchaseOrderCommand
        {
            CommandId = Guid.NewGuid(), OrderDate = DateTime.Today,
            Lines = [new CreateIndependentPurchaseOrderLineCommand()]
        });
        Assert.AreEqual(ServiceErrorCodes.BusinessBlocked, failure.Error.Code);
        Assert.AreEqual(2, f.Connection.Rollbacks);
        Assert.AreEqual(0, f.NumberCalls);
        Assert.IsNull(f.Session.Transaction);
    }

    /// <summary>确认依据失效先阻断写入；日志失败则回滚确认和公共回执。</summary>
    [TestMethod]
    public async Task ConfirmationBasisAndAuditFailure_RollBack()
    {
        var f = new Fixture();
        f.PrepareConfirmation();
        var stale = await f.Confirmations.ConfirmAsync(f.Head!.Uid, new ConfirmProcurementOrderCommand
        {
            CommandId = Guid.NewGuid(), ExpectedVersion = 1, ContextFingerprint = "过期依据"
        });
        Assert.AreEqual(ProcurementErrorCodes.BomBasisChanged, stale.Error.Code);
        Assert.AreEqual(0, f.ConfirmCalls);
        Assert.AreEqual(1, f.Connection.Rollbacks);

        var basis = await f.Builder.BuildAsync(f.Facts!, default);
        f.LogResult = 0;
        var failedAudit = await f.Confirmations.ConfirmAsync(f.Head.Uid, new ConfirmProcurementOrderCommand
        {
            CommandId = Guid.NewGuid(), ExpectedVersion = 1, ContextFingerprint = basis.Fingerprint
        });
        Assert.AreEqual(ServiceErrorCodes.BusinessBlocked, failedAudit.Error.Code);
        Assert.AreEqual(1, f.ConfirmCalls);
        Assert.AreEqual(2, f.Connection.Rollbacks);
        Assert.AreEqual(0, f.Receipts.Rows.Count);
        Assert.AreEqual(0, f.Connection.Commits);
        Assert.IsNull(f.Session.Transaction);
    }

    /// <summary>确认成功后重放原结果；公共回执写入失败时不提交确认事务。</summary>
    [TestMethod]
    public async Task ConfirmSuccessReplayAndReceiptFailure_KeepSingleBusinessWrite()
    {
        var f = new Fixture();
        f.PrepareConfirmation();
        var basis = await f.Builder.BuildAsync(f.Facts!, default);
        var command = new ConfirmProcurementOrderCommand
        {
            CommandId = Guid.NewGuid(), ExpectedVersion = 1, ContextFingerprint = basis.Fingerprint
        };
        var first = await f.Confirmations.ConfirmAsync(f.Head!.Uid, command);
        var replay = await f.Confirmations.ConfirmAsync(f.Head.Uid, command);
        Assert.IsTrue(first.Success);
        Assert.IsTrue(replay.Success);
        Assert.AreEqual(first.Data.ConfirmationId, replay.Data.ConfirmationId);
        Assert.AreEqual(1, f.ConfirmCalls);
        Assert.AreEqual(1, f.LogCalls);
        Assert.AreEqual(2, f.Connection.Commits);
        Assert.AreEqual(IsolationLevel.Serializable, f.Connection.LastIsolation);

        var failure = new Fixture();
        failure.PrepareConfirmation();
        var freshBasis = await failure.Builder.BuildAsync(failure.Facts!, default);
        failure.Receipts.FailInsert = true;
        var failed = await failure.Confirmations.ConfirmAsync(failure.Head!.Uid, new ConfirmProcurementOrderCommand
        {
            CommandId = Guid.NewGuid(), ExpectedVersion = 1, ContextFingerprint = freshBasis.Fingerprint
        });
        Assert.IsFalse(failed.Success);
        Assert.AreEqual(1, failure.Connection.Rollbacks);
        Assert.AreEqual(0, failure.Connection.Commits);
        Assert.AreEqual(0, failure.Receipts.Rows.Count);
        Assert.IsNull(failure.Session.Transaction);
    }

    /// <summary>仓储报告保存或确认冲突时，先回滚再返回版本错误，不写成功审计及回执。</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task RepositoryWriteConflict_StopsAuditAndReceipt(bool confirm)
    {
        var f = new Fixture { FailConfirm = true, FailSave = true };
        f.PrepareConfirmation();
        var line = f.Facts!.Lines.Single();
        var basis = await f.Builder.BuildAsync(f.Facts, default);
        var result = confirm
            ? await f.Confirmations.ConfirmAsync(f.Head!.Uid, new() { CommandId = Guid.NewGuid(), ExpectedVersion = 1, ContextFingerprint = basis.Fingerprint })
            : await f.Drafts.SaveAsync(f.Head!.Uid, new UpdatePurchaseOrderDraftCommand
            {
                CommandId = Guid.NewGuid(), ExpectedVersion = 1,
                Lines = [new() { PurchaseOrderLineId = line.Uid, ExpectedVersion = 1, UnitPrice = 0, TaxPrice = 0 }]
            });
        Assert.AreEqual(ServiceErrorCodes.VersionConflict, result.Error.Code);
        Assert.AreEqual("ExpectedVersion", result.Error.Field);
        Assert.AreEqual(1, f.ConfirmCalls + f.SaveCalls);
        Assert.AreEqual(0, f.LogCalls);
        Assert.AreEqual(0, f.Receipts.Rows.Count);
        Assert.AreEqual(0, f.Connection.Commits);
        Assert.AreEqual(1, f.Connection.Rollbacks);
        Assert.IsNull(f.Session.Transaction);
    }

    /// <summary>确认预览和提交共同拒绝未完整锁定的事实，读取与写命令均清理事务。</summary>
    [TestMethod]
    public async Task LockConflict_BlocksConfirmationPreviewAndCommit()
    {
        var f = new Fixture { LockError = new ServiceErrorDto { Code = "CONCURRENCY_CONFLICT", Message = "引用变化" } };
        f.PrepareConfirmation();
        var preview = await f.Confirmations.GetConfirmationContextAsync(f.Head!.Uid);
        var result = await f.Confirmations.ConfirmAsync(f.Head.Uid, new() { CommandId = Guid.NewGuid(), ExpectedVersion = 1, ContextFingerprint = "basis" });
        Assert.AreEqual(ServiceErrorCodes.VersionConflict, preview.Error.Code);
        Assert.AreEqual(ServiceErrorCodes.VersionConflict, result.Error.Code);
        Assert.AreEqual("ExpectedVersion", preview.Error.Field);
        Assert.AreEqual("ExpectedVersion", result.Error.Field);
        Assert.AreEqual(0, f.ConfirmCalls);
        Assert.AreEqual(0, f.LogCalls);
        Assert.AreEqual(0, f.Receipts.Rows.Count);
        Assert.AreEqual(2, f.Connection.Rollbacks);
        Assert.AreEqual(0, f.Connection.Commits);
        Assert.IsNull(f.Session.Transaction);
    }

    /// <summary>测试替身只模拟调用顺序和事务结果，不代表 SQL Server 锁或持久化。</summary>
    private sealed class Fixture
    {
        public readonly ConnectionStub Connection = new();
        public readonly DbSession Session;
        public readonly ReceiptStub Receipts;
        public readonly BusinessCommandExecutor Executor;
        public readonly ProcurementOrderDraftService Drafts;
        public readonly ProcurementOrderConfirmationService Confirmations;
        public readonly ProcurementConfirmationBuilder Builder;
        public bool Allow = true;
        public bool FailConfirm, FailSave;
        public bool AllowCreation, DuplicateBoNo, CreateWriteConflict;
        public int BoNoChecks, CreateWrites;
        public ServiceErrorDto? LockError;
        public int SaveCalls;
        public int NumberCalls, BusinessReads, LogCalls, ConfirmCalls, LogResult = 1;
        public PurchaseOrderHead? Head;
        public ProcurementReversalFacts? Facts;
        public string SnapshotCustomerSku="";
        public IReadOnlyList<PurchaseOrderImportProductCandidateDto> ImportCandidates=[];
        public IReadOnlyList<PurchaseOrderLine> CreatedLines=[];
        public Guid ProductId=>productId;
        private readonly Guid customerId = Guid.NewGuid(), supplierId = Guid.NewGuid(), productId = Guid.NewGuid();

        public Fixture()
        {
            Session = (DbSession)RuntimeHelpers.GetUninitializedObject(typeof(DbSession));
            typeof(DbSession).GetField("<Connection>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Session, Connection);
            var user = new UserStub();
            Receipts = new ReceiptStub(Session);
            Executor = new BusinessCommandExecutor(new ServiceBaseContext(Session, NullLoggerFactory.Instance), Receipts, user);
            var permissions = Proxy<IPermissionEvaluationService>.New((method, _) => method.Name == "HasPermissionAsync"
                ? Task.FromResult(Allow) : throw new NotSupportedException(method.Name));
            var logs = Proxy<IBusinessOperationLogRepository>.New((method, _) => method.Name == "InsertAsync"
                ? CountLog() : throw new NotSupportedException(method.Name));
            var repository = Proxy<IProcurementOrderDocumentRepository>.New((method, _) => method.Name switch
            {
                "GetHeadAsync" => ReadHead(),
                "GetLinesAsync" => Task.FromResult(Facts?.Lines ?? new List<PurchaseOrderLine>()),
                "SaveAsync" => RecordSave(),
                "ConfirmAsync" => RecordConfirm(),
                "BoNoExistsAsync" => CheckBoNo(),
                "InsertAsync" => CreateHead((PurchaseOrderHead)_[0]!, (IReadOnlyList<PurchaseOrderLine>)_[1]!),
                _ => throw new AssertFailedException("重放或拒绝不应读取采购仓储：" + method.Name)
            });
            var references = Proxy<IProcurementReferenceReadRepository>.New((method, _) => method.Name switch
            {
                "GetCustomerAsync" => ReadCustomer(),
                "GetSupplierAsync" => Task.FromResult(Facts is null ? null : new Supplier { Uid = supplierId, SupplierCode = "S", SupplierName = "供应商", DocRelateNo = "S" }),
                "GetProductAsync" => Task.FromResult(Facts is null ? null : new Product { Uid = productId, Sku = "P", Unit = "PCS", MaterialType = MaterialTypesEnum.零件, SupplyType = SupplyTypeEnums.采购 }),
                "GetProductCandidateAsync" => Task.FromResult<SwiftExport.Contracts.DTOs.MasterData.ProcurementLookups.ProcurementProductCandidateDto?>(new(){Uid=productId,CustomerSku=SnapshotCustomerSku,LinkSku="LINK",Unit="PCS",Sku="P"}),
                "GetUnitAsync" => Task.FromResult<string?>("PCS"),
                _ => throw new AssertFailedException("重放或拒绝不应读取采购资格：" + method.Name)
            });
            var reversal = Proxy<IProcurementReversalRepository>.New((method, _) => method.Name == "LockAsync"
                ? Task.FromResult<(ProcurementReversalFacts?, ServiceErrorDto?)>((LockError is null ? Facts : null, LockError)) : throw new NotSupportedException(method.Name));
            var numbers = Proxy<IDocumentNoService>.New((method, _) => { NumberCalls++; return AllowCreation
                ? Task.FromResult(ServiceResult<DocumentNoResponse>.Ok(new() { DocumentNo = "PO-BONO" }))
                : throw new AssertFailedException("失败或重放不应取号：" + method.Name); });
            var validators = new ServiceCollection()
                .AddSingleton<IValidator<CreateIndependentPurchaseOrderCommand>, CreateIndependentPurchaseOrderCommandValidator>()
                .AddSingleton<IValidator<UpdatePurchaseOrderDraftCommand>, UpdatePurchaseOrderDraftCommandValidator>()
                .AddSingleton<IValidator<ConfirmProcurementOrderCommand>, ConfirmProcurementOrderCommandValidator>()
                .BuildServiceProvider();
            var pipeline = new CommandPipeline(new CommandRequestValidator(validators), new CorrelationStub(), user,
                NullLogger<CommandPipeline>.Instance);
            var support = new ProcurementOrderCommandSupport(permissions, user, logs, new CorrelationStub(),
                NullLogger<ProcurementOrderCommandSupport>.Instance);
            Builder = new ProcurementConfirmationBuilder(references,Proxy<IProcurementDemandAssociationRepository>.New((method,_)=>throw new AssertFailedException("普通采购不应读取客供关系："+method.Name)));
            Drafts = new ProcurementOrderDraftService(repository, references, Builder, numbers,
                user, new TypeAdapterConfig(), Executor, pipeline, support,
                Proxy<SwiftExport.Core.Interfaces.ForExcel.IForExcelPurchaseOrderRepository>.New((method, args) =>
                    AllowCreation && method.Name == "MatchProductsAsync"
                        ? Task.FromResult<IReadOnlyList<PurchaseOrderImportProductCandidateDto>>(ImportCandidates.Where(item=>((string[])args[1]!).Contains(IndependentPurchaseOrderInputPolicy.Normalize(item.CustomerSku))).ToArray())
                        : throw new AssertFailedException("重放或拒绝不应查询导入关联：" + method.Name)));
            Confirmations = new ProcurementOrderConfirmationService(repository, reversal, Builder, new ProcurementOrderReadSupport(new ServiceBaseContext(Session, NullLoggerFactory.Instance), permissions, user), user, Executor, pipeline, support);
        }

        public void PrepareConfirmation()
        {
            Head = new PurchaseOrderHead { Uid = Guid.NewGuid(), Version = 1, OriginKind = 2, CustomerId = customerId, SupplierId = supplierId };
            Facts = new ProcurementReversalFacts { Head = Head, Lines = [new PurchaseOrderLine
            {
                Uid = Guid.NewGuid(), Version = 1, PurchaseOrderId = Head.Uid, ProductId = productId,
                Quantity = 1, UnlinkedQty = 1, UnitPrice = 0, TaxPrice = 0, UnitAmount = 0, TaxAmount = 0,
                UnitSnapshot = "PCS", MaterialType = MaterialTypesEnum.零件, SupplyType = SupplyTypeEnums.采购
            }] };
        }
        /// <summary>非标创建只使用真实双方及启用单位，不需要虚构产品匹配。</summary>
        public CreateIndependentPurchaseOrderCommand CreateCommand() => new() { CommandId = Guid.NewGuid(), CustomerId = customerId,
            SupplierId = supplierId, OrderDate = DateTime.Today, BoNo = "BO-1",
            Lines = [new() { IsNonStandard = true, Unit = "PCS", DescriptionZh = "非标配件", Quantity = 1, UnitPrice = 0, TaxPrice = 0 }] };
        // 观察调用方事务，不能据此声称真实 PostgreSQL 唯一索引已验证。
        private Task<bool> CheckBoNo() { Assert.IsNotNull(Session.Transaction); BoNoChecks++; return Task.FromResult(DuplicateBoNo); }
        private Task<RepositoryWriteResult> CreateHead(PurchaseOrderHead head, IReadOnlyList<PurchaseOrderLine> lines)
        {
            Assert.IsNotNull(Session.Transaction); CreateWrites++;
            CreatedLines=lines;
            return Task.FromResult(CreateWriteConflict ? RepositoryWriteResult.Fail(head.Uid, null, ServiceErrorCodes.Validation,
                "自定义采购单号（BONO）已被未删除的采购订单使用，请修改后重新保存。") : RepositoryWriteResult.Ok(head.Uid, null, 1));
        }

        private Task<int> CountLog() { LogCalls++; return Task.FromResult(LogResult); }
        /// <summary>记录确认写入次数，并模拟仓储返回的成功或版本冲突。</summary>
        private Task<RepositoryWriteResult> RecordConfirm()
        {
            ConfirmCalls++;
            return Task.FromResult(FailConfirm ? RepositoryWriteResult.Fail(Head!.Uid, Head.Version, "CONCURRENCY_CONFLICT", "确认冲突")
                : RepositoryWriteResult.Ok(Head!.Uid, Head.Version, Head.Version + 1));
        }
        /// <summary>记录保存写入次数，验证调用方在仓储冲突后停止审计和回执。</summary>
        private Task<RepositoryWriteResult> RecordSave()
        {
            SaveCalls++;
            return Task.FromResult(FailSave ? RepositoryWriteResult.Fail(Head!.Uid, Head.Version, "CONCURRENCY_CONFLICT", "保存冲突")
                : RepositoryWriteResult.Ok(Head!.Uid, Head.Version, Head.Version + 1));
        }
        private Task<PurchaseOrderHead?> ReadHead() { BusinessReads++; return Task.FromResult(Head); }
        private Task<Customer?> ReadCustomer()
        { BusinessReads++; return Task.FromResult(Facts is null ? null : new Customer { Uid = customerId, CustomerCode = "C", DocRelateNo = "C" }); }
    }

    /// <summary>为无数据库测试构造只处理指定方法的窄接口替身。</summary>
    public class Proxy<T> : DispatchProxy where T : class
    {
        private Func<MethodInfo, object?[], object?> handler = default!;
        public static T New(Func<MethodInfo, object?[], object?> handler)
        {
            var instance = Create<T, Proxy<T>>();
            ((Proxy<T>)(object)instance).handler = handler;
            return instance;
        }
        protected override object? Invoke(MethodInfo? method, object?[]? args) => handler(method!, args ?? []);
    }

    /// <summary>保存可重放结果，并观察命令锁是否在授权之前被误用。</summary>
    private sealed class ReceiptStub(DbSession session) : ICommandReceiptRepository
    {
        public readonly Dictionary<Guid, CommandReceipt> Rows = [];
        public int Locks;
        public bool FailInsert;
        public Task<int> LockAsync(Guid commandId, CancellationToken ct)
        { Assert.IsNotNull(session.Transaction); Locks++; return Task.FromResult(0); }
        public Task<CommandReceipt?> FindAsync(Guid commandId, CancellationToken ct) => Task.FromResult(Rows.GetValueOrDefault(commandId));
        public Task InsertAsync(CommandReceipt receipt, CancellationToken ct)
        { Assert.IsNotNull(session.Transaction); if (FailInsert) throw new InvalidOperationException("模拟回执写入失败"); Rows.Add(receipt.CommandId, receipt); return Task.CompletedTask; }
    }

    /// <summary>模拟一个稳定的已认证操作人。</summary>
    private sealed class UserStub : ICurrentUser
    {
        public Guid? UserId => new Guid("ac484495-23d0-4ead-94b2-622cb90b3b74");
        public string UserName => "采购测试";
        public bool IsAuthenticated => true;
    }

    /// <summary>提供测试链路号，不参与命令指纹。</summary>
    private sealed class CorrelationStub : ICorrelationContext { public string CorrelationId => "test-call"; }

    /// <summary>观察隔离级别、提交和回滚；不模拟实际 SQL 写入。</summary>
    private sealed class ConnectionStub : IDbConnection
    {
        public int Begins, Commits, Rollbacks;
        public IsolationLevel? LastIsolation;
        [AllowNull] public string ConnectionString { get; set; } = "";
        public int ConnectionTimeout => 0;
        public string Database => "stub";
        public ConnectionState State => ConnectionState.Open;
        public IDbTransaction BeginTransaction() { Begins++; LastIsolation = null; return new TransactionStub(this); }
        public IDbTransaction BeginTransaction(IsolationLevel level) { Begins++; LastIsolation = level; return new TransactionStub(this); }
        public void ChangeDatabase(string databaseName) => throw new NotSupportedException();
        public void Close() { }
        public IDbCommand CreateCommand() => throw new NotSupportedException();
        public void Open() { }
        public void Dispose() { }
        private sealed class TransactionStub(ConnectionStub owner) : IDbTransaction
        {
            public IDbConnection Connection => owner;
            public IsolationLevel IsolationLevel => IsolationLevel.Serializable;
            public void Commit() => owner.Commits++;
            public void Rollback() => owner.Rollbacks++;
            public void Dispose() { }
        }
    }
}
