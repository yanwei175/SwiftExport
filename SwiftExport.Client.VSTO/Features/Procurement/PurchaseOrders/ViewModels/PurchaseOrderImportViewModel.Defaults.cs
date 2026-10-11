using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.Apis.CustomerApis;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Customers;

namespace SwiftExport.Client.VSTO.Features.Procurement.PurchaseOrders.ViewModels
{
    public sealed partial class PurchaseOrderImportViewModel
    {
        private readonly ICustomerPoConfigApi configApi;
        private CustomerDto selectedCustomer;
        private readonly HashSet<string> requestedPairs = new HashSet<string>();
        private CancellationTokenSource defaultsRequest;
        private long defaultsSequence;
        private bool configLoading;
        private string defaultsMessage = "";
        /// <summary>缓存候选；只有明确选择或 Excel 代码唯一定位后才接纳客户身份。</summary>
        public CustomerDto SelectedCustomer { get => selectedCustomer; set => SetProperty(ref selectedCustomer, value); }
        /// <summary>确认客户选择，保留明细并撤销旧客户匹配。</summary>
        public IRelayCommand CustomerSelectedCommand { get; }
        /// <summary>明确清空客户，保留商业输入并禁止匹配。</summary>
        public IRelayCommand CustomerClearedCommand { get; }
        /// <summary>显式重新获取双方配置，仍只补空值。</summary>
        public IAsyncRelayCommand RefreshDefaultsCommand { get; }
        /// <summary>非阻断的配置加载、币种及权限反馈。</summary>
        public string DefaultsMessage { get => defaultsMessage; private set => SetProperty(ref defaultsMessage, value); }
        /// <summary>配置读取期间保留手工编辑，只暂时阻止整单检验和保存。</summary>
        public bool IsDefaultsLoading => configLoading;
        /// <summary>独立匹配仅要求客户和创建权限，不要求供应商及完整单头。</summary>
        public bool CanMatchSkus => IsEditable && HasCreatePermission && customerId != Guid.Empty;
        private bool HasParties => customerId != Guid.Empty && SelectedSupplier != null && SelectedSupplier.Uid != Guid.Empty && !SelectedSupplier.IsDeleted;
        private void CommitCustomer() => CommitCustomer(SelectedCustomer);
        // 候选来自缓存且代码唯一才接纳，不把手输文字当成真实客户。
        private void CommitCustomer(CustomerDto candidate)
        {
            if (!IsEditable) return;
            var valid = candidate != null && candidate.Uid != Guid.Empty && !candidate.IsDeleted
                && ReferenceData.Customers.Any(c => c.Uid == candidate.Uid && !c.IsDeleted)
                && ReferenceData.Customers.Count(c => !c.IsDeleted && string.Equals(c.CustomerCode, candidate.CustomerCode, StringComparison.OrdinalIgnoreCase)) == 1;
            applyingParsedPart = true;
            try { Header.CustomerCode = valid ? candidate.CustomerCode : ""; }
            finally { applyingParsedPart = false; }
            ApplyCustomer(valid ? candidate : null);
            dirty = true; InvalidateSubmission(); NotifyState(); RequestDefaults();
        }
        // Excel 保留原代码；找不到唯一有效缓存候选时要求人工明确选择。
        private void ResolveCustomerCode()
        {
            var matches = ReferenceData.Customers.Where(c => !c.IsDeleted && c.Uid != Guid.Empty
                && string.Equals(c.CustomerCode?.Trim(), Header.CustomerCode?.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
            ApplyCustomer(matches.Length == 1 ? matches[0] : null);
            if (customerId == Guid.Empty && !string.IsNullOrWhiteSpace(Header.CustomerCode))
                DefaultsMessage = "Excel 客户代码未定位到唯一客户，请明确选择客户后匹配。";
            RequestDefaults();
        }
        // 客户边界变化只清身份和检查，不清数量、价格、描述及其他手工值。
        private void ApplyCustomer(CustomerDto candidate)
        {
            var changed = customerId != (candidate?.Uid ?? Guid.Empty);
            SelectedCustomer = candidate;
            customerId = candidate?.Uid ?? Guid.Empty; CustomerName = candidate?.CustomerName ?? "";
            if (!changed) return;
            CancelDefaults();
            applyingSubmission = true;
            try { foreach (var line in Lines) line.ResetMatch(); }
            finally { applyingSubmission = false; }
            InvalidateSubmission(); NotifyState();
        }
        // 自动查询也经统一异常处理，不让 async void 把网络异常送入 Excel 消息循环。
        private async void RequestDefaults() { await LoadDefaultsAsync(false); }
        private void CancelDefaults()
        {
            if (configLoading) DefaultsMessage = "配置读取已取消，已有输入保留。";
            defaultsSequence++; defaultsRequest?.Cancel(); defaultsRequest?.Dispose(); defaultsRequest = null;
            configLoading = false;
        }
        // 每组合只自动尝试一次；响应接纳检查双方、请求序号、会话与页面生命周期。
        private async Task LoadDefaultsAsync(bool force)
        {
            if (!IsEditable || !HasParties || configLoading) return;
            var customer = customerId; var supplier = SelectedSupplier.Uid;
            var key = customer.ToString() + "/" + supplier.ToString();
            if (!force && requestedPairs.Contains(key)) return;
            if (session.Capture().User.Permissions?.Contains(PermissionKeys.CustomersPoConfigView) != true)
            { DefaultsMessage = "没有客户采购配置查看权限，请手工填写默认资料。"; return; }
            requestedPairs.Add(key); CancelDefaults();
            defaultsRequest = new CancellationTokenSource(); var token = defaultsRequest.Token;
            var sequence = defaultsSequence; var version = pageVersion;
            configLoading = true; DefaultsMessage = "正在获取客户采购默认配置…"; NotifyState();
            try
            {
                var response = await configApi.GetByPartiesAsync(customer, supplier, token);
                await DeliverAsync(() =>
                {
                    if (!Accept(version) || token.IsCancellationRequested || sequence != defaultsSequence
                        || customerId != customer || SelectedSupplier?.Uid != supplier) return;
                    var config = response?.Data;
                    if (response?.Success != true || config == null)
                    { DefaultsMessage = response?.Error?.Message ?? response?.Message ?? "没有双方配置，可手工填写或重新获取。"; return; }
                    if (config.CustomerId != customer || config.SupplierId != supplier)
                    { DefaultsMessage = "配置双方身份不一致，未接纳；请手工填写或重试。"; return; }
                    FillDefaults(config);
                    DefaultsMessage = "默认资料已补入空值，已有内容保持原样。"
                        + (!string.IsNullOrWhiteSpace(config.Currency) && !string.Equals(config.Currency, "CNY", StringComparison.OrdinalIgnoreCase)
                            ? " 配置币种不是 CNY；本次采购仍固定 CNY。" : "");
                });
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception ex) { await DeliverAsync(() => { if (Accept(version) && sequence == defaultsSequence && !token.IsCancellationRequested) DefaultsMessage = errors.Describe(ex) + " 可手工填写或重新获取。"; }); }
            finally { await DeliverAsync(() => { if (Accept(version) && sequence == defaultsSequence) { configLoading = false; defaultsRequest?.Dispose(); defaultsRequest = null; NotifyState(); } }); }
        }
        // 显式白名单补空，配置 DTO 不参与编辑；数值原文非空（包括非法输入）也必须保留。
        private void FillDefaults(CustomerPoConfigDto config)
        {
            if (string.IsNullOrWhiteSpace(Header.InletHoseType)) Header.InletHoseType = config.InletHoseType;
            if (string.IsNullOrWhiteSpace(Header.AeratorType)) Header.AeratorType = config.AeratorType;
            if (string.IsNullOrWhiteSpace(Header.PalletType)) Header.PalletType = config.PalletType;
            if (string.IsNullOrWhiteSpace(Header.BoxType)) Header.BoxType = config.BoxType;
            if (string.IsNullOrWhiteSpace(Header.LabelType)) Header.LabelType = config.LabelType;
            if (string.IsNullOrWhiteSpace(Header.InstructionManualType)) Header.InstructionManualType = config.IMType;
            if (string.IsNullOrWhiteSpace(Header.CartonType)) Header.CartonType = config.CartonType;
            if (string.IsNullOrWhiteSpace(Header.LogoType)) Header.LogoType = config.LogoType;
            if (string.IsNullOrWhiteSpace(Header.SalesPerson)) Header.SalesPerson = config.SalesPerson;
            if (string.IsNullOrWhiteSpace(Header.PurchasePerson)) Header.PurchasePerson = config.Purchaser;
            if (string.IsNullOrWhiteSpace(Header.DiscountRateText)) Header.DiscountRateText = config.DefaultDiscountRate?.ToString(CultureInfo.CurrentCulture) ?? "";
            if (string.IsNullOrWhiteSpace(Header.DiscountRemark)) Header.DiscountRemark = config.DiscountRemark;
            if (string.IsNullOrWhiteSpace(Header.Remark)) Header.Remark = config.Note;
        }
    }
}
