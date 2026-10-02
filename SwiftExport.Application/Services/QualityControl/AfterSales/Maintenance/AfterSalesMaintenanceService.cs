using SwiftExport.Application.Interfaces.Systems.Numbering;
using SwiftExport.Application.Interfaces.DemandPlanning.Sources.Reception;
using SwiftExport.Application.Interfaces.DemandPlanning.Sources.Reception.Models;
using SwiftExport.Application.Interfaces.DemandPlanning.Sources.Reception.Exceptions;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mapster;
using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces;
using SwiftExport.Application.Interfaces.Auth;
using SwiftExport.Application.Interfaces.Auth.Permissions.Evaluation;
using SwiftExport.Application.Interfaces.DemandPlanning;
using SwiftExport.Application.Interfaces.QualityControl;
using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.QualityControl.AfterSales;
using SwiftExport.Contracts.DTOs.SystemDtos.Requests;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Auditing;
using SwiftExport.Core.Entities.QualityControl;
using SwiftExport.Core.Interfaces.Auditing;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.QualityControl;

using SwiftExport.Application.Services.QualityControl.AfterSales.Policies;
using SwiftExport.Application.Services.QualityControl.AfterSales.Exceptions;
using SwiftExport.Application.Services.QualityControl.AfterSales.Internal;
using SwiftExport.Core.Interfaces.QualityControl.AfterSales.Document;
using SwiftExport.Core.Interfaces.QualityControl.AfterSales.Query;
using SwiftExport.Core.Interfaces.QualityControl.AfterSales.Lookup;
using static SwiftExport.Application.Services.QualityControl.AfterSales.Internal.AfterSalesRules;
using SwiftExport.Application.Interfaces.QualityControl.AfterSales.Maintenance;
using SwiftExport.Application.Commands;
using SwiftExport.Application.Commands.Validation;
using SwiftExport.Contracts.DTOs.Errors;
namespace SwiftExport.Application.Services.QualityControl.AfterSales.Maintenance;

/// <summary>创建、保存及合法补充售后事实；已确认单仅允许原规则许可的字段变化。</summary>
public sealed class AfterSalesMaintenanceService(ServiceBaseContext context, IAfterSalesDocumentRepository documents, IAfterSalesLookupRepository lookups, IDocumentNoService numbering, IBusinessOperationLogRepository logs, IPermissionEvaluationService permissions, ICurrentUser user, CommandPipeline pipeline, BusinessCommandExecutor executor) : IAfterSalesMaintenanceService
{
    private const string CreateType = "QualityControl.AfterSales.Create";
    private const string SaveType = "QualityControl.AfterSales.Save";
    private readonly AfterSalesCommandAudit audit = new(documents, logs, user);
    /// <inheritdoc />
    public Task<ServiceResult<AfterSalesCommandResult>> SaveAsync(Guid uid, SaveAfterSalesCommand command, bool create, CancellationToken cancellationToken = default)
        => RunAsync(() => pipeline.ExecuteAsync<SaveAfterSalesCommand, AfterSalesCommandResult>(
            command, command?.CommandId, create ? CreateType : SaveType, AuthorizeAsync,
            token => ExecuteSaveCommandAsync(uid, command!, create, token), cancellationToken));

    /// <summary>区分创建和更新身份，路由目标与完整业务输入参加公共指纹。</summary>
    private async Task<ServiceResult<CommandExecutionResult<AfterSalesCommandResult>>> ExecuteSaveCommandAsync(
        Guid uid, SaveAfterSalesCommand command, bool create, CancellationToken token)
    {
        if (uid == Guid.Empty || uid != command.Uid)
            return ServiceResult<CommandExecutionResult<AfterSalesCommandResult>>.Fail(
                ServiceErrorCodes.Validation, "单据身份不能为空且必须与路由一致。", "Uid");
        var commandType = create ? CreateType : SaveType;
        var result = await executor.ExecuteAsync(command.CommandId, commandType, uid,
            new { command.Uid, command.Version, command.Header, command.AddedLines,
                command.UpdatedLines, command.DeletedLines },
            ct => SaveInTransactionAsync(uid, command, create, ct), token, IsolationLevel.Serializable);
        result.Value.AlreadyApplied = result.IsReplay;
        return ServiceResult<CommandExecutionResult<AfterSalesCommandResult>>.Ok(result);
    }

