// 已淘汰：旧表头别名映射链路，仅保留历史兼容；新导入使用 ExcelReadProfile／ExcelReadField 地址配置。
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Data;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Client.VSTO.Api;
using SwiftExport.Client.VSTO.Authentication;
using SwiftExport.Client.VSTO.Features.Settings.ExcelFieldMappings.Models;
using SwiftExport.Client.VSTO.Hosting.Threading;
using SwiftExport.Client.VSTO.Mvvm;
using SwiftExport.Client.VSTO.Services.ExcelImport;
using SwiftExport.Contracts.Apis.SystemApis;
using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.SystemDtos.ExcelFieldMappings;

namespace SwiftExport.Client.VSTO.Features.Settings.ExcelFieldMappings.ViewModels
{
    /// <summary>维护单组字段映射的本地编辑快照；网络、会话和缓存结果只在页面存活时接纳。</summary>
    public sealed class ExcelFieldMappingViewModel : ViewModelBaseEx
    {
        private readonly IExcelFieldMappingApi api;
        private readonly ExcelImportRegistry imports;
        private readonly CurrentUserService user;
        private readonly SessionService session;
        private readonly IUiDispatcher dispatcher;
        private readonly ApiCallRunner errors;
        private readonly IExcelFieldMappingPrompt prompt;
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private long sessionVersion;
        private string currentGroup;
        private string keyword = string.Empty;
        private string statusMessage = string.Empty;
        private bool loaded;
        private bool dirty;

        /// <summary>注入页面需要的窄接口；构造期间不访问服务器。</summary>
        public ExcelFieldMappingViewModel(IExcelFieldMappingApi api,
            CurrentUserService user, SessionService session, IUiDispatcher dispatcher,
            ApiCallRunner errors, IExcelFieldMappingPrompt prompt, ExcelImportRegistry imports = null)
        {
            this.imports = imports ?? ExcelImportRegistry.CreateDefault();
            this.api = api; this.user = user; this.session = session;
            this.dispatcher = dispatcher; this.errors = errors; this.prompt = prompt;
            sessionVersion = session.Capture().Version;
            Title = "Excel 字段映射设置";
            RowsView = CollectionViewSource.GetDefaultView(Rows);
            RowsView.Filter = MatchesKeyword;
            SaveCommand = new AsyncRelayCommand(SaveCommandAsync, () => CanEdit && loaded && !IsBusy);
            ReloadCommand = new AsyncRelayCommand(InitializeAsync, () => CanEdit && !loaded && !IsBusy);
            AddCommand = new RelayCommand(AddRow, () => CanEdit && loaded && !IsBusy && Fields.Count > 0);
            DeleteCommand = new AsyncRelayCommand<ExcelFieldMappingRow>(DeleteAsync, row => CanEdit && loaded && !IsBusy && row != null);
            RegisterCommand(SaveCommand); RegisterCommand(ReloadCommand); RegisterCommand(AddCommand); RegisterCommand(DeleteCommand);
            session.Changed += OnSessionChanged;
        }

        public ObservableCollection<ExcelFieldGroupDto> Groups { get; } = new ObservableCollection<ExcelFieldGroupDto>();
        public ObservableCollection<ExcelTargetFieldDto> Fields { get; } = new ObservableCollection<ExcelTargetFieldDto>();
        public ObservableCollection<ExcelFieldMappingRow> Rows { get; } = new ObservableCollection<ExcelFieldMappingRow>();
        public ICollectionView RowsView { get; }
        public IAsyncRelayCommand SaveCommand { get; }
        public IAsyncRelayCommand ReloadCommand { get; }
        public IRelayCommand AddCommand { get; }
        public IAsyncRelayCommand<ExcelFieldMappingRow> DeleteCommand { get; }
        public string CurrentGroup { get { return currentGroup; } private set { SetProperty(ref currentGroup, value); } }
        public string Keyword
        {
            get { return keyword; }
            set { if (SetProperty(ref keyword, value)) RowsView.Refresh(); }
        }
        public string StatusMessage { get { return statusMessage; } private set { SetProperty(ref statusMessage, value); } }
        public bool CanEdit => !IsDisposed && user.HasPermission(PermissionKeys.SystemExcelFieldMappingManage)
            && session.Capture().Version == sessionVersion;

