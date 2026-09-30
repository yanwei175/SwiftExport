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
using SwiftExport.Client.VSTO.Services.Caching;
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
        private readonly IReferenceDataCacheService cache;
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
        public ExcelFieldMappingViewModel(IExcelFieldMappingApi api, IReferenceDataCacheService cache,
            CurrentUserService user, SessionService session, IUiDispatcher dispatcher,
            ApiCallRunner errors, IExcelFieldMappingPrompt prompt)
        {
            this.api = api; this.cache = cache; this.user = user; this.session = session;
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

        /// <summary>首次打开时按需加载分组及默认组，重复 Loaded 不覆盖编辑。</summary>
        public async Task InitializeAsync()
        {
            if (loaded || IsBusy || !CanEdit) return;
            var version = sessionVersion;
            IsBusy = true;
            try
            {
                var result = await api.GetGroupsAsync(lifetime.Token);
                if (!Accept(version)) return;
                if (!result.Success) { StatusMessage = result.Message; return; }
                Groups.Clear();
                foreach (var group in result.Data ?? new ExcelFieldGroupDto[0]) Groups.Add(group);
                if (Groups.Count == 0) { StatusMessage = "没有可维护的业务分组。"; return; }
                IsBusy = false;
                await SelectGroupAsync(Groups[0].GroupCode);
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { if (Accept(version)) StatusMessage = errors.Describe(error); }
            finally { IsBusy = false; RefreshCommands(); }
        }

        /// <summary>切组前处理当前组的修改，随后加载目标组全部映射和字段目录。</summary>
        public async Task<bool> SelectGroupAsync(string groupCode)
        {
            if (!CanEdit || IsBusy || string.IsNullOrWhiteSpace(groupCode)) return false;
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
                var fields = await api.GetFieldsAsync(groupCode, lifetime.Token);
                var mappings = await api.GetMappingsAsync(groupCode, true, lifetime.Token);
                if (!Accept(version)) return false;
                if (!fields.Success || !mappings.Success)
                {
                    StatusMessage = !fields.Success ? fields.Message : mappings.Message;
                    return false;
                }
                ClearRows(); Fields.Clear();
                foreach (var field in fields.Data ?? new ExcelTargetFieldDto[0]) Fields.Add(field);
                foreach (var dto in mappings.Data ?? new ExcelFieldMappingDto[0]) AddTracked(ExcelFieldMappingRow.FromDto(dto));
                CurrentGroup = groupCode; dirty = false; loaded = true;
                StatusMessage = "已加载 " + Rows.Count + " 条映射。";
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
                foreach (var row in request.Rows)
                {
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
                var refreshed = await cache.RefreshExcelFieldMappingsAsync(group);
                if (!Accept(version)) return false;
                StatusMessage = refreshed.Success ? "字段映射已保存。" : "字段映射已保存，但导入缓存刷新失败：" + refreshed.Message;
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
                var refreshed = await cache.RefreshExcelFieldMappingsAsync(CurrentGroup);
                if (!Accept(version)) return;
                StatusMessage = refreshed.Success ? "映射已删除。" : "映射已删除，但导入缓存刷新失败：" + refreshed.Message;
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
