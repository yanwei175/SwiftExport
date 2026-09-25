using System.Data;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using SwiftExport.Application.Commands;
using SwiftExport.Application.Commands.Validation;
using SwiftExport.Application.Services.QualityControl.AfterSales.Confirmation;
using SwiftExport.Application.Services.QualityControl.AfterSales.Query;
using SwiftExport.Application.Services.QualityControl.AfterSales.Maintenance;
using SwiftExport.Application.Services.QualityControl.AfterSales.Lifecycle;
using SwiftExport.Core.Entities.Commands;
using SwiftExport.Core.Interfaces.Commands;
using Mapster;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Interfaces.DemandPlanning.Sources.Reception.Models;
using SwiftExport.Application.Interfaces.DemandPlanning.Sources.Reception;
using SwiftExport.Application.Interfaces.DemandPlanning.Sources.Reception.Exceptions;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces;
using SwiftExport.Application.Interfaces.Auth;
using SwiftExport.Application.Interfaces.DemandPlanning;
using SwiftExport.Application.Services.QualityControl;
using SwiftExport.Contracts.DTOs.QualityControl.AfterSales;
using SwiftExport.Contracts.DTOs.SystemDtos.Responses;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Auditing;
using SwiftExport.Core.Entities.QualityControl;
using SwiftExport.Core.Interfaces.Auditing;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.QualityControl;
using SwiftExport.Core.Interfaces.QualityControl.AfterSales.Document;
using SwiftExport.Core.Interfaces.QualityControl.AfterSales.Query;
using SwiftExport.Core.Interfaces.QualityControl.AfterSales.Lookup;
using SwiftExport.Infrastructure.Data;

namespace SwiftExport.Application.Tests.QualityControl;

/// <summary>真实售后服务的隔离端口测试，不代替SQL Server验证。</summary>
[TestClass]
public sealed class AfterSalesServiceTests
{
    /// <summary>维护回执保留首次创建及保存结果，跨命令复用身份和修改业务输入均冲突。</summary>
    [TestMethod]
    public async Task MaintenanceReceiptsReplayAndRejectCrossCommandReuse()
    {
        using var f = new Fixture();
        var create = f.Command();
        var first = await f.Service.SaveAsync(create.Uid, create, true);
        Assert.IsTrue(first.Success, first.Message);
        var replay = await f.Service.SaveAsync(create.Uid, create, true);
        Assert.IsTrue(replay.Success, replay.Message);
        Assert.IsTrue(replay.Data.AlreadyApplied);
        Assert.AreEqual(1, f.NumberCalls);
        Assert.AreEqual(1, f.Logs.Count);
        var changed = Clone(create)!;
        changed.Header.IssueDescription = "另一投诉内容";
        Assert.AreEqual("COMMAND_CONFLICT", (await f.Service.SaveAsync(create.Uid, changed, true)).Error.Code);
        var update = f.Edit();
        update.CommandId = create.CommandId;
        Assert.AreEqual("COMMAND_CONFLICT", (await f.Service.SaveAsync(update.Uid, update, false)).Error.Code);
        update.CommandId = Guid.NewGuid();
        update.Header.CauseAnalysis = "已核实";
        var saved = await f.Service.SaveAsync(update.Uid, update, false);
        Assert.IsTrue(saved.Success, saved.Message);
        Assert.IsTrue((await f.Service.SaveAsync(update.Uid, update, false)).Data.AlreadyApplied);
        Assert.AreEqual(2, f.Logs.Count);
    }

    /// <summary>结案后原命令可以重放，异原因或异命令即使目标相同也不能取得旧回执。</summary>
    [TestMethod]
    public async Task LifecycleReceiptReplaysAfterStateChange()
    {
        using var f = new Fixture();
        var create = f.Command();
        create.AddedLines.Clear();
        var saved = await f.Service.SaveAsync(create.Uid, create, true);
        var close = new CloseAfterSalesCommand { CommandId = Guid.NewGuid(), Version = saved.Data.Version, Reason = "投诉处理完毕" };
        var first = await f.Service.CloseAsync(create.Uid, close);
        Assert.IsTrue(first.Success, first.Message);
        Assert.AreEqual(DocumentStatusEnum.Closed, f.Head!.DocumentStatus);
        var replay = await f.Service.CloseAsync(create.Uid, close);
        Assert.IsTrue(replay.Success, replay.Message);
        Assert.IsTrue(replay.Data.AlreadyApplied);
        var changed = new CloseAfterSalesCommand { CommandId = close.CommandId, Version = close.Version, Reason = "不同原因" };
        Assert.AreEqual("COMMAND_CONFLICT", (await f.Service.CloseAsync(create.Uid, changed)).Error.Code);
        var reopen = new ReopenAfterSalesCommand { CommandId = Guid.NewGuid(), Version = first.Data.Version, Reason = "继续处理" };
        var reopened = await f.Service.ReopenAsync(create.Uid, reopen);
        Assert.IsTrue(reopened.Success, reopened.Message);
        Assert.AreEqual(DocumentStatusEnum.Open, f.Head!.DocumentStatus);
        Assert.IsTrue((await f.Service.ReopenAsync(create.Uid, reopen)).Data.AlreadyApplied);
        var voidCommand = new VoidAfterSalesCommand { CommandId = Guid.NewGuid(), Version = reopened.Data.Version, Reason = "不再受理" };
        var voided = await f.Service.VoidAsync(create.Uid, voidCommand);
        Assert.IsTrue(voided.Success, voided.Message);
        Assert.AreEqual(DocumentStatusEnum.Voided, f.Head!.DocumentStatus);
        Assert.IsTrue((await f.Service.VoidAsync(create.Uid, voidCommand)).Data.AlreadyApplied);
    }

