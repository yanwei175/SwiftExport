using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Client.VSTO.Features.Sales.Orders.Models;
using SwiftExport.Client.VSTO.Features.Sales.Orders.Services;
using SwiftExport.Contracts.Apis.ForExcel;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.ForExcel.SalesOrders;
using System.Collections.Generic;
using SwiftExport.Contracts.DTOs.SalesOrders.Detail;
using Refit;

namespace SwiftExport.Client.VSTO.Features.Sales.Orders.ViewModels
{
    public sealed partial class SalesOrderImportViewModel
    {
        private IForExcelSalesOrderApi checkApi;
        private CancellationTokenSource submissionLifetime = new CancellationTokenSource();
        private bool checking, saving, applyingSubmission, serverCanSave, acceptExistingOrders, saveCompleted;
        private long documentRevision, checkedRevision = -1;
        private SaveSalesOrderRequest pendingSave;
        private string submissionMessage = "";
        /// <summary>执行只读匹配，确认提示变化后仍须重新检验。</summary>
        public IAsyncRelayCommand CheckImportCommand { get; private set; }
        /// <summary>仅当本次输入完整通过检查时新建草稿；成功后禁止再次新建同一份输入。</summary>
        public IAsyncRelayCommand SaveDraftCommand { get; private set; }
        /// <summary>结果未知时显式重发原请求和原 CommandId，不读取当前编辑值重新组装。</summary>
        public IAsyncRelayCommand RetrySaveCommand { get; private set; }
        /// <summary>成功后开始一份全新的导入，重新生成明细与命令身份。</summary>
        public IRelayCommand NewImportCommand { get; private set; }
        /// <summary>仅用户明确操作才将当前行转换为非标，反向转换仍需重新检查。</summary>
        public IRelayCommand<SalesOrderImportLine> ToggleNonStandardCommand { get; private set; }
        /// <summary>接纳当前行的明确候选选择，随后需要重新检验。</summary>
        public IRelayCommand<SalesOrderImportLine> ChooseMatchCommand { get; private set; }
        /// <summary>检验、确认及保存回执反馈；不含响应正文或令牌。</summary>
        public string SubmissionMessage { get => submissionMessage; private set => SetProperty(ref submissionMessage, value); }
        /// <summary>整单通过检查的版本必须与当前输入一致。</summary>
        public bool CanSaveDraft => IsEditable && HasMaintainPermission && pendingSave == null && serverCanSave
            && checkedRevision == documentRevision && Lines.Count > 0 && string.IsNullOrEmpty(Header.Error)
            && Lines.All(l => string.IsNullOrEmpty(l.Error) && l.MatchStatus == (l.IsNonStandard ? "NonStandard" : "Matched"));
        /// <summary>未收到确定回执的原始保存意图，页面此时锁定，禁止形成第二份保存请求。</summary>
        public bool HasPendingSave => pendingSave != null;
        /// <summary>用于人工排查未知结果；不是服务器订单号。</summary>
        public string PendingCommandId => pendingSave?.CommandId.ToString() ?? "";
        private bool HasMaintainPermission => session.Capture().User?.Permissions?.Contains(PermissionKeys.SalesOrderMaintain) == true;
        private bool SubmissionLocked => checking || saving || saveCompleted || pendingSave != null;