    /// <summary>首次执行时按原头行和引用顺序写入，前后审计与完整回执同事务。</summary>
    private async Task<AfterSalesCommandResult> SaveInTransactionAsync(
        Guid uid, SaveAfterSalesCommand command, bool create, CancellationToken token)
    {
            token.ThrowIfCancellationRequested();
            var before = await audit.SnapshotAsync(uid);
            if (uid==Guid.Empty || uid!=command.Uid) Fail("VALIDATION","单据身份不能为空且必须与路由一致。","Uid");
            if (command.Header is null || command.AddedLines is null || command.UpdatedLines is null || command.DeletedLines is null)
                Fail("VALIDATION","单头与行变更集合不能为空。");
            var added = command.AddedLines.Where(x => !AfterSalesPolicy.IsEmptyPlaceholder(x)).ToList();
            var head = create ? new AfterSalesHeader { Uid=uid } : await documents.GetHeaderAsync(uid,true)
                ?? throw new AfterSalesException("NOT_FOUND", "售后单不存在。");
            if (create && await documents.GetHeaderAsync(uid,true) is not null) Fail("VERSION_CONFLICT","单据身份已存在。");
            if (!create) RequireVersion(head,command.Version);
            RequireOpen(head);
            var all = (await documents.GetLinesAsync(uid,true)).ToList();
            var existing = all.Where(x=>!x.IsDeleted).ToDictionary(x=>x.Uid);
            var ids = added.Select(x=>x.Uid).Concat(command.UpdatedLines.Select(x=>x.Uid)).Concat(command.DeletedLines.Select(x=>x.Uid)).ToList();
            if (ids.Any(x=>x==Guid.Empty) || ids.Distinct().Count()!=ids.Count) Fail("VALIDATION","行身份为空或在多个变更集合中重复。");
            var confirmed=head.ConfirmationStatus==ConfirmationStatusEnum.已确认;
            if (confirmed && (!AfterSalesPolicy.LockedHeaderFactsEqual(head.Adapt<AfterSalesEditHeaderDto>(),command.Header)
                || added.Count>0 || command.DeletedLines.Count>0))
                Fail("BUSINESS_BLOCKED","确认后的单头事实和行结构已锁定，请先取消确认。");

            var oldHeader=head.Adapt<AfterSalesHeader>();
            var previousCustomer=head.CustomerId;
            if (!confirmed)
            {
                var customer=await ReferenceAsync("customers",command.Header.CustomerId,command.Header.CustomerId,create||head.CustomerId!=command.Header.CustomerId,"Header.CustomerId");
                command.Header.Adapt(head);
                head.AcceptedDate=head.AcceptedDate.Date;
                head.ExpectedCompletionDate=head.ExpectedCompletionDate?.Date;
                if (head.AcceptedDate==default) Fail("VALIDATION","必须填写受理日期。","Header.AcceptedDate");
                if (create || previousCustomer!=head.CustomerId)
                {
                    head.CustomerCodeSnapshot=customer.Code;
                    head.CustomerNameSnapshot=customer.Name;
                }
                if (create && !head.OwnerId.HasValue) head.OwnerId=user.UserId;
                if (head.OwnerId.HasValue) await ReferenceAsync("owners",head.OwnerId.Value,null,true,"Header.OwnerId");
            }
            else command.Header.Adapt(head);
            await NormalizeHeaderAsync(head,create?null:oldHeader,confirmed);

            var writes=new List<(AfterSalesLine Line,bool Insert)>();
            var next=all.Count==0?1:all.Max(x=>x.LineNumber)+1;
            foreach (var input in added)
            {
                if (all.Any(x=>x.Uid==input.Uid)) Fail("VALIDATION","不能复用历史行身份。");
                var line=input.Adapt<AfterSalesLine>();
                line.HeaderId=uid; line.LineNumber=next++; line.Version=1;
                await NormalizeLineAsync(line,null,head.CustomerId);
                writes.Add((line,true)); existing.Add(line.Uid,line);
            }
            foreach (var input in command.UpdatedLines)
            {
                if (!existing.TryGetValue(input.Uid,out var old)) Fail("VALIDATION","更新行不属于当前单据。");
                if (old!.Version!=input.Version) Fail("VERSION_CONFLICT","售后行已变化，请刷新。");
                if (confirmed && !AfterSalesPolicy.LockedFactsEqual(old.Adapt<AfterSalesEditLineDto>(),input))
                    Fail("BUSINESS_BLOCKED","已确认行的事实字段已锁定，请先取消确认。");
                var line=old.Adapt<AfterSalesLine>();
                input.Adapt(line);
                if (!confirmed) await NormalizeLineAsync(line,old,head.CustomerId,previousCustomer!=head.CustomerId);
                else AfterSalesPolicy.ValidateLine(line,false);
                writes.Add((line,false)); existing[line.Uid]=line;
            }
            foreach (var input in command.DeletedLines)
            {
                if (!existing.TryGetValue(input.Uid,out var line)) Fail("VALIDATION","删除行不属于当前单据。");
                if (line!.Version!=input.Version) Fail("VERSION_CONFLICT","删除行已变化。");
                line.IsDeleted=true; writes.Add((line,false)); existing.Remove(line.Uid);
            }
            // 换客户时同步未进入修改集合的补件客户型号，保持同一客户的快照口径。
            if (previousCustomer!=head.CustomerId)
            {
                foreach(var line in existing.Values)
                {
                    if(!writes.Any(x=>x.Line.Uid==line.Uid))
                    {
                        var old=line.Adapt<AfterSalesLine>();
                        await NormalizeLineAsync(line,old,head.CustomerId,true);
                        writes.Add((line,false));
                    }
                }
            }
            ValidateLengths(head);
            foreach(var item in writes) ValidateLengths(item.Line);
            if (create)
            {
                var number=await numbering.GetNextDocumentNoAsync(new DocumentNoRequest {DocType="CR",BusinessDate=head.AcceptedDate,SerialWidth=4,Operator=user.UserName});
                if(!number.Success) Fail("BUSINESS_BLOCKED","售后单号生成失败。");
                head.AfterSalesNo=number.Data.DocumentNo;
            }
            if(!await documents.WriteHeaderAsync(head,create)) Fail("VERSION_CONFLICT","售后单保存冲突。");
            foreach(var item in writes)
                if(!await documents.WriteLineAsync(item.Line,item.Insert)) Fail("VERSION_CONFLICT","售后行保存冲突。");
            var result = Result(head,existing.Values.Any(x=>x.Quantity>0));
            var after = await audit.SnapshotAsync(uid);
            await audit.WriteAsync(uid, command.CommandId, create ? "Create" : "Save", null, result, before, after);
            return result;
    }