    /// <summary>资料路径可确认后补充，限制长度、保留审计，且不传递或重发来源需求。</summary>
    [TestMethod]
    public async Task LocalFolderIsSupplementOnlyAndLimitedTo1000()
    {
        using var f=new Fixture();var c=f.Command();
        c.Header.LocalFileFolder=@"  D:\投诉\客户甲  ";
        var saved=await f.Service.SaveAsync(c.Uid,c,true);
        Assert.IsTrue(saved.Success,saved.Message);
        Assert.AreEqual(@"D:\投诉\客户甲",(await f.Service.GetAsync(c.Uid)).Data.Header.LocalFileFolder);
        Assert.IsTrue((await f.Confirm.ConfirmDemandAsync(c.Uid,new(){CommandId=Guid.NewGuid(),Version=saved.Data.Version})).Success);
        Assert.IsFalse(JsonSerializer.Serialize(f.Received).Contains("LocalFileFolder"));
        var edit=f.Edit();edit.Header.LocalFileFolder=new string('中',1000);
        Assert.IsTrue((await f.Service.SaveAsync(c.Uid,edit,false)).Success);
        Assert.AreEqual(1,f.ReceiveCalls);
        using(var audit=JsonDocument.Parse(f.Logs.Last().ChangedFieldsJson!))
        {
            Assert.AreEqual(@"D:\投诉\客户甲",audit.RootElement.GetProperty("Before").GetProperty("Header").GetProperty("LocalFileFolder").GetString());
            Assert.AreEqual(new string('中',1000),audit.RootElement.GetProperty("After").GetProperty("Header").GetProperty("LocalFileFolder").GetString());
        }
        edit=f.Edit();edit.Header.LocalFileFolder=new string('中',1001);
        var rejected=await f.Service.SaveAsync(c.Uid,edit,false);
        Assert.AreEqual("VALIDATION",rejected.Error.Code);Assert.AreEqual("Header.LocalFileFolder",rejected.Error.Field);
        Assert.AreEqual(1000,f.Head!.LocalFileFolder!.Length);
        edit=f.Edit();edit.Header.LocalFileFolder="   ";
        Assert.IsTrue((await f.Service.SaveAsync(c.Uid,edit,false)).Success);
        Assert.IsNull((await f.Service.GetAsync(c.Uid)).Data.Header.LocalFileFolder);
        Assert.AreEqual(1,f.ReceiveCalls);
        foreach(var status in new[]{DocumentStatusEnum.Closed,DocumentStatusEnum.Voided})
        {
            f.Head!.DocumentStatus=status;edit=f.Edit();edit.Header.LocalFileFolder="不能修改";
            Assert.AreEqual("BUSINESS_BLOCKED",(await f.Service.SaveAsync(c.Uid,edit,false)).Error.Code);
            Assert.IsNull(f.Head!.LocalFileFolder);
        }
    }

    /// <summary>一个投诉的多种补件共享交期，确认后投诉事实和行结构锁定，处理信息可补充。</summary>
    [TestMethod]
    public async Task MultiplePartsShareHeaderAndConfirmedFactsAreLocked()
    {
        using var f=new Fixture();var c=f.Command();c.AddedLines.Add(f.Line(3));
        var saved=await f.Service.SaveAsync(c.Uid,c,true);
        Assert.IsTrue(saved.Success,saved.Message);
        var confirmed=await f.Confirm.ConfirmDemandAsync(c.Uid,new(){CommandId=Guid.NewGuid(),Version=saved.Data.Version});
        Assert.IsTrue(confirmed.Success,confirmed.Message);
        Assert.AreEqual(2,f.Received!.Items.Count);
        Assert.IsTrue(f.Received.Items.All(x=>x.RequiredDate==c.Header.RequiredDate));
        foreach(var field in new[]{"CustomerId","ResponsibleSupplierId","IssueProductId","IssueSkuSnapshot",
            "IssueCustomerSkuSnapshot","IssueLinkSkuSnapshot","OriginalSalesOrderLineId","RequiredDate","QualityCategoryId"})
        {
            var edit=f.Edit();var property=typeof(AfterSalesEditHeaderDto).GetProperty(field)!;
            object value=property.PropertyType==typeof(string)?"修改型号":
                field=="RequiredDate"?new DateTime(2027,1,1):Guid.NewGuid();
            property.SetValue(edit.Header,value);
            Assert.AreEqual("BUSINESS_BLOCKED",(await f.Service.SaveAsync(c.Uid,edit,false)).Error.Code,field);
        }
        var supplement=f.Edit();supplement.Header.CauseAnalysis="原因";
        supplement.Header.TreatmentDescription="处理";supplement.Header.ReturnedReceivedDate=DateTime.Today;
        supplement.Header.Remark="整单备注";supplement.UpdatedLines[0].Remark="补件备注";
        Assert.IsTrue((await f.Service.SaveAsync(c.Uid,supplement,false)).Success);
        Assert.AreEqual("原因",f.Head!.CauseAnalysis);Assert.AreEqual(1,f.ReceiveCalls);
        var add=f.Edit();add.AddedLines.Add(f.Line(1));
        Assert.AreEqual("BUSINESS_BLOCKED",(await f.Service.SaveAsync(c.Uid,add,false)).Error.Code);
    }

