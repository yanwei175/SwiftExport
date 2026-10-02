using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using CommunityToolkit.Mvvm.Input;
using Refit;
using SwiftExport.Client.VSTO.Features.Procurement.PurchaseOrders.Services;
using SwiftExport.Contracts.Apis.ForExcel;
using SwiftExport.Contracts.Apis.Procurement;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
namespace SwiftExport.Client.VSTO.Features.Procurement.PurchaseOrders.ViewModels
{
    public sealed partial class PurchaseOrderImportViewModel
    {
        private readonly IForExcelPurchaseOrderApi checkApi;
        private readonly IProcurementOrderApi orderApi;
        private CancellationTokenSource submissionLifetime=new CancellationTokenSource();
        private bool checking, saving, completed, serverCanSave, unknownSave;
        private long documentRevision, checkedRevision=-1;
        private Guid customerId;
        private CreateIndependentPurchaseOrderCommand pendingSave;
        private string submissionMessage="";
        /// <summary>只读检验并定位客户、供应商和标准产品。</summary>
        public IAsyncRelayCommand CheckImportCommand { get; }
        /// <summary>通过当前版本检验后新建独立草稿。</summary>
        public IAsyncRelayCommand SaveDraftCommand { get; }
        /// <summary>未知结果显式重发同一 CommandId 和输入，不自动重试。</summary>
        public IAsyncRelayCommand RetrySaveCommand { get; }
        /// <summary>成功后清空输入开始另一份导入。</summary>
        public IRelayCommand NewImportCommand { get; }
        /// <summary>保存或检验反馈，不包含完整响应正文。</summary>
        public string SubmissionMessage { get=>submissionMessage; private set=>SetProperty(ref submissionMessage,value); }
        /// <summary>未知保存结果的稳定命令身份。</summary>
        public string PendingCommandId => pendingSave?.CommandId.ToString()??"";
        /// <summary>未知结果存在时禁止编辑和第二次创建。</summary>
        public bool HasPendingSave => pendingSave!=null;
        /// <summary>保存资格要求当前整单版本、逐行状态、权限及输入同时通过。</summary>
        public bool CanSaveDraft => IsEditable && HasCreatePermission && serverCanSave && checkedRevision==documentRevision && customerId!=Guid.Empty
            && SelectedSupplier!=null && Lines.Count>0 && string.IsNullOrEmpty(Header.Error)
            && Lines.All(l=>string.IsNullOrEmpty(l.InputError) && l.MatchStatus==(l.IsNonStandard ? "NonStandard" : "Matched"));
        private bool SubmissionLocked => checking || saving || completed || HasPendingSave;
        // 任何编辑撤销整单检查快照，不把匹配失败自动变为非标。
        private void InvalidateSubmission() { if(resetting || applyingSubmission) return; documentRevision++; checkedRevision=-1; serverCanSave=false; SubmissionMessage="资料已变化，请重新检验。"; }
        /// <summary>检查接纳核对会话、整单版本、稳定行身份及逐行编辑版本。</summary>
        public async Task CheckImportAsync()
        {
            if(!CheckImportCommand.CanExecute(null)) return;
            var input=Header.Error+string.Join("",Lines.Select(l=>l.InputError));
            if(Lines.Count>500 || SelectedSupplier==null || !string.IsNullOrEmpty(input)) { SubmissionMessage=Lines.Count>500 ? "最多支持 500 行。" : SelectedSupplier==null ? "请选择实际供应商。" : input; return; }
            var version=pageVersion; var revision=documentRevision; var rows=Lines.ToArray(); var rowVersions=rows.Select(l=>l.EditRevision).ToArray();
            var request=PurchaseOrderImportRequests.Check(Header,SelectedSupplier.Uid,rows); var token=submissionLifetime.Token;
            checking=true; serverCanSave=false; checkedRevision=-1; SubmissionMessage="正在检验采购资料…"; NotifyState();
            try
            {
                var response=await checkApi.CheckAsync(request,token);
                await DeliverAsync(()=> {
                    if(!Accept(version) || token.IsCancellationRequested || revision!=documentRevision || !rows.SequenceEqual(Lines) || rows.Where((l,i)=>l.EditRevision!=rowVersions[i]).Any()) return;
                    var data=response?.Data;
                    if(response?.Success!=true || data==null) { SubmissionMessage=response?.Error?.Message??response?.Message??"检验没有返回有效结果。"; return; }
                    if(data.Customer==null || !data.Customer.IsActive || data.Customer.Uid==Guid.Empty || !string.Equals(data.Customer.CustomerCode,request.CustomerCode,StringComparison.OrdinalIgnoreCase)
                        || data.Supplier==null || !data.Supplier.IsActive || data.Supplier.Uid!=request.SupplierId
                        || data.Lines==null || data.Lines.Count!=rows.Length || data.Lines.Any(l=>l==null) || data.Lines.Select(l=>l.ClientLineId).Distinct().Count()!=rows.Length
                        || data.Lines.Any(l=>!rows.Any(r=>r.Uid==l.ClientLineId)))
                    { SubmissionMessage=string.Join("；",(data.Issues??new System.Collections.Generic.List<SwiftExport.Contracts.DTOs.ForExcel.PurchaseOrders.PurchaseOrderImportIssueDto>()).Select(i=>i.Message))+" 检验未返回完整且一致的身份，不能保存。"; return; }
                    applyingSubmission=true;
                    try
                    {
                        var valid=rows.Select((l,i)=>l.TryApplyCheck(data.Lines.Single(r=>r.ClientLineId==l.Uid),rowVersions[i])).All(v=>v);
                        customerId=data.Customer.Uid; CustomerName=data.Customer.CustomerName;
                        var issues=data.Issues??new System.Collections.Generic.List<SwiftExport.Contracts.DTOs.ForExcel.PurchaseOrders.PurchaseOrderImportIssueDto>();
                        serverCanSave=valid && data.CanSave && !issues.Any(i=>!i.RequiresConfirmation || !i.Acknowledged);
                        checkedRevision=serverCanSave ? revision : -1;
                        SubmissionMessage=serverCanSave ? "检验通过，可以新建未确认、无来源采购草稿。" : string.Join("；",issues.Select(i=>i.Message))+" 请处理行匹配或输入问题后重新检验。";
                    }
                    finally { applyingSubmission=false; }
                });
            }
            catch(OperationCanceledException) when(token.IsCancellationRequested) { }
            catch(Exception ex) { await DeliverAsync(()=> { if(Accept(version) && revision==documentRevision) SubmissionMessage=errors.Describe(ex); }); }
            finally { await DeliverAsync(()=> { if(Accept(version)) { checking=false; NotifyState(); } }); }
        }
        /// <summary>创建期间冻结输入；不明确回执保留原命令，成功后禁止重复新建。</summary>
        public async Task SaveDraftAsync()
        {
            if(saving || !Accept(pageVersion) || !HasCreatePermission || pendingSave==null && !CanSaveDraft) return;
            var version=pageVersion;
            if(pendingSave==null) pendingSave=PurchaseOrderImportRequests.Create(Header,customerId,SelectedSupplier.Uid,Lines);
            var request=pendingSave; var token=submissionLifetime.Token; saving=true; SubmissionMessage="正在创建采购草稿…"; NotifyState();
            try
            {
                var result=await orderApi.CreateAsync(request,token);
                await DeliverAsync(()=> {
                    if(!Accept(version) || !ReferenceEquals(pendingSave,request)) return;
                    if(result?.Success==true && result.Data!=null && result.Data.PurchaseOrderId!=Guid.Empty && !string.IsNullOrWhiteSpace(result.Data.OrderNo))
                    { OrderNo=result.Data.OrderNo; SubmissionMessage="采购草稿已保存："+OrderNo+"（"+result.Data.PurchaseOrderId+"）。来源关联在系统中另行办理。"; pendingSave=null; unknownSave=false; completed=true; dirty=false; serverCanSave=false; }
                    else SaveFailed(result?.Success==false ? result.Error?.Code : ServiceErrorCodes.OutcomeUnknown,result?.Error?.Message??result?.Message??"没有确定保存回执。");
                });
            }
            catch(Exception ex) { await DeliverAsync(()=> { if(Accept(version) && ReferenceEquals(pendingSave,request)) SaveFailed(SaveErrorCode(ex),errors.Describe(ex)); }); }
            finally { await DeliverAsync(()=> { if(Accept(version)) { saving=false; NotifyState(); } }); }
        }
        // 一旦结果未知，后续权限/限流错误不能证明最初请求未提交，保留同一意图。
        private void SaveFailed(string code,string message)
        {
            unknownSave |= string.IsNullOrEmpty(code) || new[] { ServiceErrorCodes.OutcomeUnknown,ServiceErrorCodes.CommandBusy,ServiceErrorCodes.ServiceUnavailable }.Contains(code);
            var keep=unknownSave && (string.IsNullOrEmpty(code) || new[] { ServiceErrorCodes.OutcomeUnknown,ServiceErrorCodes.CommandBusy,ServiceErrorCodes.ServiceUnavailable,ServiceErrorCodes.RateLimited,ServiceErrorCodes.Unauthenticated,ServiceErrorCodes.Forbidden }.Contains(code));
            if(!keep) { pendingSave=null; unknownSave=false; InvalidateSubmission(); }
            SubmissionMessage=message+(keep ? " 保存结果未知，请重试原请求。CommandId："+PendingCommandId : " 请修正资料后重新检验。");
        }
        // 只解析结构化拒绝代码；网关和网络异常均不能证明事务没有提交。
        private static string SaveErrorCode(Exception exception)
        {
            var error=exception as ApiException; if(string.IsNullOrWhiteSpace(error?.Content)) return null;
            try { using(var json=JsonDocument.Parse(error.Content)) { JsonElement success,detail,code;
                if(json.RootElement.ValueKind==JsonValueKind.Object && json.RootElement.TryGetProperty("success",out success) && success.ValueKind==JsonValueKind.False
                    && json.RootElement.TryGetProperty("error",out detail) && detail.ValueKind==JsonValueKind.Object && detail.TryGetProperty("code",out code) && code.ValueKind==JsonValueKind.String) return code.GetString(); } }
            catch(JsonException) { } return null;
        }
    }
}
