using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using SwiftExport.Client.VSTO.Api;
using SwiftExport.Client.VSTO.Authentication;
using SwiftExport.Client.VSTO.Diagnostics;
using SwiftExport.Client.VSTO.Features.Settings.ExcelFieldMappings;
using SwiftExport.Client.VSTO.Features.Settings.ExcelFieldMappings.ViewModels;
using SwiftExport.Client.VSTO.Features.Settings.ExcelFieldMappings.Views;
using SwiftExport.Client.VSTO.Hosting.Threading;
using SwiftExport.Client.VSTO.Testing;
using SwiftExport.Contracts.Apis.SystemApis;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.SystemDtos.ExcelFieldMappings;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Client.VSTO.WpfTests
{
    /// <summary>在独立 STA 验证设置页加载、筛选范围、整组提交和逐行删除。</summary>
    internal static class FieldMappingPageChecks
    {
        /// <summary>运行无需 Excel 或真实服务器的页面行为断言。</summary>
        internal static void Run(Action<bool, string> check)
        {
            var tokenStore = new MemoryTokenStore();
            using (var session = new SessionService(tokenStore))
            {
                session.TryCommit(0, "test", new MeDto
                {
                    UserId = "admin", UserName = "admin",
                    Permissions = new List<string> { "System.ExcelFieldMapping.Manage" }
                });
                var api = new MemoryApi();
                var prompt = new MemoryPrompt();
                using (var model = new ExcelFieldMappingViewModel(api, new ImportCache(),
                    new CurrentUserService(session), session, new ImmediateDispatcher(),
                    new ApiCallRunner(new AddInLog()), prompt))
                {
                    var view = new ExcelFieldMappingView(model);
                    check(ReferenceEquals(view.DataContext, model), "设置页连接 ViewModel");
                    model.InitializeAsync().GetAwaiter().GetResult();
                    view.Measure(new Size(800, 600));
                    view.Arrange(new Rect(0, 0, 800, 600));
                    view.UpdateLayout();
                    check(model.Rows.Count == 2 && model.CurrentGroup == "PurchaseOrder", "首次加载含停用种子");
                    model.Keyword = "数量";
                    check(model.RowsView.Cast<object>().Count() == 1, "Keyword 仅过滤显示");
                    model.Rows[0].ExcelHeader = "日期";
                    model.SaveCommand.ExecuteAsync(null).GetAwaiter().GetResult();
                    check(api.LastSave != null && api.LastSave.Rows.Count == 2, "保存包含 Keyword 隐藏行");
                    check(api.LastSave.Rows.Any(row => row.ExcelHeader == string.Empty && !row.IsEnabled), "空表头种子保持停用");
                    check(model.Rows.All(row => row.Uid != Guid.Empty && row.Version > 0), "保存后接纳身份及版本");
                    model.Keyword = string.Empty;
                    model.Rows[0].Remark = "本地修改";
                    prompt.Choice = UnsavedMappingChoice.Cancel;
                    check(!model.SelectGroupAsync("SalesOrder").GetAwaiter().GetResult()
                        && model.CurrentGroup == "PurchaseOrder", "取消切组保留本地修改");
                    prompt.Choice = UnsavedMappingChoice.Discard;
                    check(model.SelectGroupAsync("SalesOrder").GetAwaiter().GetResult(), "放弃修改后可以切组");
                    model.AddCommand.Execute(null);
                    check(model.Rows.Count == 1, "可添加同字段新别名");
                    model.Rows[0].ExcelHeader = "客户型号"; model.Rows[0].IsEnabled = true;
                    model.SaveCommand.ExecuteAsync(null).GetAwaiter().GetResult();
                    check(model.Rows[0].Uid != Guid.Empty, "新别名由服务端分配身份");
                    model.DeleteCommand.ExecuteAsync(model.Rows[0]).GetAwaiter().GetResult();
                    check(api.DeleteCalls == 1 && model.Rows.Count == 0, "已保存行逐条调用删除 API");
                    session.TryEnd(session.Capture().Version);
                    check(!model.CanEdit && model.Groups.Count == 0 && model.Fields.Count == 0,
                        "退出登录后清除设置页数据并禁止编辑");
                }
            }
        }

        private sealed class MemoryPrompt : IExcelFieldMappingPrompt
        {
            internal UnsavedMappingChoice Choice;
            public UnsavedMappingChoice ResolveUnsavedChanges() { return Choice; }
        }

        private sealed class MemoryTokenStore : ITokenStore
        {
            public string Read() { return null; }
            public void Write(string token) { }
            public void Clear() { }
        }

        private sealed class ImmediateDispatcher : IUiDispatcher
        {
            public Task InvokeAsync(Action action) { action(); return Task.CompletedTask; }
        }

        private sealed class MemoryApi : IExcelFieldMappingApi
        {
            private readonly Dictionary<string, List<ExcelFieldMappingDto>> groups = new Dictionary<string, List<ExcelFieldMappingDto>>
            {
                ["PurchaseOrder"] = new List<ExcelFieldMappingDto>
                {
                    new ExcelFieldMappingDto { Uid = Guid.NewGuid(), Version = 1, GroupCode = "PurchaseOrder", TargetFieldKey = "Header.OrderDate", ExcelHeader = "采购日期", IsEnabled = true },
                    new ExcelFieldMappingDto { Uid = Guid.NewGuid(), Version = 1, GroupCode = "PurchaseOrder", TargetFieldKey = "Line.Quantity", ExcelHeader = string.Empty, IsEnabled = false }
                },
                ["SalesOrder"] = new List<ExcelFieldMappingDto>()
            };
            internal SaveExcelFieldMappingGroupRequest LastSave;
            internal int DeleteCalls;

            public Task<ServiceResult<IReadOnlyList<ExcelFieldGroupDto>>> GetGroupsAsync(CancellationToken cancellationToken = default(CancellationToken))
                => Task.FromResult(ServiceResult<IReadOnlyList<ExcelFieldGroupDto>>.Ok(new[]
                {
                    new ExcelFieldGroupDto { GroupCode = "PurchaseOrder", DisplayName = "采购订单" },
                    new ExcelFieldGroupDto { GroupCode = "SalesOrder", DisplayName = "销售订单" }
                }));
            public Task<ServiceResult<IReadOnlyList<ExcelTargetFieldDto>>> GetFieldsAsync(string groupCode, CancellationToken cancellationToken = default(CancellationToken))
                => Task.FromResult(ServiceResult<IReadOnlyList<ExcelTargetFieldDto>>.Ok(new[]
                {
                    new ExcelTargetFieldDto { FieldKey = groupCode == "PurchaseOrder" ? "Header.OrderDate" : "Line.CustomerSku", DisplayName = "日期" },
                    new ExcelTargetFieldDto { FieldKey = "Line.Quantity", DisplayName = "数量" }
                }));
            public Task<ServiceResult<IReadOnlyList<ExcelFieldMappingDto>>> GetMappingsAsync(string groupCode, bool includeDisabled = false, CancellationToken cancellationToken = default(CancellationToken))
                => Task.FromResult(ServiceResult<IReadOnlyList<ExcelFieldMappingDto>>.Ok(groups[groupCode].ToArray()));
            public Task<ServiceResult<IReadOnlyList<ExcelFieldMappingDto>>> SaveGroupAsync(string groupCode, SaveExcelFieldMappingGroupRequest request, CancellationToken cancellationToken = default(CancellationToken))
            {
                LastSave = request;
                groups[groupCode] = request.Rows.Select(row => new ExcelFieldMappingDto
                {
                    Uid = row.Uid == Guid.Empty ? Guid.NewGuid() : row.Uid,
                    Version = row.ExpectedVersion + 1, GroupCode = groupCode,
                    ExcelHeader = row.ExcelHeader, TargetFieldKey = row.TargetFieldKey,
                    IsEnabled = row.IsEnabled, Remark = row.Remark
                }).ToList();
                return Task.FromResult(ServiceResult<IReadOnlyList<ExcelFieldMappingDto>>.Ok(groups[groupCode].ToArray()));
            }
            public Task<ServiceResult<bool>> DeleteAsync(Guid uid, int expectedVersion, CancellationToken cancellationToken = default(CancellationToken))
            {
                DeleteCalls++;
                foreach (var rows in groups.Values) rows.RemoveAll(row => row.Uid == uid);
                return Task.FromResult(ServiceResult<bool>.Ok(true));
            }
            public Task<ServiceResult<ExcelFieldMappingDto>> CreateAsync(CreateExcelFieldMappingRequest request, CancellationToken cancellationToken = default(CancellationToken))
                => throw new NotSupportedException();
            public Task<ServiceResult<ExcelFieldMappingDto>> UpdateAsync(Guid uid, UpdateExcelFieldMappingRequest request, CancellationToken cancellationToken = default(CancellationToken))
                => throw new NotSupportedException();
        }
    }
}