    /// <summary>单头必填、金额和补件负数量分别返回稳定字段错误；分类身份仅作可空存储。</summary>
    [TestMethod]
    public async Task HeaderValidationUsesStableFieldPaths()
    {
        using var f=new Fixture();var c=f.Command();c.Header.IssueDescription=" ";
        Assert.AreEqual("Header.IssueDescription",(await f.Service.SaveAsync(c.Uid,c,true)).Error.Field);
        c.Header.IssueDescription="投诉";c.Header.ServiceAmountCny=-1;
        Assert.AreEqual("Header.ServiceAmountCny",(await f.Service.SaveAsync(c.Uid,c,true)).Error.Field);
        c.Header.ServiceAmountCny=0;c.AddedLines[0].Quantity=-1;
        Assert.AreEqual($"Lines[{c.AddedLines[0].Uid}].Quantity",(await f.Service.SaveAsync(c.Uid,c,true)).Error.Field);
        c.AddedLines[0].Quantity=1;c.Header.ResponsibleSupplierId=null;c.Header.QualityCategoryId=Guid.NewGuid();
        var saved=await f.Service.SaveAsync(c.Uid,c,true);Assert.IsTrue(saved.Success,saved.Message);
        Assert.AreEqual(c.Header.QualityCategoryId,f.Head!.QualityCategoryId);
        var confirmed=await f.Confirm.ConfirmDemandAsync(c.Uid,new(){CommandId=Guid.NewGuid(),Version=saved.Data.Version});
        Assert.AreEqual("Header.ResponsibleSupplierId",confirmed.Error.Field);Assert.AreEqual(0,f.ReceiveCalls);
    }

    /// <summary>只有单头的投诉可保存查询和结案，新占位忽略但显式零数量及已保存空行保留。</summary>
    [TestMethod]
    public async Task HeaderOnlyAndPlaceholderRowsPreserveExplicitFacts()
    {
        using var f=new Fixture();var c=f.Command();
        c.AddedLines=[new(){Uid=Guid.NewGuid()}];
        var saved=await f.Service.SaveAsync(c.Uid,c,true);
        Assert.IsTrue(saved.Success,saved.Message);
        Assert.AreEqual(0,(await f.Service.GetAsync(c.Uid)).Data.Lines.Count);
        var edit=f.Edit();edit.AddedLines=[new(){Uid=Guid.NewGuid(),Quantity=0},new(){Uid=Guid.NewGuid(),Remark="待核实补件"}];
        saved=await f.Service.SaveAsync(c.Uid,edit,false);
        Assert.IsTrue(saved.Success,saved.Message);Assert.AreEqual(2,f.Rows.Count);
        edit=f.Edit();edit.UpdatedLines[1].Remark=null;
        Assert.IsTrue((await f.Service.SaveAsync(c.Uid,edit,false)).Success);
        Assert.AreEqual(2,(await f.Service.GetAsync(c.Uid)).Data.Lines.Count);
        edit=f.Edit();edit.DeletedLines=edit.UpdatedLines.Select(x=>new AfterSalesDeletedLineDto{Uid=x.Uid,Version=x.Version}).ToList();
        edit.UpdatedLines.Clear();
        saved=await f.Service.SaveAsync(c.Uid,edit,false);
        Assert.IsTrue(saved.Success,saved.Message);
        Assert.AreEqual(0,(await f.Service.GetAsync(c.Uid)).Data.Lines.Count);
        var closed=await f.Service.CloseAsync(c.Uid,new(){CommandId=Guid.NewGuid(),Version=saved.Data.Version,Reason="投诉已处理"});
        Assert.IsTrue(closed.Success,closed.Message);Assert.AreEqual(0,f.ReceiveCalls);
    }