    /// <summary>只在新选择身份时采用当前主数据单位；普通编辑保留历史快照，缺失单位不推断。</summary>
    private async Task NormalizeLineAsync(AfterSalesLine line,AfterSalesLine? old,Guid customerId,bool customerChanged=false)
    {
        line.UnitSnapshot=line.UnitSnapshot?.Trim() ?? string.Empty;
        if(line.IsNonStandard)
        {
            if(line.ProductId.HasValue) Fail("VALIDATION","非标不能绑定Product。",$"Lines[{line.Uid}].ProductId");
            line.SkuSnapshot=NonStandardSkuGenerator.Create(line.Uid);
        }
        else if(line.ProductId.HasValue && (old is null||old.ProductId!=line.ProductId||old.IsNonStandard))
        {
            var product=await ReferenceAsync("products",line.ProductId.Value,customerId,true,$"Lines[{line.Uid}].ProductId");
            line.SkuSnapshot=product.Code; line.LinkSkuSnapshot=product.LinkSku; line.CustomerSkuSnapshot=product.CustomerSku;
            line.DescriptionSnapshot=product.Name; line.ColorId=product.ColorId; line.ColorSnapshot=product.ColorName;
            line.SpecificationSnapshot=product.Specification;
            line.UnitSnapshot=product.Unit?.Trim() ?? string.Empty;
        }
        else if(line.ProductId.HasValue && old is not null)
        {
            // 同一标准身份保留原编号快照，不能通过普通DTO输入伪造物料编号或单位。
            line.SkuSnapshot=old.SkuSnapshot;
            line.LinkSkuSnapshot=old.LinkSkuSnapshot;
            line.CustomerSkuSnapshot=customerChanged
                ? (await ReferenceAsync("products",line.ProductId.Value,customerId,false,$"Lines[{line.Uid}].ProductId")).CustomerSku
                : old.CustomerSkuSnapshot;
            line.UnitSnapshot=old.UnitSnapshot;
        }
        else if(!line.IsNonStandard)
        {
            line.SkuSnapshot=null;
            line.LinkSkuSnapshot=null;
            line.CustomerSkuSnapshot=null;
        }
        if(!line.ColorId.HasValue && old?.ColorId.HasValue==true) line.ColorSnapshot=null;
        if(line.ColorId.HasValue && (old is null||old.ColorId!=line.ColorId))
            line.ColorSnapshot=(await ReferenceAsync("colors",line.ColorId.Value,null,true,$"Lines[{line.Uid}].ColorId")).Name;
        AfterSalesPolicy.ValidateLine(line,false);
        await ValidateReferencesAsync(line,customerId,false,old);
    }
    private async Task ValidateReferencesAsync(AfterSalesLine line,Guid customerId,bool confirming,AfterSalesLine? old=null)
    {
        var prefix=$"Lines[{line.Uid}].";
        if(line.ProductId.HasValue) await ReferenceAsync("products",line.ProductId.Value,customerId,confirming||old?.ProductId!=line.ProductId,prefix+"ProductId");
        if(line.ColorId.HasValue) await ReferenceAsync("colors",line.ColorId.Value,null,false,prefix+"ColorId");
    }
    // 同一问题产品保留历史型号，重新选择身份或客户时才重新取快照。
    private async Task NormalizeHeaderAsync(AfterSalesHeader head,AfterSalesHeader? old,bool confirmed)
    {
        head.CustomerComplaintNo=string.IsNullOrWhiteSpace(head.CustomerComplaintNo)?null:head.CustomerComplaintNo.Trim();
        // 资料路径只做文本规范化，不校验目录存在性，也不参与来源需求交接。
        head.LocalFileFolder=string.IsNullOrWhiteSpace(head.LocalFileFolder)?null:head.LocalFileFolder.Trim();
        head.ReturnedReceivedDate=head.ReturnedReceivedDate?.Date;
        if(!confirmed)
        {
            head.RequiredDate=head.RequiredDate?.Date;
            if(head.IssueProductId.HasValue && head.IssueProductId!=old?.IssueProductId)
            {
                var product=await ReferenceAsync("products",head.IssueProductId.Value,head.CustomerId,false,"Header.IssueProductId");
                head.IssueSkuSnapshot=product.Code; head.IssueLinkSkuSnapshot=product.LinkSku;
                head.IssueCustomerSkuSnapshot=product.CustomerSku;
            }
            else if(head.IssueProductId.HasValue && old is not null)
            {
                head.IssueSkuSnapshot=old.IssueSkuSnapshot; head.IssueLinkSkuSnapshot=old.IssueLinkSkuSnapshot;
                head.IssueCustomerSkuSnapshot=head.CustomerId!=old.CustomerId
                    ? (await ReferenceAsync("products",head.IssueProductId.Value,head.CustomerId,false,"Header.IssueProductId")).CustomerSku
                    : old.IssueCustomerSkuSnapshot;
            }
            await ValidateHeaderReferencesAsync(head,false,old);
        }
        AfterSalesPolicy.ValidateHeader(head,false);
    }