        /// <summary>首次打开时从注册模型生成分组，再按需加载默认组配置，重复 Loaded 不覆盖编辑。</summary>
        public async Task InitializeAsync()
        {
            if (loaded || IsBusy || !CanEdit) return;
            var version = sessionVersion;
            IsBusy = true;
            try
            {
                if (!Accept(version)) return;
                Groups.Clear();
                foreach (var group in imports.Groups) Groups.Add(new ExcelFieldGroupDto { GroupCode = group.GroupCode, DisplayName = group.DisplayName });
                if (Groups.Count == 0) { StatusMessage = "没有可维护的业务分组。"; return; }
                IsBusy = false;
                await SelectGroupAsync(Groups[0].GroupCode);
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { if (Accept(version)) StatusMessage = errors.Describe(error); }
            finally { IsBusy = false; RefreshCommands(); }
        }

        /// <summary>切组前处理当前组的修改，随后从模型生成字段目录并加载目标组全部映射。</summary>
        public async Task<bool> SelectGroupAsync(string groupCode)
        {
            if (!CanEdit || IsBusy || string.IsNullOrWhiteSpace(groupCode)) return false;
            var definition = imports.Find(groupCode);
            if (definition == null) { StatusMessage = "该组尚未注册导入模型，现有数据库配置保留。"; return false; }
            groupCode = definition.GroupCode;
            if (string.Equals(CurrentGroup, groupCode, StringComparison.Ordinal)) return true;
            if (dirty)
            {
                var choice = prompt.ResolveUnsavedChanges();
                if (choice == UnsavedMappingChoice.Cancel) return false;
                if (choice == UnsavedMappingChoice.Save && !await SaveAsync()) return false;
            }
            var version = sessionVersion;
            IsBusy = true; RefreshCommands();
            try
            {
                var mappings = await api.GetMappingsAsync(groupCode, true, lifetime.Token);
                if (!Accept(version)) return false;
                if (!mappings.Success)
                {
                    StatusMessage = mappings.Message;
                    return false;
                }
                ClearRows(); Fields.Clear();
                foreach (var field in definition.Fields) Fields.Add(new ExcelTargetFieldDto
                {
                    FieldKey = field.FieldKey, DisplayName = field.DisplayName,
                    SupportsCreate = field.SupportsCreate, SupportsUpdate = field.SupportsUpdate
                });
                foreach (var dto in mappings.Data ?? new ExcelFieldMappingDto[0])
                {
                    // 未知历史键必须可见且保留原值；不能让 ComboBox 无候选时回写 null。
                    if (!definition.ContainsField(dto.TargetFieldKey) && !Fields.Any(field => field.FieldKey == dto.TargetFieldKey))
                        Fields.Add(new ExcelTargetFieldDto { FieldKey = dto.TargetFieldKey, DisplayName = "[不支持] " + dto.TargetFieldKey });
                    AddTracked(ExcelFieldMappingRow.FromDto(dto));
                }
                CurrentGroup = groupCode; dirty = false; loaded = true;
                var unknown = Rows.Count(row => !definition.ContainsField(row.TargetFieldKey));
                StatusMessage = "已加载 " + Rows.Count + " 条映射。" + (unknown > 0 ? "有 " + unknown + " 个目标字段不受当前模型支持，请修正或停用。" : "");
                RowsView.Refresh();
                return true;
            }
            catch (OperationCanceledException) { return false; }
            catch (Exception error) { if (Accept(version)) StatusMessage = errors.Describe(error); return false; }
            finally { IsBusy = false; RefreshCommands(); }
        }

        private bool MatchesKeyword(object value)
        {
            var row = value as ExcelFieldMappingRow;
            if (row == null || string.IsNullOrWhiteSpace(Keyword)) return true;
            var term = Keyword.Trim();
            var name = Fields.FirstOrDefault(field => field.FieldKey == row.TargetFieldKey)?.DisplayName;
            return Contains(row.ExcelHeader, term) || Contains(row.TargetFieldKey, term)
                || Contains(name, term) || Contains(row.Remark, term);
        }

        private static bool Contains(string value, string keyword)
            => value != null && value.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;

        private void AddRow()
        {
            if (!CanEdit || Fields.Count == 0) return;
            Keyword = string.Empty;
            AddTracked(ExcelFieldMappingRow.New(Fields[0].FieldKey));
            dirty = true;
        }

        private void AddTracked(ExcelFieldMappingRow row) { row.PropertyChanged += OnRowChanged; Rows.Add(row); }
        private void ClearRows()
        {
            foreach (var row in Rows) row.PropertyChanged -= OnRowChanged;
            Rows.Clear();
        }
        private void OnRowChanged(object sender, PropertyChangedEventArgs args)
        {
            dirty = true;
            if (args.PropertyName == nameof(ExcelFieldMappingRow.ExcelHeader)
                || args.PropertyName == nameof(ExcelFieldMappingRow.TargetFieldKey)
                || args.PropertyName == nameof(ExcelFieldMappingRow.Remark)) RowsView.Refresh();
        }

        private Task SaveCommandAsync() { return SaveAsync(); }

        /// <summary>提交当前组所有行；成功后用服务端身份和版本替换本地快照。</summary>
        private async Task<bool> SaveAsync()
        {
            if (!CanEdit || !loaded || IsBusy) return false;
            var version = sessionVersion;
            var group = CurrentGroup;
            var request = new SaveExcelFieldMappingGroupRequest { Rows = Rows.Select(row => row.ToRequest()).ToList() };
            IsBusy = true; RefreshCommands();
            try
            {
                var definition = imports.Find(group);
                foreach (var row in request.Rows)
                {
                    if (row.IsEnabled && (definition == null || !definition.ContainsField(row.TargetFieldKey)))
                    { StatusMessage = "启用的映射含不受当前模型支持的字段：" + row.TargetFieldKey + "，请修正或停用。"; return false; }
                    var normalized = ExcelHeaderNormalizer.Normalize(row.ExcelHeader);
                    if (normalized.Length == 0 && (row.Uid == Guid.Empty || row.IsEnabled))
                    { StatusMessage = "新行必须填写表头；空表头种子行必须保持停用。"; return false; }
                }
                var result = await api.SaveGroupAsync(group, request, lifetime.Token);
                if (!Accept(version)) return false;
                if (!result.Success) { StatusMessage = result.Message; return false; }
                ClearRows();
                foreach (var dto in result.Data ?? new ExcelFieldMappingDto[0]) AddTracked(ExcelFieldMappingRow.FromDto(dto));
                dirty = false; RowsView.Refresh();
                StatusMessage = "旧字段映射已保存（已淘汰，不再维护导入缓存）。";
                return true;
            }
            catch (OperationCanceledException) { return false; }
            catch (Exception error) { if (Accept(version)) StatusMessage = errors.Describe(error); return false; }
            finally { IsBusy = false; RefreshCommands(); }
        }

        /// <summary>已保存行逐条调用删除 API；本地新行仅从当前草稿移除。</summary>
        private async Task DeleteAsync(ExcelFieldMappingRow row)
        {
            if (!CanEdit || !Rows.Contains(row) || IsBusy) return;
            if (row.Uid == Guid.Empty) { row.PropertyChanged -= OnRowChanged; Rows.Remove(row); dirty = true; return; }
            var version = sessionVersion;
            IsBusy = true; RefreshCommands();
            try
            {
                var result = await api.DeleteAsync(row.Uid, row.Version, lifetime.Token);
                if (!Accept(version)) return;
                if (!result.Success) { StatusMessage = result.Message; return; }
                row.PropertyChanged -= OnRowChanged; Rows.Remove(row);
                StatusMessage = "旧映射已删除（已淘汰，不再维护导入缓存）。";
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { if (Accept(version)) StatusMessage = errors.Describe(error); }
            finally { IsBusy = false; RefreshCommands(); }
        }

        // 会话可能从网络回调线程变化，页面状态仅在 Excel/WPF STA 清理。
        private async void OnSessionChanged(object sender, EventArgs args)
        {
            try
            {
                await dispatcher.InvokeAsync(() =>
                {
                    if (IsDisposed) return;
                    sessionVersion = session.Capture().Version;
                    loaded = false; dirty = false; CurrentGroup = null;
                    ClearRows(); Fields.Clear(); Groups.Clear();
                    StatusMessage = CanEdit ? "登录已切换，请重新加载字段映射。" : "当前账号无字段映射维护权限。";
                    RefreshCommands();
                });
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { errors.Describe(error); }
        }

        private bool Accept(long version) => !IsDisposed && session.Capture().Version == version && CanEdit;

        /// <inheritdoc />
        protected override void DisposeCore()
        {
            session.Changed -= OnSessionChanged;
            lifetime.Cancel(); lifetime.Dispose();
            ClearRows(); Fields.Clear(); Groups.Clear();
        }
    }
}