    /// <summary>客户投诉单号确认前后均可补充和清空，超长拒绝且不重新发布需求。</summary>
    [TestMethod]
    public async Task CustomerComplaintNumberCanBeSupplementedWithoutPublishingDemand()
    {
        using var f=new Fixture();var c=f.Command();c.Header.CustomerComplaintNo="  客诉-001  ";
        var saved=await f.Service.SaveAsync(c.Uid,c,true);
        Assert.IsTrue(saved.Success,saved.Message);Assert.AreEqual("客诉-001",f.Head!.CustomerComplaintNo);
        var confirmed=await f.Confirm.ConfirmDemandAsync(c.Uid,new(){CommandId=Guid.NewGuid(),Version=saved.Data.Version});
        Assert.IsTrue(confirmed.Success,confirmed.Message);
        var edit=f.Edit();edit.Header.CustomerComplaintNo=" 客诉-002 ";
        var changed=await f.Service.SaveAsync(c.Uid,edit,false);
        Assert.IsTrue(changed.Success,changed.Message);Assert.AreEqual(1,f.ReceiveCalls);
        Assert.AreEqual("客诉-002",f.Head!.CustomerComplaintNo);
        using(var audit=JsonDocument.Parse(f.Logs.Last().ChangedFieldsJson!))
        {
            Assert.AreEqual("客诉-001",audit.RootElement.GetProperty("Before").GetProperty("Header").GetProperty("CustomerComplaintNo").GetString());
            Assert.AreEqual("客诉-002",audit.RootElement.GetProperty("After").GetProperty("Header").GetProperty("CustomerComplaintNo").GetString());
        }
        edit=f.Edit();edit.Header.CustomerComplaintNo=new string('中',101);
        var rejected=await f.Service.SaveAsync(c.Uid,edit,false);
        Assert.AreEqual("VALIDATION",rejected.Error.Code);StringAssert.Contains(rejected.Error.Field,"CustomerComplaintNo");
        Assert.AreEqual("客诉-002",f.Head!.CustomerComplaintNo);
        edit=f.Edit();edit.Header.CustomerComplaintNo="   ";
        Assert.IsTrue((await f.Service.SaveAsync(c.Uid,edit,false)).Success);
        Assert.IsNull((await f.Service.GetAsync(c.Uid)).Data.Header.CustomerComplaintNo);
        Assert.AreEqual(1,f.ReceiveCalls);
        foreach(var status in new[]{DocumentStatusEnum.Closed,DocumentStatusEnum.Voided})
        {
            f.Head!.DocumentStatus=status;edit=f.Edit();edit.Header.CustomerComplaintNo="不允许修改";
            Assert.AreEqual("BUSINESS_BLOCKED",(await f.Service.SaveAsync(c.Uid,edit,false)).Error.Code);
            Assert.IsNull(f.Head!.CustomerComplaintNo);
        }
    }