    // 分类仅保留可空身份；本次不接入尚未建设的品质分类功能。
    private async Task ValidateHeaderReferencesAsync(AfterSalesHeader head,bool confirming,AfterSalesHeader? old=null)
    {
        if(head.ResponsibleSupplierId.HasValue)
            await ReferenceAsync("suppliers",head.ResponsibleSupplierId.Value,null,confirming||old?.ResponsibleSupplierId!=head.ResponsibleSupplierId,"Header.ResponsibleSupplierId");
        if(head.IssueProductId.HasValue)
            await ReferenceAsync("products",head.IssueProductId.Value,head.CustomerId,false,"Header.IssueProductId");
        if(head.OriginalSalesOrderLineId.HasValue)
            await ReferenceAsync("sales-order-lines",head.OriginalSalesOrderLineId.Value,head.CustomerId,false,"Header.OriginalSalesOrderLineId");
    }

    private async Task<AfterSalesLookupDto> ReferenceAsync(string kind,Guid uid,Guid? customerId,bool active,string field)
        => await lookups.ReferenceAsync(kind,uid,customerId,active) ?? throw new AfterSalesException("VALIDATION","引用对象不存在、不适用或已停用。",field);

    // 在数据库截断或溢出前提供稳定字段错误，不把输入静默裁剪。
    private static void ValidateLengths(object entity)
    {
        foreach(var property in entity.GetType().GetProperties().Where(x=>x.PropertyType==typeof(string)))
        {
            if(property.GetValue(entity) is not string value) continue;
            var max=property.Name is "IssueDescription" or "CauseAnalysis" or "TreatmentDescription" or "Remark" or "Conclusion"?2000:
                property.Name=="LocalFileFolder"?1000:
                property.Name is "DescriptionSnapshot" or "SpecificationSnapshot"?500:
                property.Name=="CustomerNameSnapshot"?200:100;
            if(value.Length>max) Fail("VALIDATION",$"字段长度不能超过{max}。",(entity is AfterSalesHeader ? "Header." : $"Lines[{((AfterSalesLine)entity).Uid}].")+property.Name);
        }
    }

