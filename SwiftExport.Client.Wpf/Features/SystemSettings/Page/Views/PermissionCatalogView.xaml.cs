using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExportClient.Features.SystemSettings.RbacManagement.Permissions.Query;

namespace SwiftExportClient.Features.SystemSettings.Page.Views;

/// <summary>只读呈现服务端权限目录，并在完整已载入集合上筛选分组。</summary>
public partial class PermissionCatalogView : System.Windows.Controls.Page
{
    private readonly PermissionsQueryViewModel _model;

    /// <summary>共享 RBAC 读取模型；此页不暴露任何目录写命令。</summary>
    public PermissionCatalogView(PermissionsQueryViewModel model)
    {
        InitializeComponent();
        _model = model;
        DataContext = model;
        RefreshGroups();
    }

    /// <summary>目录加载或刷新后同步分组与可见计数。</summary>
    public void RefreshGroups()
    {
        var selected = GroupFilter.SelectedItem as string;
        GroupFilter.Items.Clear();
        GroupFilter.Items.Add("全部分组");
        foreach (var group in _model.Permissions.Select(item => item.GroupKey).Distinct().OrderBy(item => item))
            GroupFilter.Items.Add(group);
        GroupFilter.SelectedItem = selected is not null && GroupFilter.Items.Contains(selected) ? selected : "全部分组";
        UpdateCount();
    }

    private void OnFilterChanged(object sender, EventArgs e)
    {
        if (_model is null) return;
        var group = GroupFilter.SelectedItem as string;
        CollectionViewSource.GetDefaultView(_model.Permissions).Filter = item => item is ManagedPermissionDto permission
            && (group is null or "全部分组" || permission.GroupKey == group)
            && (string.IsNullOrWhiteSpace(SearchBox.Text)
                || permission.PermissionKey.Contains(SearchBox.Text, StringComparison.OrdinalIgnoreCase)
                || permission.DisplayName.Contains(SearchBox.Text, StringComparison.OrdinalIgnoreCase));
        UpdateCount();
    }

    private void UpdateCount()
    {
        if (_model is null) return;
        CountText.Text = $"{CollectionViewSource.GetDefaultView(_model.Permissions).Cast<object>().Count()} 条";
    }

    private async void OnRefresh(object sender, RoutedEventArgs e)
    {
        await _model.RefreshPermissionsPageAsync();
        RefreshGroups();
    }
}