    /// <summary>混合行只把配件正数量交给来源，空和零只保留投诉。</summary>
    [TestMethod]
    public async Task MixedRowsAndStableReplay()
    {
        using var f=new Fixture();var c=f.Command();
        c.AddedLines.Add(f.Line(0));c.AddedLines.Add(f.Line(null));
        var saved=await f.Service.SaveAsync(c.Uid,c,true);
        Assert.IsTrue(saved.Success,saved.Message);
        var replay=await f.Service.SaveAsync(c.Uid,c,true);
        Assert.IsTrue(replay.Success,replay.Message);Assert.IsTrue(replay.Data.AlreadyApplied);Assert.AreEqual(1,f.NumberCalls);
        var confirmed=await f.Confirm.ConfirmDemandAsync(c.Uid,new(){CommandId=Guid.NewGuid(),Version=saved.Data.Version});
        Assert.IsTrue(confirmed.Success,confirmed.Message);
        Assert.AreEqual(1,f.Received!.Items.Count);Assert.AreEqual(c.AddedLines[0].Uid,f.Received.Items[0].SourceLineId);
        Assert.AreEqual("CR",f.Received.SourceType);Assert.AreEqual(2m,f.Received.Items[0].DemandQty);
        Assert.AreEqual(c.Header.RequiredDate,f.Received.Items[0].RequiredDate);
        Assert.AreEqual("Pcs",f.Received.Items[0].UnitSnapshot);Assert.AreEqual(confirmed.Data.Version,f.Received.SourceVersion);
        Assert.IsTrue(f.Received.Items[0].SkuSnapshot.StartsWith("非标件-"));
        c.AddedLines[0].Quantity=3;
        Assert.AreEqual("COMMAND_CONFLICT",(await f.Service.SaveAsync(c.Uid,c,true)).Error.Code);
    }
    /// <summary>零数量无需确认，可独立结案。</summary>
    [TestMethod]
    public async Task ZeroComplaintCanClose()
    {
        using var f=new Fixture();var c=f.Command();c.AddedLines[0].Quantity=0;c.AddedLines.Add(f.Line(null));
        var saved=await f.Service.SaveAsync(c.Uid,c,true);
        var confirmed=await f.Confirm.ConfirmDemandAsync(c.Uid,new(){CommandId=Guid.NewGuid(),Version=saved.Data.Version});
        Assert.IsTrue(confirmed.Success,confirmed.Message);Assert.IsNull(f.Received);
        Assert.AreEqual(ConfirmationStatusEnum.待确认,confirmed.Data.ConfirmationStatus);
        var closed=await f.Service.CloseAsync(c.Uid,new(){CommandId=Guid.NewGuid(),Version=confirmed.Data.Version,Reason="已说明"});
        Assert.IsTrue(closed.Success,closed.Message);Assert.AreEqual(DocumentStatusEnum.Closed,closed.Data.DocumentStatus);
    }
    /// <summary>未定金额阻断确认，来源失败回滚确认及日志。</summary>
    [TestMethod]
    public async Task MissingAmountAndFailedReceptionRollback()
    {
        using var f=new Fixture();var c=f.Command();c.Header.ServiceAmountCny=null;
        var saved=await f.Service.SaveAsync(c.Uid,c,true);
        var blocked=await f.Confirm.ConfirmDemandAsync(c.Uid,new(){CommandId=Guid.NewGuid(),Version=saved.Data.Version});
        Assert.AreEqual("VALIDATION",blocked.Error.Code);Assert.IsNull(f.Received);
        f.Head!.ServiceAmountCny=0;f.FailReception=true;
        var failed=await f.Confirm.ConfirmDemandAsync(c.Uid,new(){CommandId=Guid.NewGuid(),Version=saved.Data.Version});
        Assert.AreEqual("SOURCE_EXIT_BLOCKED",failed.Error.Code);
        Assert.AreEqual(saved.Data.Version,f.Head!.Version);Assert.AreEqual(ConfirmationStatusEnum.待确认,f.Head.ConfirmationStatus);
        Assert.AreEqual(1,f.Logs.Count);
    }
    /// <summary>确认后锁定金额，说明可补充；撤回失败保持锁定，成功后可重确认。</summary>
    [TestMethod]
    public async Task LockSupplementCancelAndReconfirm()
    {
        using var f=new Fixture();var c=f.Command();
        var saved=await f.Service.SaveAsync(c.Uid,c,true);
        var confirmed=await f.Confirm.ConfirmDemandAsync(c.Uid,new(){CommandId=Guid.NewGuid(),Version=saved.Data.Version});
        var edit=f.Edit();edit.Header.ServiceAmountCny=10;
        Assert.AreEqual("BUSINESS_BLOCKED",(await f.Service.SaveAsync(c.Uid,edit,false)).Error.Code);
        edit=f.Edit();edit.Header.IssueDescription="补充描述";
        var updated=await f.Service.SaveAsync(c.Uid,edit,false);
        Assert.IsTrue(updated.Success,updated.Message);Assert.AreEqual(1,f.ReceiveCalls);
        f.FailCancellation=true;
        var failed=await f.Service.CancelDemandConfirmationAsync(c.Uid,new(){CommandId=Guid.NewGuid(),Version=updated.Data.Version,Reason="修正"});
        Assert.IsFalse(failed.Success);Assert.AreEqual(ConfirmationStatusEnum.已确认,f.Head!.ConfirmationStatus);
        f.FailCancellation=false;
        var cancelled=await f.Service.CancelDemandConfirmationAsync(c.Uid,new(){CommandId=Guid.NewGuid(),Version=updated.Data.Version,Reason="修正"});
        Assert.IsTrue(cancelled.Success,cancelled.Message);Assert.IsNull(f.Head.DemandConfirmedAt);
        edit=f.Edit();edit.UpdatedLines[0].Quantity=4;
        updated=await f.Service.SaveAsync(c.Uid,edit,false);
        var again=await f.Confirm.ConfirmDemandAsync(c.Uid,new(){CommandId=Guid.NewGuid(),Version=updated.Data.Version});
        Assert.IsTrue(again.Success,again.Message);Assert.AreEqual(c.AddedLines[0].Uid,f.Received!.Items[0].SourceLineId);
        Assert.AreEqual(4m,f.Received.Items[0].DemandQty);
    }
    /// <summary>版本冲突不残留部分修改；交付查询未接入时正数量补货明确阻断结案。</summary>
    [TestMethod]
    public async Task ConflictAndClosureBlockerPreserveFacts()
    {
        using var f=new Fixture();var c=f.Command();c.AddedLines.Add(f.Line(0));
        var saved=await f.Service.SaveAsync(c.Uid,c,true);
        var edit=f.Edit();edit.Header.IssueDescription="不能残留";edit.UpdatedLines[1].Version=999;
        Assert.AreEqual("VERSION_CONFLICT",(await f.Service.SaveAsync(c.Uid,edit,false)).Error.Code);
        Assert.AreEqual("成品故障",f.Head!.IssueDescription);
        var confirmed=await f.Confirm.ConfirmDemandAsync(c.Uid,new(){CommandId=Guid.NewGuid(),Version=saved.Data.Version});
        var closed=await f.Service.CloseAsync(c.Uid,new(){CommandId=Guid.NewGuid(),Version=confirmed.Data.Version,Reason="结案"});
        Assert.AreEqual("AFTERSALES_DELIVERY_QUERY_UNAVAILABLE",closed.Error.Code);
        StringAssert.Contains(closed.Message,"PackingList");
        StringAssert.Contains(closed.Message,"尚未接入");
        Assert.AreEqual(DocumentStatusEnum.Open,f.Head!.DocumentStatus);
    }
    /// <summary>无权限不能读取、写入或借幂等重放越权。</summary>
    [TestMethod]
    public async Task PermissionDeniesAllAccess()
    {
        using var f=new Fixture();f.Allowed=false;var c=f.Command();
        Assert.AreEqual("FORBIDDEN",(await f.Service.SaveAsync(c.Uid,c,true)).Error.Code);
        Assert.AreEqual("FORBIDDEN",(await f.Service.GetAsync(c.Uid)).Error.Code);Assert.AreEqual(0,f.NumberCalls);
    }