    /// <summary>维护权限先于验证、回执和事务，模块访问本身不授予写入。</summary>
    private async Task<ServiceResult<AfterSalesCommandResult>?> AuthorizeAsync(CancellationToken token)
    {
        if (!user.IsAuthenticated || user.UserId is not { } actor)
            return ServiceResult<AfterSalesCommandResult>.Fail(ServiceErrorCodes.Unauthenticated,
                "登录状态无效，请重新登录。");
        return await permissions.HasPermissionAsync(actor, PermissionKeys.AfterSalesMaintain, token)
            ? null : ServiceResult<AfterSalesCommandResult>.Fail(ServiceErrorCodes.Forbidden,
                "当前账号没有售后维护权限。");
    }

    /// <summary>保留售后专属业务错误与公共执行器的结果不明语义。</summary>
    private async Task<ServiceResult<AfterSalesCommandResult>> RunAsync(
        Func<Task<ServiceResult<AfterSalesCommandResult>>> action)
    {
        try { return await action(); }
        catch (CommandValidationException ex)
        { return ServiceResult<AfterSalesCommandResult>.Fail(ServiceErrorCodes.Validation, ex.Message, ex.Field); }
        catch (BusinessCommandException ex)
        { return ServiceResult<AfterSalesCommandResult>.Fail(ex.Code, ex.Message, ex.Field); }
        catch (AfterSalesException ex)
        { return ServiceResult<AfterSalesCommandResult>.Fail(ex.Code, ex.Message, ex.Field); }
        catch (SourceDemandReceptionException ex)
        { return ServiceResult<AfterSalesCommandResult>.Fail(ex.Code, ex.Message); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            context.LoggerFactory.CreateLogger<AfterSalesMaintenanceService>()
                .LogError(ex, "售后维护结果需按原 CommandId 核实。");
            return ServiceResult<AfterSalesCommandResult>.Fail(ServiceErrorCodes.Unexpected,
                "售后维护失败，请核实命令结果。");
        }
    }
}
