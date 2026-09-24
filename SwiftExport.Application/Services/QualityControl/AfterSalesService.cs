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

namespace SwiftExport.Application.Services.QualityControl;

/// <summary>编排售后事实、稳定身份、权限和来源交接；所有写命令共享一个业务事务。</summary>
public sealed class AfterSalesService(
    ServiceBaseContext context,
    IAfterSalesRepository repository,
    ISourceDemandReceptionService reception,
    IDocumentNoService numbering,
    IBusinessOperationLogRepository logs,
    IUserPermissionService permissions,
    ICurrentUser user) : IAfterSalesService
{
    private const string Module = "AfterSales";

    /// <inheritdoc />
    public Task<ServiceResult<PagedResponseBase<AfterSalesListItemDto>>> SearchAsync(AfterSalesSearchRequest request, CancellationToken cancellationToken = default)
        => ReadAsync(() => repository.SearchAsync(request, cancellationToken), cancellationToken);

    /// <inheritdoc />
    public Task<ServiceResult<PagedResponseBase<AfterSalesLookupDto>>> LookupAsync(string kind, AfterSalesLookupRequest request, CancellationToken cancellationToken = default)
        => ReadAsync(() => repository.LookupAsync(kind, request, cancellationToken), cancellationToken);

    /// <inheritdoc />
    public Task<ServiceResult<AfterSalesDetailDto>> GetAsync(Guid uid, CancellationToken cancellationToken = default)
        => ReadAsync(async () =>
        {
            // 保持头行在同一读取版本，避免并发保存导致编辑页拿到混合快照。
            if(context.Session.Transaction is not null) Fail("BUSINESS_BLOCKED","不能接管外部事务。");
            using var tx=context.Session.Connection.BeginTransaction(IsolationLevel.RepeatableRead);
            context.Session.Transaction=tx;
            try
            {
                var head = await RequireHeaderAsync(uid);
                var lines = (await repository.GetLinesAsync(uid)).Where(x=>!x.IsDeleted).ToList();
                var result = new AfterSalesDetailDto { Header=head.Adapt<AfterSalesHeaderDto>(),
                    Lines=lines.Adapt<List<AfterSalesLineDto>>(),
                    Capabilities=AfterSalesPolicy.Capabilities(head,lines.Any(x=>x.Quantity>0)) };
                tx.Commit();
                return result;
            }
            finally {context.Session.Transaction=null;}
        }, cancellationToken);

    /// <inheritdoc />
    public Task<ServiceResult<AfterSalesCommandResult>> SaveAsync(Guid uid, SaveAfterSalesCommand command, bool create, CancellationToken cancellationToken = default)
        => WriteAsync(uid,command.CommandId,create?"Create":"Save",command,async () =>
        {
            if (uid==Guid.Empty || uid!=command.Uid) Fail("VALIDATION","单据身份不能为空且必须与路由一致。","Uid");
            if (command.Header is null || command.AddedLines is null || command.UpdatedLines is null || command.DeletedLines is null)
                Fail("VALIDATION","单头与行变更集合不能为空。");
            var added = command.AddedLines.Where(x => !AfterSalesPolicy.IsEmptyPlaceholder(x)).ToList();
            var head = create ? new AfterSalesHeader { Uid=uid } : await RequireHeaderAsync(uid,true);
            if (create && await repository.GetHeaderAsync(uid,true) is not null) Fail("VERSION_CONFLICT","单据身份已存在。");
            if (!create) RequireVersion(head,command.Version);
            RequireOpen(head);
            var all = (await repository.GetLinesAsync(uid,true)).ToList();
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
            if(!await repository.WriteHeaderAsync(head,create)) Fail("VERSION_CONFLICT","售后单保存冲突。");
            foreach(var item in writes)
                if(!await repository.WriteLineAsync(item.Line,item.Insert)) Fail("VERSION_CONFLICT","售后行保存冲突。");
            return Result(head,existing.Values.Any(x=>x.Quantity>0));
        },cancellationToken);

    /// <inheritdoc />
    public Task<ServiceResult<AfterSalesCommandResult>> ConfirmDemandAsync(Guid uid, ConfirmAfterSalesDemandCommand command, CancellationToken cancellationToken = default)
        => ChangeAsync(uid,command.CommandId,command.Version,"Confirm",command,null,async (head,lines) =>
        {
            RequireOpen(head);
            if(head.ConfirmationStatus==ConfirmationStatusEnum.已确认) Fail("BUSINESS_BLOCKED","实物需求已经确认。");
            var positive=lines.Where(x=>x.Quantity>0).ToList();
            if(positive.Count==0) return; // 纯投诉不调用不接受空集合的来源服务。
            AfterSalesPolicy.ValidateHeader(head,true);
            await ValidateHeaderReferencesAsync(head,true);
            await ReferenceAsync("customers",head.CustomerId,head.CustomerId,true,"Header.CustomerId");
            foreach(var line in positive)
            {
                AfterSalesPolicy.ValidateLine(line,true);
                await ValidateReferencesAsync(line,head.CustomerId,true);
            }
            // 先确定本次头版本；来源接收与确认打戳仍处于同一事务，失败整体回滚。
            var sourceVersion=head.Version+1;
            await reception.ReceiveAsync(new SourceDemandReceptionCommand {
                CommandId=command.CommandId,SourceType=DocumentTypeStaticsEnum.ComplaintReplenishment,
                SourceUid=uid,SourceNo=head.AfterSalesNo,SourceVersion=sourceVersion,CustomerId=head.CustomerId,
                RejectUnsubmittedActiveLines=true,
                Items=positive.Select(x=>new SourceDemandReceptionItem {
                    SourceLineId=x.Uid,SourceLineNumber=x.LineNumber,ProductId=x.ProductId,IsNonStandard=x.IsNonStandard,
                    SkuSnapshot=x.SkuSnapshot!,CustomerSkuSnapshot=x.CustomerSkuSnapshot,LinkSkuSnapshot=x.LinkSkuSnapshot,
                    DescriptionSnapshot=x.DescriptionSnapshot,ColorSnapshot=x.ColorSnapshot,SpecificationSnapshot=x.SpecificationSnapshot,
                    UnitSnapshot=x.UnitSnapshot,DemandQty=x.Quantity!.Value,RequiredDate=head.RequiredDate,Remark=x.Remark }).ToList()
            });
            head.ConfirmationStatus=ConfirmationStatusEnum.已确认; head.DemandConfirmedAt=DateTime.Now; head.DemandConfirmedBy=user.UserName;
        },cancellationToken);

    /// <inheritdoc />
    public Task<ServiceResult<AfterSalesCommandResult>> CancelDemandConfirmationAsync(Guid uid, CancelAfterSalesDemandConfirmationCommand command, CancellationToken cancellationToken = default)
        => ChangeAsync(uid,command.CommandId,command.Version,"CancelConfirmation",command,command.Reason,async(head,lines)=>
        {
            RequireOpen(head); RequireReason(command.Reason);
            if(head.ConfirmationStatus!=ConfirmationStatusEnum.已确认) Fail("BUSINESS_BLOCKED","当前未确认实物需求。");
            await reception.CancelConfirmationAsync("CR",uid,lines.Where(x=>x.Quantity>0).Select(x=>x.Uid).ToList(),command.Reason);
            head.ConfirmationStatus=ConfirmationStatusEnum.待确认; head.DemandConfirmedAt=null; head.DemandConfirmedBy=null;
        },cancellationToken);

    /// <inheritdoc />
    public Task<ServiceResult<AfterSalesCommandResult>> CloseAsync(Guid uid, CloseAfterSalesCommand command, CancellationToken cancellationToken = default)
        => ChangeAsync(uid,command.CommandId,command.Version,"Close",command,command.Reason,(head,lines)=>
        {
            RequireOpen(head); RequireReason(command.Reason);
            var positive=lines.Where(x=>x.Quantity>0).ToList();
            if(positive.Count>0 && head.ConfirmationStatus!=ConfirmationStatusEnum.已确认) Fail("BUSINESS_BLOCKED","存在尚未确认的实物要求。");
            if(positive.Count>0)
                Fail(
                    "AFTERSALES_DELIVERY_QUERY_UNAVAILABLE",
                    "售后补货交付查询尚未接入，当前无法核验对应售后来源的 PackingList 是否已生效，不能结案。");
            head.DocumentStatus=DocumentStatusEnum.Closed; head.Conclusion=command.Reason;
            return Task.CompletedTask;
        },cancellationToken);

    /// <inheritdoc />
    public Task<ServiceResult<AfterSalesCommandResult>> ReopenAsync(Guid uid, ReopenAfterSalesCommand command, CancellationToken cancellationToken = default)
        => ChangeAsync(uid,command.CommandId,command.Version,"Reopen",command,command.Reason,(head,lines)=>
        {
            RequireReason(command.Reason);
            if(head.DocumentStatus!=DocumentStatusEnum.Closed) Fail("BUSINESS_BLOCKED","只有已结案售后单可以重开。");
            head.DocumentStatus=DocumentStatusEnum.Open;
            return Task.CompletedTask;
        },cancellationToken);

    /// <inheritdoc />
    public Task<ServiceResult<AfterSalesCommandResult>> VoidAsync(Guid uid, VoidAfterSalesCommand command, CancellationToken cancellationToken = default)
        => ChangeAsync(uid,command.CommandId,command.Version,"Void",command,command.Reason,async(head,lines)=>
        {
            RequireOpen(head); RequireReason(command.Reason);
            if(head.ConfirmationStatus==ConfirmationStatusEnum.已确认 || await reception.HasActiveAsync("CR",uid))
                Fail("BUSINESS_BLOCKED","必须先成功取消确认并处置来源要求，才能作废。");
            head.DocumentStatus=DocumentStatusEnum.Voided;
        },cancellationToken);

    // 所有生命周期命令使用统一事务框架，但动作规则仍由显式入口负责。
    private Task<ServiceResult<AfterSalesCommandResult>> ChangeAsync(Guid uid,Guid commandId,int version,string operation,object payload,string? reason,
        Func<AfterSalesHeader,List<AfterSalesLine>,Task> change,CancellationToken token)
        => WriteAsync(uid,commandId,operation,payload,async()=>
        {
            var head=await RequireHeaderAsync(uid,true); RequireVersion(head,version);
            var lines=(await repository.GetLinesAsync(uid,true)).Where(x=>!x.IsDeleted).ToList();
            await change(head,lines);
            if(!await repository.WriteHeaderAsync(head,false)) Fail("VERSION_CONFLICT","售后单状态已变化。");
            return Result(head,lines.Any(x=>x.Quantity>0));
        },token,reason);

    // 权限在幂等读取前检查；命令锁先于头锁，避免并发重试生成两份事实。
    private async Task<ServiceResult<AfterSalesCommandResult>> WriteAsync(Guid uid,Guid commandId,string operation,object payload,
        Func<Task<AfterSalesCommandResult>> action,CancellationToken token,string? reason=null)
    {
        var authorization=await AuthorizedAsync(token);
        if(!authorization) return ServiceResult<AfterSalesCommandResult>.Fail("FORBIDDEN","当前账号无品质管理权限。");
        if(commandId==Guid.Empty) return ServiceResult<AfterSalesCommandResult>.Fail("VALIDATION","必须提供CommandId。","CommandId");
        if(context.Session.Transaction is not null) return ServiceResult<AfterSalesCommandResult>.Fail("BUSINESS_BLOCKED","不能接管外部事务。");
        var fingerprint=JsonSerializer.Serialize(new { Hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new {uid,operation,payload})))) });
        using var tx=context.Session.Connection.BeginTransaction(IsolationLevel.Serializable);
        context.Session.Transaction=tx;
        try
        {
            await repository.LockCommandAsync(commandId);
            var prior=await logs.GetByCommandAsync(Module,"Command",commandId);
            if(prior is not null)
            {
                if(prior.RelatedObjectsJson!=fingerprint) Fail("COMMAND_CONFLICT","同一命令标识不能提交不同内容。");
                var replay=JsonSerializer.Deserialize<CommandLogPayload>(prior.ChangedFieldsJson!)?.Result ?? throw new InvalidOperationException("幂等结果损坏。");
                replay.AlreadyApplied=true; tx.Commit(); return ServiceResult<AfterSalesCommandResult>.Ok(replay);
            }
            token.ThrowIfCancellationRequested();
            var before=await AuditSnapshotAsync(uid);
            var result=await action();
            var after=await AuditSnapshotAsync(uid);
            var affected=await logs.InsertAsync(new BusinessOperationLog {
                Uid=GuidSequentialGenerator.NewGuid(),CommandId=commandId,Module=Module,AggregateType="AfterSales",
                AggregateUid=uid,AggregateNoSnapshot=result.AfterSalesNo,OperationType="Command",OperationName=operation,
                Reason=reason,Summary="售后"+operation,ChangedFieldsJson=JsonSerializer.Serialize(new CommandLogPayload {Result=result,Before=before,After=after}),
                RelatedObjectsJson=fingerprint,UserUid=user.UserId,UserNameSnapshot=user.UserName,OccurredDate=DateTime.Now });
            if(affected!=1) throw new InvalidOperationException("售后日志写入失败。");
            token.ThrowIfCancellationRequested(); tx.Commit();
            return ServiceResult<AfterSalesCommandResult>.Ok(result);
        }
        catch(AfterSalesException ex) {tx.Rollback();return ServiceResult<AfterSalesCommandResult>.Fail(ex.Code,ex.Message,ex.Field);}
        catch(SourceDemandReceptionException ex) {tx.Rollback();return ServiceResult<AfterSalesCommandResult>.Fail(ex.Code,ex.Message);}
        catch(OperationCanceledException) {tx.Rollback();throw;}
        catch(Exception ex)
        {
            tx.Rollback();
            context.LoggerFactory.CreateLogger<AfterSalesService>().LogError(ex,"售后命令 {Operation} 失败，单据 {Uid}。",operation,uid);
            return ServiceResult<AfterSalesCommandResult>.Fail("UNEXPECTED","售后操作失败，未提交变更。");
        }
        finally {context.Session.Transaction=null;}
    }

    private async Task<ServiceResult<T>> ReadAsync<T>(Func<Task<T>> action,CancellationToken token)
    {
        if(!await AuthorizedAsync(token)) return ServiceResult<T>.Fail("FORBIDDEN","当前账号无品质管理权限。");
        try { token.ThrowIfCancellationRequested(); return ServiceResult<T>.Ok(await action()); }
        catch(AfterSalesException ex) {return ServiceResult<T>.Fail(ex.Code,ex.Message,ex.Field);}
        catch(ArgumentException) {return ServiceResult<T>.Fail("VALIDATION","查询条件或候选类型无效。");}
    }
    // 审计保存前后业务事实，既支持幂等重放，也保留说明和金额修改历史。
    private async Task<AfterSalesDetailDto?> AuditSnapshotAsync(Guid uid)
    {
        var head=await repository.GetHeaderAsync(uid,true);
        if(head is null) return null;
        return new() {Header=head.Adapt<AfterSalesHeaderDto>(),Lines=(await repository.GetLinesAsync(uid,true)).Where(x=>!x.IsDeleted).Adapt<List<AfterSalesLineDto>>()};
    }
    private sealed class CommandLogPayload
    {
        public AfterSalesCommandResult Result {get;set;}=new();
        public AfterSalesDetailDto? Before {get;set;}
        public AfterSalesDetailDto? After {get;set;}
    }
    private Task<bool> AuthorizedAsync(CancellationToken token)
        => user.IsAuthenticated && user.UserId.HasValue
            ? permissions.HasPermissionAsync(user.UserId.Value,PermissionKeys.ModuleQualityControlAccess,token)
            : Task.FromResult(false);
    private async Task<AfterSalesHeader> RequireHeaderAsync(Guid uid,bool locked=false)
        => await repository.GetHeaderAsync(uid,locked) ?? throw new AfterSalesException("NOT_FOUND","售后单不存在。");
    private static void RequireVersion(AfterSalesHeader head,int version) {if(head.Version!=version) Fail("VERSION_CONFLICT","售后单已被其他操作更新，请刷新。");}
    private static void RequireOpen(AfterSalesHeader head) {if(head.DocumentStatus!=DocumentStatusEnum.Open) Fail("BUSINESS_BLOCKED","售后单已关闭或作废。");}
    private static void RequireReason(string? reason) {if(string.IsNullOrWhiteSpace(reason)||reason.Length>2000) Fail("VALIDATION","必须填写不超过2000字的操作原因。","Reason");}
    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private static void Fail(string code,string message,string? field=null)=>throw new AfterSalesException(code,message,field);
    private static AfterSalesCommandResult Result(AfterSalesHeader head,bool positive)
        => new() {Uid=head.Uid,AfterSalesNo=head.AfterSalesNo,Version=head.Version,DocumentStatus=head.DocumentStatus,
            ConfirmationStatus=head.ConfirmationStatus,Capabilities=AfterSalesPolicy.Capabilities(head,positive)};

    // 只在新选择身份时采用当前主数据快照；普通编辑不刷新历史资料。
    private async Task NormalizeLineAsync(AfterSalesLine line,AfterSalesLine? old,Guid customerId,bool customerChanged=false)
    {
        line.UnitSnapshot=string.IsNullOrWhiteSpace(line.UnitSnapshot)?"Pcs":line.UnitSnapshot.Trim();
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
            line.UnitSnapshot=string.IsNullOrWhiteSpace(product.Unit)?"Pcs":product.Unit;
        }
        else if(line.ProductId.HasValue && old is not null)
        {
            // 同一标准身份保留原编号快照，不能通过普通DTO输入伪造物料编号或单位。
            line.SkuSnapshot=old.SkuSnapshot;
            line.LinkSkuSnapshot=old.LinkSkuSnapshot;
            line.CustomerSkuSnapshot=customerChanged
                ? (await ReferenceAsync("products",line.ProductId.Value,customerId,false,$"Lines[{line.Uid}].ProductId")).CustomerSku
                : old.CustomerSkuSnapshot;
            line.UnitSnapshot=string.IsNullOrWhiteSpace(old.UnitSnapshot)?"Pcs":old.UnitSnapshot;
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
        => await repository.ReferenceAsync(kind,uid,customerId,active) ?? throw new AfterSalesException("VALIDATION","引用对象不存在、不适用或已停用。",field);

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
}