    /// <summary>确认回执在版本校验前重放，异内容、其他操作占用以及无权重放均明确拒绝。</summary>
    [TestMethod]
    public async Task ConfirmationReceiptAndPermissionBoundary()
    {
        using var f=new Fixture();var save=f.Command();
        var saved=await f.Service.SaveAsync(save.Uid,save,true);
        Assert.IsTrue(saved.Success,saved.Message);
        var command=new ConfirmAfterSalesDemandCommand {CommandId=Guid.NewGuid(),Version=saved.Data.Version};
        var first=await f.Confirm.ConfirmDemandAsync(save.Uid,command);
        Assert.IsTrue(first.Success,first.Message);
        f.Allowed=false;
        Assert.AreEqual("FORBIDDEN",(await f.Confirm.ConfirmDemandAsync(save.Uid,command)).Error.Code);
        f.Allowed=true;
        var replay=await f.Confirm.ConfirmDemandAsync(save.Uid,command);
        Assert.IsTrue(replay.Success,replay.Message);
        Assert.IsTrue(replay.Data.AlreadyApplied);
        Assert.AreEqual(first.Data.Version,replay.Data.Version);
        Assert.AreEqual(1,f.ReceiveCalls);
        Assert.AreEqual(2,f.Logs.Count);
        Assert.AreEqual("COMMAND_CONFLICT",(await f.Confirm.ConfirmDemandAsync(save.Uid,
            new(){CommandId=command.CommandId,Version=command.Version+1})).Error.Code);
        Assert.AreEqual("COMMAND_CONFLICT",(await f.Confirm.ConfirmDemandAsync(save.Uid,
            new(){CommandId=save.CommandId,Version=saved.Data.Version})).Error.Code);
    }

    /// <summary>来源、审计和回执失败都回滚；提交未知保持稳定错误码。</summary>
    [TestMethod]
    public async Task ConfirmationFailuresRollbackWholeTransaction()
    {
        using var f=new Fixture();var save=f.Command();
        var saved=await f.Service.SaveAsync(save.Uid,save,true);
        f.FailReception=true;
        Assert.AreEqual("SOURCE_EXIT_BLOCKED",(await f.Confirm.ConfirmDemandAsync(save.Uid,
            new(){CommandId=Guid.NewGuid(),Version=saved.Data.Version})).Error.Code);
        Assert.AreEqual(ConfirmationStatusEnum.待确认,f.Head!.ConfirmationStatus);
        f.FailReception=false;f.FailAudit=true;
        Assert.IsFalse((await f.Confirm.ConfirmDemandAsync(save.Uid,
            new(){CommandId=Guid.NewGuid(),Version=saved.Data.Version})).Success);
        Assert.AreEqual(ConfirmationStatusEnum.待确认,f.Head!.ConfirmationStatus);
        f.FailAudit=false;f.FailReceipt=true;
        Assert.AreEqual("UNEXPECTED",(await f.Confirm.ConfirmDemandAsync(save.Uid,
            new(){CommandId=Guid.NewGuid(),Version=saved.Data.Version})).Error.Code);
        Assert.AreEqual(ConfirmationStatusEnum.待确认,f.Head!.ConfirmationStatus);
        f.FailReceipt=false;f.FailCommit=true;
        Assert.AreEqual("OUTCOME_UNKNOWN",(await f.Confirm.ConfirmDemandAsync(save.Uid,
            new(){CommandId=Guid.NewGuid(),Version=saved.Data.Version})).Error.Code);
    }