        // 首期直接复用窄 Refit 接口，DTO 组装放在业务服务文件；不建立新导航或通用命令框架。
        private void InitializeSubmission(IForExcelSalesOrderApi service)
        {
            checkApi = service;
            CheckImportCommand = new AsyncRelayCommand(CheckImportAsync, () => IsEditable && HasMaintainPermission && checkApi != null && Lines.Count > 0);
            SaveDraftCommand = new AsyncRelayCommand(SaveDraftAsync, () => CanSaveDraft);
            RetrySaveCommand = new AsyncRelayCommand(SaveDraftAsync, () => !IsDisposed && !saving && HasPendingSave && HasMaintainPermission && Accept(pageVersion));
            NewImportCommand = new RelayCommand(StartNewImport, () => !IsDisposed && saveCompleted && !saving && Accept(pageVersion));
            ToggleNonStandardCommand = new RelayCommand<SalesOrderImportLine>(l => { if (IsEditable && Lines.Contains(l)) { CancelSearch(l); l.SetNonStandard(!l.IsNonStandard); } }, l => IsEditable && l != null && Lines.Contains(l));
            ChooseMatchCommand = new RelayCommand<SalesOrderImportLine>(l => { if (IsEditable && Lines.Contains(l)) l.ChooseMatchCandidate(); }, l => IsEditable && l != null && Lines.Contains(l));
            foreach (var command in new IRelayCommand[] { CheckImportCommand, SaveDraftCommand, RetrySaveCommand, NewImportCommand, ToggleNonStandardCommand, ChooseMatchCommand }) RegisterCommand(command);
        }
        // 编辑使整单授权快照失效；不会悄悄删除商业值或把匹配失败行转为非标。
        private void InvalidateSubmission()
        {
            if (resetting || applyingSubmission) return;
            documentRevision++; checkedRevision = -1; serverCanSave = false; acceptExistingOrders = false;
            SubmissionMessage = "资料已变化，请重新检验。";
        }
        /// <summary>检查最多 500 行；响应接纳核对账号、整单版本和逐行输入版本。</summary>
        public async Task CheckImportAsync()
        {
            if (!CheckImportCommand.CanExecute(null)) return;
            var local = Header.Error + string.Join("", Lines.Select(l => l.InputError));
            if (Lines.Count > 500 || !string.IsNullOrEmpty(local)) { SubmissionMessage = Lines.Count > 500 ? "检查最多支持 500 行，请缩小本次导入范围。" : local; return; }
            var version = pageVersion; var revision = documentRevision;
            var rows = Lines.ToArray(); var rowVersions = rows.Select(l => l.EditRevision).ToArray();
            var token = submissionLifetime.Token;
            var request = SalesOrderImportRequests.Check(Header, rows, acceptExistingOrders);
            checking = true; serverCanSave = false; checkedRevision = -1; CancelSearches(); ErrorMessage = "";
            SubmissionMessage = "正在检验并匹配产品…"; NotifyState();
            try
            {
                // 重复订单确认后只补充一次检查；不会自动保存或无限弹框。
                for (var attempt = 0; attempt < 2; attempt++)
                {
                    token.ThrowIfCancellationRequested();
                    var result = await checkApi.CheckAsync(request, token);
                    var repeat = false;
                    await DeliverSubmissionAsync(() =>
                    {
                        if (!Accept(version) || documentRevision != revision || token.IsCancellationRequested || !rows.SequenceEqual(Lines)
                            || rows.Where((l, i) => l.EditRevision != rowVersions[i]).Any()) return;
                        if (result == null || !result.Success || result.Data == null) { SubmissionMessage = result?.Error?.Message ?? result?.Message ?? "检验未返回有效结果。"; return; }
                        var data = result.Data;
                        if (data.Customer == null || !data.Customer.IsActive || data.Customer.Uid != Header.CustomerId
                            || !string.Equals(data.Customer.CustomerCode, Header.CustomerCode, StringComparison.OrdinalIgnoreCase)
                            || data.Lines == null || data.Lines.Count != rows.Length || data.Lines.Any(l => l == null)
                            || data.Lines.Select(l => l.ClientLineId).Distinct().Count() != rows.Length
                            || data.Lines.Any(l => !rows.Any(r => r.Uid == l.ClientLineId)))
                        { SubmissionMessage = string.Join("；", (data.Issues ?? new List<SalesOrderImportIssueDto>()).Select(i => i.Message)) + " 检验未返回完整且一致的客户和明细，不能保存。"; return; }
                        applyingSubmission = true;
                        bool valid;
                        try { valid = rows.Select((l, i) => l.TryApplyCheck(data.Lines.Single(r => r.ClientLineId == l.Uid), rowVersions[i])).All(v => v); }
                        finally { applyingSubmission = false; }
                        var issues = data.Issues ?? new List<SalesOrderImportIssueDto>();
                        SubmissionMessage = string.Join("；", issues.Where(i => !i.RequiresConfirmation || !i.Acknowledged).Select(i => i.Message));
                        if (valid && attempt == 0 && !acceptExistingOrders && data.ExistingOrders?.Count > 0
                            && issues.Any(i => i.Code == "ExistingOrders" && i.RequiresConfirmation && !i.Acknowledged))
                        {
                            var description = string.Join("\n", data.ExistingOrders.Select(o => o.OrderNo + " / Invoice: " + o.InvoiceNo + " / PO: " + o.CustomerPoNo
                                + " / 单据状态: " + o.DocumentStatus + " / 确认状态: " + o.ConfirmationStatus + " / " + o.Uid));
                            var confirmed = prompt.ConfirmDiscard("已存在相同客户和商业编号的订单：\n" + description + "\n仍要新建另一份草稿吗？");
                            // 确认框有嵌套消息循环，取消也必须重验身份，防止反馈污染新账号页面。
                            if (!Accept(version) || documentRevision != revision || token.IsCancellationRequested) return;
                            if (confirmed) { acceptExistingOrders = true; request.AcceptExistingOrders = true; repeat = true; }
                        }
                        serverCanSave = valid && data.CanSave && !repeat && !issues.Any(i => !i.RequiresConfirmation || !i.Acknowledged);
                        checkedRevision = serverCanSave ? revision : -1;
                        if (string.IsNullOrEmpty(SubmissionMessage)) SubmissionMessage = serverCanSave ? "检验通过，可以保存新草稿。" : "请处理明细中的匹配或输入问题，再重新检验。";
                        NotifyState();
                    });
                    if (!repeat) break;
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { /* 页面结束，不再提示或接纳。 */ }
            catch (Exception ex)
            {
                await DeliverSubmissionAsync(() => { if (Accept(version) && !token.IsCancellationRequested && revision == documentRevision) SubmissionMessage = errors.Describe(ex); });
            }
            finally { await DeliverSubmissionAsync(() => { if (Accept(version)) { checking = false; NotifyState(); } }); }
        }
        /// <summary>保存期间锁定输入；不明确的结果保留原请求，明确失败允许修正后重新检查。</summary>
        public async Task SaveDraftAsync()
        {
            if (saving || IsDisposed || !HasMaintainPermission || !Accept(pageVersion) || (pendingSave == null && !CanSaveDraft)) return;
            var version = pageVersion;
            if (pendingSave == null) pendingSave = SalesOrderImportRequests.Save(Header, Lines);
            var request = pendingSave;
            saving = true; SubmissionMessage = "正在保存草稿…"; CancelSearches(); NotifyState();
            try
            {
                var result = await api.SaveAsync(request);
                await DeliverSubmissionAsync(() =>
                {
                    if (!Accept(version) || !ReferenceEquals(request, pendingSave)) return;
                    var receipt = result?.Data?.Document?.Header;
                    if (result?.Success == true && receipt != null && receipt.Uid != Guid.Empty && receipt.CustomerId == request.Header.CustomerId && !string.IsNullOrWhiteSpace(receipt.OrderNo))
                    {
                        applyingSubmission = true;
                        try { Header.OrderNo = receipt.OrderNo; }
                        finally { applyingSubmission = false; }
                        SubmissionMessage = "草稿已保存：" + receipt.OrderNo + "（" + receipt.Uid + "）。可在客户端继续处理。";
                        pendingSave = null; saveCompleted = true; dirty = false; serverCanSave = false;
                    }
                    else if (result?.Success == false)
                        SaveFailed(result.Error?.Code, result.Error?.Message ?? result.Message);
                    else SaveFailed(ServiceErrorCodes.OutcomeUnknown, "服务器未返回确定的保存回执。");
                });
            }
            catch (Exception ex)
            {
                await DeliverSubmissionAsync(() =>
                {
                    if (!Accept(version) || !ReferenceEquals(request, pendingSave)) return;
                    // 只有标准结构化错误才能证明业务拒绝；网关、断网、超时按结果未知处理。
                    SaveFailed(SaveErrorCode(ex), errors.Describe(ex));
                });
            }
            finally { await DeliverSubmissionAsync(() => { if (Accept(version)) { saving = false; NotifyState(); } }); }
        }
        // 插件退出时调度入口可能已停止；不再向已关闭窗口投递错误或泄漏命令异常。
        private async Task DeliverSubmissionAsync(Action action)
        {
            if (IsDisposed) return;
            try { await dispatcher.InvokeAsync(() => { if (!IsDisposed) action(); }); }
            catch (OperationCanceledException) { /* 宿主已经退出，不恢复页面。 */ }
        }
        private bool unknownSave;
        // 首次确定拒绝可编辑；曾经结果未知后，权限或限流失败不能证明原请求未提交。
        private void SaveFailed(string code, string message)
        {
            unknownSave |= string.IsNullOrEmpty(code) || code == ServiceErrorCodes.OutcomeUnknown || code == ServiceErrorCodes.CommandBusy || code == ServiceErrorCodes.ServiceUnavailable;
            var keep = unknownSave && (string.IsNullOrEmpty(code) || new[] { ServiceErrorCodes.OutcomeUnknown, ServiceErrorCodes.CommandBusy,
                ServiceErrorCodes.ServiceUnavailable, ServiceErrorCodes.RateLimited, ServiceErrorCodes.Unauthenticated, ServiceErrorCodes.Forbidden }.Contains(code));
            SubmissionMessage = message + (keep ? " 保存结果尚未确定；请重试原请求。CommandId: " + PendingCommandId : " 请修正资料后重新检验。");
            if (!keep) { pendingSave = null; unknownSave = false; InvalidateSubmission(); SubmissionMessage = message + " 请重新检验后保存。"; }
        }
        // 仅解析错误代码，不打印或保存完整 HTTP 正文。
        private static string SaveErrorCode(Exception exception)
        {
            var error = exception as ApiException;
            if (string.IsNullOrWhiteSpace(error?.Content)) return null;
            try
            {
                using (var json = JsonDocument.Parse(error.Content))
                {
                    JsonElement success, detail, code;
                    if (json.RootElement.ValueKind == JsonValueKind.Object && json.RootElement.TryGetProperty("success", out success) && success.ValueKind == JsonValueKind.False
                        && json.RootElement.TryGetProperty("error", out detail) && detail.ValueKind == JsonValueKind.Object
                        && detail.TryGetProperty("code", out code) && code.ValueKind == JsonValueKind.String) return code.GetString();
                }
            }
            catch (JsonException) { /* 网关正文不能确定提交结果。 */ }
            return null;
        }
        // 清空成功单的输入，不复用已落库的行或 CommandId；共享基础资料仍可使用。
        private void StartNewImport()
        {
            if (!NewImportCommand.CanExecute(null)) return;
            resetting = true;
            try
            {
                CancelSearches(); Lines.Clear(); selectedLines.Clear(); acceptedCustomer = null; SelectedCustomer = null;
                header.PropertyChanged -= HeaderChanged; header = new SalesOrderImportHeader(); header.PropertyChanged += HeaderChanged;
                OnPropertyChanged(nameof(Header)); dirty = false; ResetSubmission(); ParseMessage = "";
            }
            finally { resetting = false; }
            RefreshOptions(); NotifyState();
        }
        // 账号变化或释放取消检查；保存没有取消契约，依靠会话和请求身份拒绝迟到回执。
        private void ResetSubmission()
        {
            submissionLifetime.Cancel(); submissionLifetime.Dispose(); submissionLifetime = new CancellationTokenSource();
            documentRevision++; checkedRevision = -1; serverCanSave = acceptExistingOrders = checking = saving = saveCompleted = unknownSave = false;
            pendingSave = null; SubmissionMessage = "";
        }
    }
}
