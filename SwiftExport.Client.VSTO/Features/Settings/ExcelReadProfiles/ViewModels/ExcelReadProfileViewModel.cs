using CommunityToolkit.Mvvm.Input;
using SwiftExport.Client.VSTO.Api;
using SwiftExport.Client.VSTO.Authentication;
using SwiftExport.Client.VSTO.Features.Sales.Orders;
using SwiftExport.Client.VSTO.Features.Settings.ExcelReadProfiles.Models;
using SwiftExport.Client.VSTO.Features.Settings.ExcelReadProfiles.Services;
using SwiftExport.Client.VSTO.Hosting.Threading;
using SwiftExport.Client.VSTO.Mvvm;
using SwiftExport.Client.VSTO.Services.ExcelImport;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.SystemDtos.ExcelReadProfiles;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SwiftExport.Client.VSTO.Features.Settings.ExcelReadProfiles.ViewModels
{
    /// <summary>维护销售或采购地址配置，分组从模型目录选择，业务资格不进入配置保存。</summary>
    public sealed class ExcelReadProfileViewModel : ViewModelBaseEx
    {
        private readonly ExcelReadProfileService service;
        private readonly IExcelAddressReader reader;
        private ExcelImportDefinition definition;
        private readonly ExcelImportRegistry registry;
        private readonly SessionService session;
        private readonly IUiDispatcher dispatcher;
        private readonly ApiCallRunner errors;
        private readonly ISalesOrderImportPrompt prompt;
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private readonly List<ExcelReadFieldModel> subscribed = new List<ExcelReadFieldModel>();
        // 已弃用的销售汇率配置不再创建编辑器；保留独立快照，保存其他字段时原样回传。
        private readonly List<ExcelReadFieldDto> deprecatedFields = new List<ExcelReadFieldDto>();
        private long sessionVersion;
        private Guid? uid, initialUid;
        private int version, endMode;
        private bool initialized, dirty, applying;
        private string name = "", worksheet = "", lastColumn = "", summary = "", remark = "", status = "";
        /// <summary>注入宿主边界，不在构造期间请求网络或创建 COM 对象。</summary>
        public ExcelReadProfileViewModel(ExcelReadProfileService service, IExcelAddressReader reader, ExcelImportRegistry registry,
            SessionService session, IUiDispatcher dispatcher, ApiCallRunner errors, ISalesOrderImportPrompt prompt)
        {
            this.service = service; this.reader = reader; this.registry = registry; definition = registry.Find("SalesOrder"); this.session = session;
            this.dispatcher = dispatcher; this.errors = errors; this.prompt = prompt; sessionVersion = session.Capture().Version;
            Title = "销售订单读取配置";
            ReloadCommand = new AsyncRelayCommand(ReloadAsync, () => CanEdit && !IsBusy);
            CreateCommand = new AsyncRelayCommand(CreateAsync, () => CanEdit && !IsBusy && !string.IsNullOrWhiteSpace(ConfigName));
            SaveCommand = new AsyncRelayCommand(SaveAsync, () => CanEdit && !IsBusy && uid.HasValue);
            DeleteCommand = new AsyncRelayCommand(DeleteAsync, () => CanEdit && !IsBusy && uid.HasValue);
            PickCommand = new AsyncRelayCommand<ExcelReadFieldModel>(PickAsync, row => CanEdit && !IsBusy && row != null && row.IsKnown);
            ClearCommand = new RelayCommand<ExcelReadFieldModel>(row => row.CellAddress = "", row => CanEdit && !IsBusy && row != null && row.IsKnown);
            PickLastColumnCommand = new AsyncRelayCommand(() => PickBoundaryAsync(true), () => CanEdit && !IsBusy);
            PickSummaryCommand = new AsyncRelayCommand(() => PickBoundaryAsync(false), () => CanEdit && !IsBusy);
            PreviewCommand = new AsyncRelayCommand(PreviewAsync, () => CanEdit && !IsBusy && uid.HasValue && !dirty);
            foreach (var cmd in new IRelayCommand[] { ReloadCommand, CreateCommand, SaveCommand, DeleteCommand, PickCommand, ClearCommand, PickLastColumnCommand, PickSummaryCommand, PreviewCommand }) RegisterCommand(cmd);
            session.Changed += SessionChanged;
        }
        public ObservableCollection<ExcelReadProfileListItemDto> Profiles { get; } = new ObservableCollection<ExcelReadProfileListItemDto>();
        /// <summary>业务目录来自显式注册模型，不扫描程序集或手写第二套字段表。</summary>
        public IReadOnlyList<ExcelImportDefinition> Groups => registry.Groups;
        /// <summary>当前业务组，只能经受控切换修改。</summary>
        public string CurrentGroupCode => definition.GroupCode;
        /// <summary>切换业务组前确认未保存内容；进行中禁止切换。</summary>
        public async Task<bool> SelectGroupAsync(string groupCode, Guid? profileUid = null)
        {
            var next = registry.Find(groupCode);
            if (next == null || !CanEdit || IsBusy) return false;
            if (next == definition)
            {
                if (!initialized) { initialUid = profileUid; return true; }
                return !profileUid.HasValue || await SelectAsync(profileUid.Value);
            }
            if (!Discard("切换业务分组将丢弃当前未保存配置，是否继续？") || !CanEdit) return false;
            applying = true;
            try
            {
                definition = next; uid = null; initialUid = profileUid; initialized = false; ClearRows(); Profiles.Clear();
                ConfigName = WorksheetName = Remark = LastRowColumn = SummaryCellAddress = ""; EndMode = 0; dirty = false;
                Title = next.DisplayName + "读取配置"; OnPropertyChanged(nameof(CurrentGroupCode)); OnPropertyChanged(nameof(CurrentUid)); OnPropertyChanged(nameof(HasChanges));
            }
            finally { applying = false; }
            await ReloadAsync(); return initialized;
        }
        public ObservableCollection<ExcelReadFieldModel> HeaderFields { get; } = new ObservableCollection<ExcelReadFieldModel>();
        public ObservableCollection<ExcelReadFieldModel> LineFields { get; } = new ObservableCollection<ExcelReadFieldModel>();
        /// <summary>当前已接纳的配置身份，切换失败保留原配置。</summary>
        public Guid? CurrentUid => uid;
        /// <summary>当前会话须具备配置维护权限，换号后禁止继续维护。</summary>
        public bool CanEdit => !IsDisposed && session.Capture().Version == sessionVersion && session.Capture().User?.Permissions?.Contains(PermissionKeys.SystemExcelFieldMappingManage) == true;
        /// <summary>请求进行中禁止编辑快照，防止保存响应覆盖请求期间的修改。</summary>
        public bool CanChangeFields => CanEdit && !IsBusy;
        /// <summary>用于切换和关闭确认的未保存标记。</summary>
        public bool HasChanges => dirty;
        /// <summary>配置名称，与原配置 UID 分开编辑，新建采用此名称。</summary>
        public string ConfigName { get => name; set { if (SetProperty(ref name, value)) Edited(); } }
        /// <summary>顶部统一的可空参考名称，不用于实际读取源定位。</summary>
        public string WorksheetName { get => worksheet; set { if (SetProperty(ref worksheet, value)) Edited(); } }
        /// <summary>明细结束方式，使用后端固定整数协议。</summary>
        public int EndMode { get => endMode; set { if (SetProperty(ref endMode, value)) Edited(); } }
        /// <summary>结束方式为一时判断末行的列字母。</summary>
        public string LastRowColumn { get => lastColumn; set { if (SetProperty(ref lastColumn, value)) Edited(); } }
        /// <summary>结束方式为二时指定汇总单元格地址。</summary>
        public string SummaryCellAddress { get => summary; set { if (SetProperty(ref summary, value)) Edited(); } }
        /// <summary>配置说明，不保存单据业务内容。</summary>
        public string Remark { get => remark; set { if (SetProperty(ref remark, value)) Edited(); } }
        /// <summary>当前配置完整性及操作反馈。</summary>
        public string Status { get => status; private set => SetProperty(ref status, value); }
        public IAsyncRelayCommand ReloadCommand { get; }
        public IAsyncRelayCommand CreateCommand { get; }
        public IAsyncRelayCommand SaveCommand { get; }
        public IAsyncRelayCommand DeleteCommand { get; }
        public IAsyncRelayCommand PreviewCommand { get; }
        public IAsyncRelayCommand<ExcelReadFieldModel> PickCommand { get; }
        public IRelayCommand<ExcelReadFieldModel> ClearCommand { get; }
        public IAsyncRelayCommand PickLastColumnCommand { get; }
        public IAsyncRelayCommand PickSummaryCommand { get; }
        /// <summary>窗口首次创建时定位导入页当前配置，重复打开不重置编辑。</summary>
        public void SetInitialProfile(Guid? profileUid) { if (!initialized) initialUid = profileUid; }
        /// <summary>首次加载幂等，失败后可重试，不预加载基础资料。</summary>
        public async Task InitializeAsync() { if (!initialized) await ReloadAsync(); }
        /// <summary>切换配置前确认丢弃，取消时调用方应恢复下拉选择。</summary>
        public async Task<bool> SelectAsync(Guid profileUid)
        {
            if (!CanEdit || IsBusy) return false;
            if (uid == profileUid) return true;
            if (!Discard("切换配置将丢弃未保存的地址修改。是否继续？")) return false;
            await RunAsync(async token => { var result = await service.GetAsync(profileUid, token); Ensure(result.Success, result.Message); await UiAsync(() => Apply(result.Data)); });
            return uid == profileUid;
        }
        /// <summary>普通关闭时确认未保存内容；读写进行中拒绝普通关闭。</summary>
        public bool CanClose() => !IsBusy && Discard("配置尚未保存，是否丢弃并关闭？");
        // 页面可能在确认框或 await 期间换号，必须重新检查当前会话。
        private bool Discard(string text) => !dirty || prompt.ConfirmDiscard(text);
        private bool Accept() => CanEdit && !lifetime.IsCancellationRequested;
        // 重新读取列表与详情，所有 UI 接纳均通过调度器。
        private async Task ReloadAsync()
        {
            if (!Discard("重新加载将丢弃未保存修改。是否继续？")) return;
            await RunAsync(async token =>
            {
                var result = await service.ListAsync(CurrentGroupCode, token); Ensure(result.Success, result.Message);
                Guid? chosen = null;
                await UiAsync(() =>
                {
                    Profiles.Clear(); foreach (var item in result.Data) Profiles.Add(item);
                    chosen = (Profiles.FirstOrDefault(p => p.Uid == (uid ?? initialUid)) ?? Profiles.FirstOrDefault())?.Uid;
                });
                if (!Accept()) return;
                if (chosen.HasValue) { var detail = await service.GetAsync(chosen.Value, token); Ensure(detail.Success, detail.Message); await UiAsync(() => Apply(detail.Data)); }
                else await UiAsync(() => { ClearRows(); uid = null; dirty = false; Status = "没有配置，请填写名称并新建。"; });
                await UiAsync(() => { initialized = true; OnPropertyChanged(nameof(CurrentUid)); });
            });
        }
        // 创建空配置后接纳服务器身份，不覆盖其他已保存配置。
        private async Task CreateAsync()
        {
            var newName = ConfigName;
            if (!Discard("新建空配置将丢弃当前未保存修改。是否继续？")) return;
            await RunAsync(async token =>
            {
                var result = await service.CreateAsync(CurrentGroupCode, newName, token); Ensure(result.Success, result.Message);
                await UiAsync(() => Apply(result.Data)); if (!Accept()) return;
                var list = await service.ListAsync(CurrentGroupCode, token); Ensure(list.Success, list.Message);
                await UiAsync(() => { Profiles.Clear(); foreach (var item in list.Data) Profiles.Add(item); OnPropertyChanged(nameof(CurrentUid)); });
            });
        }
        // 保存当前完整快照，失败保留本地编辑和版本。
        private async Task SaveAsync()
        {
            await RunAsync(async token =>
            {
                var request = BuildRequest(); var profileUid = uid.Value;
                var result = await service.SaveAsync(profileUid, request, token); Ensure(result.Success, result.Message);
                await UiAsync(() =>
                {
                    Apply(result.Data); var item = Profiles.FirstOrDefault(p => p.Uid == uid);
                    if (item != null) { var index = Profiles.IndexOf(item); Profiles[index] = new ExcelReadProfileListItemDto { Uid = result.Data.Uid, ConfigName = result.Data.ConfigName, GroupCode = result.Data.GroupCode, Version = result.Data.Version }; }
                    OnPropertyChanged(nameof(CurrentUid));
                });
            });
        }
        // 用户确认后删除整份配置，身份失效后不接纳响应。
        private async Task DeleteAsync()
        {
            if (!prompt.ConfirmDiscard("删除整份配置及其字段地址，是否继续？")) return;
            await RunAsync(async token =>
            {
                var profileUid = uid.Value;
                var result = await service.DeleteAsync(profileUid, version, token); Ensure(result.Success, result.Message);
                await UiAsync(() =>
                {
                    var item = Profiles.FirstOrDefault(p => p.Uid == uid); if (item != null) Profiles.Remove(item);
                    uid = null; ClearRows(); dirty = false; Status = "配置已删除，请选择其他配置或新建。"; OnPropertyChanged(nameof(CurrentUid));
                });
            });
        }
        /// <summary>组装活动字段修改；未知字段及已弃用销售汇率配置保留，不静默删除旧数据。</summary>
        public UpdateExcelReadProfileRequest BuildRequest()
        {
            var fields = HeaderFields.Concat(LineFields).Where(r => !string.IsNullOrWhiteSpace(r.CellAddress)).Select(row =>
            {
                if (!ExcelAddressRules.TryCell(row.CellAddress, out var r, out var c)) throw new InvalidOperationException(row.DisplayName + "地址无效。");
                if (row.IsKnown && row.IsDownward == row.IsHeader) throw new InvalidOperationException(row.DisplayName + "：单头读取一次，明细须向下读取。");
                return new ExcelReadFieldDto
                {
                    TargetFieldKey = row.FieldKey,
                    CellAddress = ExcelAddressRules.ColumnName(c) + r,
                    WorksheetName = string.IsNullOrWhiteSpace(WorksheetName) ? null : WorksheetName.Trim(),
                    IsDownward = row.IsDownward
                };
            }).ToList();
            fields.AddRange(deprecatedFields.Select(field => new ExcelReadFieldDto
            {
                TargetFieldKey = field.TargetFieldKey, CellAddress = field.CellAddress,
                WorksheetName = field.WorksheetName, IsDownward = field.IsDownward
            }));
            return new UpdateExcelReadProfileRequest
            {
                ExpectedVersion = version,
                ConfigName = ConfigName,
                Remark = Remark,
                EndMode = EndMode,
                LastRowColumn = EndMode == 1 ? LastRowColumn?.Trim().ToUpperInvariant() : null,
                SummaryCellAddress = EndMode == 2 ? SummaryCellAddress?.Trim() : null,
                Fields = fields
            };
        }
        /// <summary>重建活动字段编辑器；销售旧汇率配置只保留原记录，不开放地址编辑。</summary>
        private void Apply(ExcelReadProfileDto profile)
        {
            if (!Accept()) return;
            if (profile == null || profile.GroupCode != CurrentGroupCode) throw new InvalidOperationException("配置不属于当前业务分组。");
            applying = true;
            try
            {
                ClearRows(); uid = profile.Uid; version = profile.Version; ConfigName = profile.ConfigName; Remark = profile.Remark; EndMode = profile.EndMode;
                LastRowColumn = profile.LastRowColumn; SummaryCellAddress = profile.SummaryCellAddress;
                // 旧汇率参考表名只属于兼容记录，不能清空或改变活动字段的统一表名。
                var names = profile.Fields.Where(f => !ExcelAddressRules.IsDeprecatedField(profile.GroupCode, f.TargetFieldKey))
                    .Select(f => f.WorksheetName).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToArray();
                WorksheetName = names.Length == 1 ? names[0] : "";
                foreach (var field in definition.Fields)
                {
                    var saved = profile.Fields.FirstOrDefault(f => f.TargetFieldKey == field.FieldKey);
                    AddRow(new ExcelReadFieldModel
                    {
                        FieldKey = field.FieldKey,
                        DisplayName = field.DisplayName,
                        IsHeader = field.ModelType == definition.HeaderType,
                        CellAddress = saved?.CellAddress ?? "",
                        IsDownward = saved?.IsDownward ?? field.ModelType == definition.LineType
                    });
                }
                deprecatedFields.AddRange(profile.Fields.Where(f => ExcelAddressRules.IsDeprecatedField(profile.GroupCode, f.TargetFieldKey))
                    .Select(field => new ExcelReadFieldDto { TargetFieldKey = field.TargetFieldKey, CellAddress = field.CellAddress,
                        WorksheetName = field.WorksheetName, IsDownward = field.IsDownward }));
                foreach (var field in profile.Fields.Where(f => !definition.ContainsField(f.TargetFieldKey)
                    && !ExcelAddressRules.IsDeprecatedField(profile.GroupCode, f.TargetFieldKey)))
                    AddRow(new ExcelReadFieldModel { FieldKey = field.TargetFieldKey, DisplayName = "未知字段（保留）：" + field.TargetFieldKey, IsKnown = false, CellAddress = field.CellAddress, IsDownward = field.IsDownward, IsHeader = !field.IsDownward });
                dirty = false; Status = names.Length > 1 ? "参考工作表名称不一致；顶部填写统一名称，保存后统一。" : "配置已加载。";
                if (deprecatedFields.Count > 0) Status += "\n旧销售汇率地址已弃用，不读取；配置记录继续保留。";
                var issues = ExcelAddressRules.Validate(profile, definition); if (issues.Count > 0) Status += "\n" + string.Join("\n", issues);
                OnPropertyChanged(nameof(CurrentUid)); OnPropertyChanged(nameof(HasChanges));
            }
            finally { applying = false; }
        }
        // 统一登记字段通知，清除与释放必须对称解绑。
        private void AddRow(ExcelReadFieldModel row) { (row.IsHeader ? HeaderFields : LineFields).Add(row); row.PropertyChanged += RowChanged; subscribed.Add(row); }
        // 解绑旧字段并清空可观察集合，不保留来源业务值。
        private void ClearRows() { foreach (var row in subscribed) row.PropertyChanged -= RowChanged; subscribed.Clear(); HeaderFields.Clear(); LineFields.Clear(); deprecatedFields.Clear(); }
        /// <summary>忙碌变化同时刷新整个配置编辑区。</summary>
        protected override void OnPropertyChanged(PropertyChangedEventArgs args)
        {
            base.OnPropertyChanged(args);
            if (args.PropertyName == nameof(IsBusy) || args.PropertyName == nameof(CanEdit)) OnPropertyChanged(nameof(CanChangeFields));
        }
        private void RowChanged(object sender, PropertyChangedEventArgs args) { if (args.PropertyName != nameof(ExcelReadFieldModel.Preview)) Edited(); }
        // 只把用户修改标记为未保存，服务器回显不产生脏状态。
        private void Edited() { if (!applying) { dirty = true; OnPropertyChanged(nameof(HasChanges)); RefreshCommands(); } }
        // 取消选址不修改地址，返回后核对会话和字段实例。
        private async Task PickAsync(ExcelReadFieldModel row)
        {
            await RunAsync(async token => { var selected = await reader.SelectCellAsync(token); if (selected == null) return; await UiAsync(() => { if (!subscribed.Contains(row)) return; row.CellAddress = selected.Address; row.Preview = selected.Preview; if (string.IsNullOrWhiteSpace(WorksheetName)) WorksheetName = selected.WorksheetName; }); });
        }
        // 将选中单格转换为判断列或汇总地址。
        private async Task PickBoundaryAsync(bool column)
        {
            await RunAsync(async token =>
            {
                var selected = await reader.SelectCellAsync(token); if (selected == null) return;
                await UiAsync(() => { ExcelAddressRules.TryCell(selected.Address, out var r, out var c); if (column) LastRowColumn = ExcelAddressRules.ColumnName(c); else SummaryCellAddress = selected.Address; });
            });
        }
        /// <summary>仅预览已保存活动字段；旧销售汇率配置不决定预览方向，也不触发 Excel 读取。</summary>
        private async Task PreviewAsync()
        {
            await RunAsync(async token =>
            {
                var result = await service.GetAsync(uid.Value, token); Ensure(result.Success, result.Message); if (!Accept()) return;
                var issues = ExcelAddressRules.Validate(result.Data, definition); if (issues.Count > 0) throw new InvalidOperationException(string.Join("\n", issues));
                // 每次只预览一种部分，保证整次操作使用同一工作表快照。
                var fields = result.Data.Fields.Where(f => !ExcelAddressRules.IsDeprecatedField(result.Data.GroupCode, f.TargetFieldKey)).ToArray();
                if (fields.Length == 0)
                {
                    await UiAsync(() => Status = "旧销售汇率地址已弃用；没有活动字段可预览，配置记录继续保留。");
                    return;
                }
                var detail = fields.Any(f => f.IsDownward);
                var data = await reader.ReadAsync(result.Data, detail, token); if (!Accept()) return;
                await UiAsync(() =>
                {
                    foreach (var value in data.Rows.FirstOrDefault() ?? new List<ExcelAddressValue>())
                    { var row = subscribed.FirstOrDefault(r => r.FieldKey == value.FieldKey); if (row != null) row.Preview = value.Issue ?? Convert.ToString(value.Value); }
                    Status = "已预览" + (detail ? "首条明细" : "单头") + "：" + data.WorkbookName + " / " + data.WorksheetName;
                });
            });
        }
        // 所有异步入口集中门禁和错误观察，避免关闭、换号和确认框导致迟到回写。
        private async Task RunAsync(Func<CancellationToken, Task> operation)
        {
            if (!Accept() || IsBusy) return;
            IsBusy = true; ErrorMessage = "";
            try { await operation(lifetime.Token); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { try { await UiAsync(() => ErrorMessage = ex is InvalidOperationException ? ex.Message : errors.Describe(ex)); } catch (OperationCanceledException) { } }
            finally { try { await dispatcher.InvokeAsync(() => { if (!IsDisposed) { IsBusy = false; RefreshCommands(); } }); } catch (OperationCanceledException) { } }
        }
        private Task UiAsync(Action action) => dispatcher.InvokeAsync(() => { if (Accept()) action(); });
        private static void Ensure(bool success, string message) { if (!success) throw new InvalidOperationException(message ?? "配置请求失败。"); }
        // 身份通知统一投递到 UI，取消当前操作并锁定旧窗口。
        private async void SessionChanged(object sender, EventArgs args)
        {
            try { await dispatcher.InvokeAsync(() => { if (IsDisposed || session.Capture().Version == sessionVersion) return; lifetime.Cancel(); ClearRows(); Profiles.Clear(); uid = null; dirty = false; Status = "账号已变更，请关闭后重新打开配置页。"; OnPropertyChanged(nameof(CanEdit)); OnPropertyChanged(nameof(CurrentUid)); RefreshCommands(); }); }
            catch (OperationCanceledException) { }
        }
        /// <summary>取消请求并解绑身份及字段通知，不关闭宿主 Dispatcher。</summary>
        protected override void DisposeCore() { lifetime.Cancel(); session.Changed -= SessionChanged; ClearRows(); lifetime.Dispose(); }
    }
}