    // 快照回滚模拟事务，不执行真实数据库写入。
    private sealed class Fixture:IDisposable
    {
        public AfterSalesHeader? Head;
        public List<AfterSalesLine> Rows=new();
        public List<BusinessOperationLog> Logs=new();
        public bool Allowed=true,FailReception,FailCancellation,FailAudit,FailReceipt,FailCommit;
        public int NumberCalls,ReceiveCalls;
        public SourceDemandReceptionCommand? Received;
        public TestAfterSalesOperations Service {get;}
        public AfterSalesConfirmationService Confirm { get; }
        private Dictionary<Guid, CommandReceipt> receipts = [];
        private readonly DbSession session;
        private readonly Guid customer=Guid.NewGuid(),actor=Guid.NewGuid();
        public Fixture()
        {
            var connection=new MemoryConnection { ShouldFailCommit=()=>FailCommit };
            connection.Begin=()=>{var h=Clone(Head);var r=Clone(Rows)!;var l=Clone(Logs)!;
                var savedReceipts=new Dictionary<Guid,CommandReceipt>(receipts);
                return ()=>{Head=h;Rows=r;Logs=l;receipts=savedReceipts;};};
            session=(DbSession)RuntimeHelpers.GetUninitializedObject(typeof(DbSession));
            typeof(DbSession).GetField("<Connection>k__BackingField",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(session,connection);
            var repo=Port<IAfterSalesDocumentRepository>((n,a)=>n switch {
                "GetHeaderAsync"=>Task.FromResult(Clone(Head)),
                "GetLinesAsync"=>Task.FromResult<IReadOnlyList<AfterSalesLine>>(Clone(Rows)!),
                "WriteHeaderAsync"=>WriteHead((AfterSalesHeader)a[0]!, (bool)a[1]!),
                "WriteLineAsync"=>WriteLine((AfterSalesLine)a[0]!, (bool)a[1]!),
                _=>throw new NotSupportedException(n)});
            var lookups=Port<IAfterSalesLookupRepository>((n,a)=>n switch {
                "ReferenceAsync"=>Task.FromResult<AfterSalesLookupDto?>(new(){Uid=(Guid)a[1]!,Code="CODE",Name="名称"}),
                _=>throw new NotSupportedException(n)});
            var queries=Port<IAfterSalesQueryRepository>((n,a)=>throw new NotSupportedException(n));
            var reception=Port<ISourceDemandReceptionService>((n,a)=>{
                if(n=="ReceiveAsync"){if(FailReception)throw new SourceDemandReceptionException("SOURCE_EXIT_BLOCKED","模拟来源阻断");ReceiveCalls++;Received=(SourceDemandReceptionCommand)a[0]!;return Task.FromResult(new SourceDemandReceptionResult());}
                if(n=="CancelConfirmationAsync"){if(FailCancellation)throw new SourceDemandReceptionException("SOURCE_EXIT_BLOCKED","模拟已有执行");return Task.FromResult(new SourceDemandExitResult());}
                if(n=="HasActiveAsync")return Task.FromResult(false);throw new NotSupportedException(n);});
            var logs=Port<IBusinessOperationLogRepository>((n,a)=>n switch {
                "GetByCommandAsync"=>Task.FromResult(Logs.SingleOrDefault(x=>x.CommandId==(Guid)a[2]!)),
                "GetByModuleCommandAsync"=>Task.FromResult(Logs.SingleOrDefault(x=>x.Module==(string)a[0]!&&x.CommandId==(Guid)a[1]!)),
                "InsertAsync"=>AppendLog((BusinessOperationLog)a[0]!),_=>throw new NotSupportedException(n)});
            var numbering=Port<IDocumentNoService>((_,_)=>{NumberCalls++;return Task.FromResult(ServiceResult<DocumentNoResponse>.Ok(new(){DocumentNo="CR2026090001"}));});
            var permission=Port<IUserPermissionService>((_,_)=>Task.FromResult(Allowed));
            var user=Port<ICurrentUser>((n,_)=>n switch {"get_UserId"=>(Guid?)actor,"get_UserName"=>"tester","get_IsAuthenticated"=>true,_=>throw new NotSupportedException(n)});
            var receiptPort=Port<ICommandReceiptRepository>((n,a)=>n switch {
                "LockAsync"=>Task.FromResult(0),
                "FindAsync"=>Task.FromResult(receipts.GetValueOrDefault((Guid)a[0]!)),
                "InsertAsync"=>AppendReceipt((CommandReceipt)a[0]!),
                _=>throw new NotSupportedException(n)});
            var correlation=Port<ICorrelationContext>((n,_)=>n=="get_CorrelationId"?"after-sales-test":throw new NotSupportedException(n));
            var validators=new ServiceCollection()
                .AddValidatorsFromAssemblyContaining<ConfirmAfterSalesDemandCommandValidator>()
                .BuildServiceProvider();
            var context=new ServiceBaseContext(session,NullLoggerFactory.Instance);
            var pipeline = new CommandPipeline(new CommandRequestValidator(validators),correlation,user,
                NullLogger<CommandPipeline>.Instance);
            var executor = new BusinessCommandExecutor(context,receiptPort,user);
            Service=new TestAfterSalesOperations(
                new AfterSalesQueryService(context,queries,lookups,repo,permission,user),
                new AfterSalesMaintenanceService(context,repo,lookups,numbering,logs,permission,user,pipeline,executor),
                new AfterSalesCancellationService(context,repo,logs,permission,user,reception,pipeline,executor),
                new AfterSalesLifecycleService(context,repo,logs,permission,user,reception,pipeline,executor));
            Confirm=new(context,repo,lookups,reception,logs,permission,user,
                pipeline, executor);
        }
        public SaveAfterSalesCommand Command()=>new(){CommandId=Guid.NewGuid(),Uid=Guid.NewGuid(),Header=new(){CustomerId=customer,AcceptedDate=new(2026,9,8),IssueDescription="成品故障",IssueSkuSnapshot="成品001",ResponsibleSupplierId=Guid.NewGuid(),ServiceAmountCny=0,RequiredDate=new(2026,10,1)},AddedLines=[Line(2)]};
        public AfterSalesEditLineDto Line(decimal? q)=>new(){Uid=Guid.NewGuid(),Quantity=q,IsNonStandard=true,DescriptionSnapshot="补发螺丝",UnitSnapshot=""};
        public SaveAfterSalesCommand Edit()=>new(){CommandId=Guid.NewGuid(),Uid=Head!.Uid,Version=Head.Version,Header=Head.Adapt<AfterSalesEditHeaderDto>(),UpdatedLines=Rows.Where(x=>!x.IsDeleted).Adapt<List<AfterSalesEditLineDto>>()};
        private Task<bool> WriteHead(AfterSalesHeader h,bool insert){if(!insert)h.Version++;Head=Clone(h);return Task.FromResult(true);}
        private Task<bool> WriteLine(AfterSalesLine l,bool insert){if(!insert)l.Version++;Rows.RemoveAll(x=>x.Uid==l.Uid);Rows.Add(Clone(l)!);return Task.FromResult(true);}
        private Task<int> AppendLog(BusinessOperationLog l)
        {if(FailAudit)return Task.FromResult(0);Logs.Add(Clone(l)!);return Task.FromResult(1);}
        private Task AppendReceipt(CommandReceipt receipt)
        {if(FailReceipt)throw new InvalidOperationException("模拟回执失败");receipts.Add(receipt.CommandId,receipt);return Task.CompletedTask;}
        public void Dispose()=>session.Dispose();
    }
    /// <summary>仅在隔离测试中组合窄服务，保持既有行为断言逐项调用真实切片。</summary>
    private sealed class TestAfterSalesOperations(AfterSalesQueryService query, AfterSalesMaintenanceService maintenance,
        AfterSalesCancellationService cancellation, AfterSalesLifecycleService lifecycle)
    {
        public Task<ServiceResult<AfterSalesDetailDto>> GetAsync(Guid uid) => query.GetAsync(uid);
        public Task<ServiceResult<AfterSalesCommandResult>> SaveAsync(Guid uid, SaveAfterSalesCommand command, bool create) => maintenance.SaveAsync(uid,command,create);
        public Task<ServiceResult<AfterSalesCommandResult>> CancelDemandConfirmationAsync(Guid uid, CancelAfterSalesDemandConfirmationCommand command) => cancellation.CancelDemandConfirmationAsync(uid,command);
        public Task<ServiceResult<AfterSalesCommandResult>> CloseAsync(Guid uid, CloseAfterSalesCommand command) => lifecycle.CloseAsync(uid,command);
        public Task<ServiceResult<AfterSalesCommandResult>> ReopenAsync(Guid uid, ReopenAfterSalesCommand command) => lifecycle.ReopenAsync(uid,command);
        public Task<ServiceResult<AfterSalesCommandResult>> VoidAsync(Guid uid, VoidAfterSalesCommand command) => lifecycle.VoidAsync(uid,command);
    }
    private static T? Clone<T>(T value)=>value is null?default:JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value));
    private static T Port<T>(Func<string,object?[],object?> invoke) where T:class {var p=DispatchProxy.Create<T,TestPort>();((TestPort)(object)p).Handler=invoke;return p;}
    /// <summary>只模拟当前测试实际访问的依赖端口。</summary>
    public class TestPort:DispatchProxy
    {
        public Func<string,object?[],object?> Handler {get;set;}=null!;
        protected override object? Invoke(MethodInfo? method,object?[]? args)=>Handler(method!.Name,args??[]);
    }
    private sealed class MemoryConnection:IDbConnection
    {
        public Func<Action> Begin {get;set;}=null!;
        public Func<bool> ShouldFailCommit {get;set;}=()=>false;
        [System.Diagnostics.CodeAnalysis.AllowNull] public string ConnectionString {get;set;}="";
        public int ConnectionTimeout=>0;public string Database=>"memory";public ConnectionState State=>ConnectionState.Open;
        public IDbTransaction BeginTransaction()=>new MemoryTransaction(this,Begin(),ShouldFailCommit);
        public IDbTransaction BeginTransaction(IsolationLevel level)=>BeginTransaction();
        public void ChangeDatabase(string name)=>throw new NotSupportedException();
        public void Close(){} public void Open(){} public void Dispose(){}
        public IDbCommand CreateCommand()=>throw new NotSupportedException();
    }
    private sealed class MemoryTransaction(IDbConnection connection,Action undo,Func<bool> failCommit):IDbTransaction
    {
        private bool ended;
        public IDbConnection Connection=>connection;public IsolationLevel IsolationLevel=>IsolationLevel.Serializable;
        public void Commit(){if(failCommit())throw new IOException("模拟提交未知");ended=true;}
        public void Rollback(){if(!ended)undo();ended=true;}
        public void Dispose(){if(!ended)Rollback();}
    }
}
