using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Client.VSTO.Features.Settings.ExcelReadProfiles.Services;
using SwiftExport.Client.VSTO.Services.ExcelImport;
using SwiftExport.Client.VSTO.Features.Sales.Orders.Services;
using SwiftExport.Contracts.DTOs.SystemDtos.ExcelReadProfiles;

namespace SwiftExport.Client.VSTO.Features.Sales.Orders.ViewModels
{
    public sealed partial class SalesOrderImportViewModel
    {
        private bool parsing;
        private bool applyingParsedPart;
        private string parseMessage = "";
        /// <summary>显示地址读取的结果及需要用户补充的内容。</summary>
        public string ParseMessage { get => parseMessage; private set => SetProperty(ref parseMessage, value); }
        private ExcelReadProfileService profileService;
        private IExcelAddressReader addressReader;
        private SalesOrderAddressParser addressParser;
        private ExcelImportDefinition addressDefinition;
        private CancellationTokenSource addressLifetime = new CancellationTokenSource();
        private ExcelReadProfileListItemDto selectedProfile;
        private ExcelReadProfileDto readProfile;
        private long profileRequest;
        private bool profilesLoading, settingProfile, refreshPending;
        /// <summary>当前页面按需持有配置摘要，Excel 启动不加载。</summary>
        public ObservableCollection<ExcelReadProfileListItemDto> ReadProfiles { get; } = new ObservableCollection<ExcelReadProfileListItemDto>();
        /// <summary>选择配置只获取详情，不读取 Excel、不覆盖订单编辑。</summary>
        public ExcelReadProfileListItemDto SelectedReadProfile
        {
            get => selectedProfile;
            set { if (SetProperty(ref selectedProfile, value) && !settingProfile) { readProfile = null; LoadProfileAsync(); } }
        }
        /// <summary>按需更新配置，失败保留页面业务内容。</summary>
        public IAsyncRelayCommand RefreshReadProfilesCommand { get; private set; }
        /// <summary>读取配置中的单头地址，不提取表头标签。</summary>
        public IAsyncRelayCommand ReadHeaderCommand { get; private set; }
        /// <summary>按统一结束边界读取明细，包含隐藏行。</summary>
        public IAsyncRelayCommand ReadLinesCommand { get; private set; }
        /// <summary>请求视图打开独立配置窗口，ViewModel 不创建窗口或访问句柄。</summary>
        public IRelayCommand DesignReadProfileCommand { get; private set; }
        /// <summary>视图在自己的生命周期内桥接配置窗口入口。</summary>
        public event Action<Guid?> DesignReadProfileRequested;
        /// <summary>由视图反馈窗口打开失败，不让命令回调异常进入 Excel 消息循环。</summary>
        public void ReportConfigurationOpenFailure(Exception error) { if (!IsDisposed) ErrorMessage = errors.Describe(error); }
        private void InitializeAddresses(ExcelReadProfileService service, IExcelAddressReader reader, ExcelImportRegistry registry)
        {
            profileService = service; addressReader = reader;
            registry = registry ?? ExcelImportRegistry.CreateDefault(); addressDefinition = registry.Find("SalesOrder"); addressParser = new SalesOrderAddressParser(registry);
            RefreshReadProfilesCommand = new AsyncRelayCommand(RefreshReadProfilesAsync, () => !IsDisposed && profileService != null && !profilesLoading && !parsing && Accept(pageVersion));
            ReadHeaderCommand = new AsyncRelayCommand(() => ReadAddressesAsync(false), CanReadAddresses);
            ReadLinesCommand = new AsyncRelayCommand(() => ReadAddressesAsync(true), CanReadAddresses);
            DesignReadProfileCommand = new RelayCommand(() => DesignReadProfileRequested?.Invoke(SelectedReadProfile?.Uid), () => !IsDisposed && !parsing && !SubmissionLocked && !profilesLoading && Accept(pageVersion)
                && session.Capture().User.Permissions?.Contains(SwiftExport.Contracts.DTOs.Auth.PermissionKeys.SystemExcelFieldMappingManage) == true);
            foreach (var command in new IRelayCommand[] { RefreshReadProfilesCommand, ReadHeaderCommand, ReadLinesCommand, DesignReadProfileCommand }) RegisterCommand(command);
            if (profileService != null) profileService.Changed += ReadProfilesChanged;
        }
        /// <summary>读取和配置请求期间锁定配置选择，避免旧配置结果覆盖新选择。</summary>
        public bool CanSelectReadProfile => !IsDisposed && !parsing && !SubmissionLocked && !profilesLoading && Accept(pageVersion);
        private bool CanReadAddresses() => IsEditable && !profilesLoading && addressReader != null && readProfile != null;
        /// <summary>Loaded 时按需加载配置，与基础资料请求分开；重复 Loaded 不覆盖当前选择。</summary>
        public async Task EnsureReadProfilesAsync()
        {
            Task loading = null;
            await dispatcher.InvokeAsync(() => { if (ReadProfiles.Count == 0 && !profilesLoading) loading = RefreshReadProfilesAsync(); });
            if (loading != null) await loading;
        }
        /// <summary>更新摘要与当前完整快照，不把配置加入共享基础资料缓存。</summary>
        public async Task RefreshReadProfilesAsync()
        {
            if (profileService == null || profilesLoading || parsing || !Accept(pageVersion)) return;
            var version = pageVersion; var request = ++profileRequest; var token = addressLifetime.Token;
            profilesLoading = true; OnPropertyChanged(nameof(CanSelectReadProfile)); RefreshCommands();
            try
            {
                var list = await profileService.ListAsync(token);
                if (!Accept(version) || request != profileRequest) return;
                if (!list.Success) throw new InvalidOperationException(list.Message);
                Guid? chosen = null;
                await dispatcher.InvokeAsync(() =>
                {
                    if (!Accept(version) || request != profileRequest) return;
                    var uid = SelectedReadProfile?.Uid;
                    settingProfile = true;
                    try { ReadProfiles.Clear(); foreach (var item in list.Data) ReadProfiles.Add(item); SelectedReadProfile = ReadProfiles.FirstOrDefault(p => p.Uid == uid) ?? ReadProfiles.FirstOrDefault(); }
                    finally { settingProfile = false; }
                    readProfile = null; chosen = SelectedReadProfile?.Uid;
                });
                if (!Accept(version) || request != profileRequest) return;
                if (chosen.HasValue)
                {
                    var detail = await profileService.GetAsync(chosen.Value, token);
                    if (!detail.Success) throw new InvalidOperationException(detail.Message);
                    await dispatcher.InvokeAsync(() =>
                    {
                        if (!Accept(version) || request != profileRequest) return;
                        readProfile = detail.Data;
                        var issues = ExcelAddressRules.Validate(readProfile, addressDefinition);
                        ParseMessage = issues.Count == 0 ? "读取配置已加载，选择读取单头或明细。" : string.Join("\n", issues);
                    });
                }
                else await dispatcher.InvokeAsync(() => { if (Accept(version) && request == profileRequest) ParseMessage = "没有销售读取配置，请通过设计读取配置新建。"; });
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { await ReportAddressErrorAsync(version, request, ex); }
            finally { await FinishProfileRequestAsync(version, request); }
        }
        // 合并写入通知触发的刷新；当前查询结束后再请求，避免吞掉进行中收到的变更。
        private async Task FinishProfileRequestAsync(long version, long request)
        {
            Task loading = null;
            try
            {
                await dispatcher.InvokeAsync(() =>
                {
                    if (!Accept(version) || request != profileRequest) return;
                    profilesLoading = false; OnPropertyChanged(nameof(CanSelectReadProfile)); RefreshCommands();
                    if (refreshPending && !parsing) { refreshPending = false; loading = RefreshReadProfilesAsync(); }
                });
                if (loading != null) await loading;
            }
            catch (OperationCanceledException) { }
        }
        private async void LoadProfileAsync()
        {
            var request = ++profileRequest; var version = pageVersion;
            var selected = SelectedReadProfile; if (selected == null) { profilesLoading = false; OnPropertyChanged(nameof(CanSelectReadProfile)); RefreshCommands(); return; }
            if (profileService == null || !Accept(version)) return;
            profilesLoading = true; OnPropertyChanged(nameof(CanSelectReadProfile)); RefreshCommands();
            try
            {
                var result = await profileService.GetAsync(selected.Uid, addressLifetime.Token);
                if (!Accept(version) || request != profileRequest) return;
                if (!result.Success) throw new InvalidOperationException(result.Message);
                await dispatcher.InvokeAsync(() => { if (Accept(version) && request == profileRequest) { readProfile = result.Data;
                    ParseMessage = string.Join("\n", ExcelAddressRules.Validate(readProfile, addressDefinition)); } });
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { await ReportAddressErrorAsync(version, request, ex); }
            finally { await FinishProfileRequestAsync(version, request); }
        }
        // 配置变更通知不读 Excel；读取进行中延迟刷新，避免同时替换配置快照。
        private async void ReadProfilesChanged(object sender, EventArgs args)
        {
            try { await dispatcher.InvokeAsync(() => { if (IsDisposed) return; if (parsing || profilesLoading) refreshPending = true; else RefreshReadProfilesCommand.Execute(null); }); }
            catch (OperationCanceledException) { }
        }
        /// <summary>读取活动地址并受控接纳；仅含废弃汇率的部分跳过读取，保留现有销售输入。</summary>
        private async Task ReadAddressesAsync(bool detail)
        {
            if (!CanReadAddresses()) return;
            var version = pageVersion; var uid = readProfile.Uid; var token = addressLifetime.Token;
            parsing = true; CancelSearches(); ErrorMessage = ""; NotifyState();
            try
            {
                // 开始读取前重新获取已保存快照，不使用未保存修改或过期结构状态。
                var saved = await profileService.GetAsync(uid, token); if (!Accept(version)) return;
                if (!saved.Success) throw new InvalidOperationException(saved.Message);
                var issues = ExcelAddressRules.Validate(saved.Data, addressDefinition);
                if (issues.Count > 0) throw new InvalidOperationException(string.Join("\n", issues));
                if (saved.Data.Fields.Any(f => ExcelAddressRules.IsDeprecatedField(saved.Data.GroupCode, f.TargetFieldKey))
                    && !saved.Data.Fields.Any(f => f.IsDownward == detail && !ExcelAddressRules.IsDeprecatedField(saved.Data.GroupCode, f.TargetFieldKey)))
                {
                    await dispatcher.InvokeAsync(() => { if (Accept(version)) ParseMessage = "旧销售汇率地址已弃用；此部分没有活动字段，保留当前输入。"; });
                    return;
                }
                var snapshot = await addressReader.ReadAsync(saved.Data, detail, token); if (!Accept(version)) return;
                var result = await Task.Run(() => addressParser.Parse(snapshot), token);
                await dispatcher.InvokeAsync(() =>
                {
                    if (!Accept(version)) return;
                    if (HasEdits && !prompt.ConfirmDiscard(detail ? "读取将替换全部明细，保留单头。是否继续？" : "读取将替换单头，保留已选客户和全部明细。是否继续？")) return;
                    if (!Accept(version)) return;
                    applyingParsedPart = true;
                    try
                    {
                        if (detail)
                        {
                            selectedLines.Clear(); Lines.Clear();
                            foreach (var line in result.Lines) { ConfigureSearch(line); Lines.Add(line); }
                        }
                        else
                        {
                            if (result.Header == null) throw new InvalidOperationException("没有可读取的单头字段。");
                            result.Header.CustomerId = Header.CustomerId; result.Header.CustomerCode = Header.CustomerCode;
                            header.PropertyChanged -= HeaderChanged; header = result.Header; header.PropertyChanged += HeaderChanged; OnPropertyChanged(nameof(Header));
                            foreach (var line in Lines) line.InvalidateCheck();
                        }
                    }
                    finally { applyingParsedPart = false; }
                    if (detail) LinesChanged(Lines, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
                    InvalidateSubmission(); dirty = true; ParseMessage = result.Message; RefreshOptions(); NotifyState();
                });
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { await ReportAddressErrorAsync(version, null, ex); }
            finally
            {
                var refresh = false;
                try { await dispatcher.InvokeAsync(() => { parsing = false; if (!IsDisposed) { NotifyState(); refresh = refreshPending; refreshPending = false; } }); }
                catch (OperationCanceledException) { }
                if (refresh && Accept(version))
                {
                    Task loading = null;
                    try { await dispatcher.InvokeAsync(() => { if (Accept(version)) loading = RefreshReadProfilesAsync(); }); if (loading != null) await loading; }
                    catch (OperationCanceledException) { }
                }
            }
        }
        // 错误反馈也必须投递到 UI；无同步上下文时 await 不能保证回到 Excel STA。
        private async Task ReportAddressErrorAsync(long version, long? request, Exception ex)
        {
            try { await dispatcher.InvokeAsync(() => { if (Accept(version) && (!request.HasValue || request == profileRequest))
                ErrorMessage = ex is InvalidOperationException ? ex.Message : errors.Describe(ex); }); }
            catch (OperationCanceledException) { }
        }

        private void ResetAddressSession()
        {
            addressLifetime.Cancel(); addressLifetime.Dispose(); addressLifetime = new CancellationTokenSource();
            profileRequest++; profilesLoading = false; readProfile = null; settingProfile = true;
            try { ReadProfiles.Clear(); SelectedReadProfile = null; } finally { settingProfile = false; }
        }
        private void DisposeAddresses()
        {
            addressLifetime.Cancel(); addressLifetime.Dispose(); if (profileService != null) profileService.Changed -= ReadProfilesChanged;
            DesignReadProfileRequested = null;
        }
    }
}
