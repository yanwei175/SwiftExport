using SwiftExport.AppLayer.DTOs; // 确保引用了你的 BaseDto
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

public static class BindingSourceExtensions
{
    // 核心黑科技：为每个 BindingSource 实例分配一个独立的布尔值
    // 就像给 BindingSource 偷偷加了一个名为 "IsSilent" 的私有字段
    private static readonly ConditionalWeakTable<BindingSource, StrongBox<bool>> _statusTable =
        new ConditionalWeakTable<BindingSource, StrongBox<bool>>();

    public static void EnableDirtyTracking<T>(this BindingSource bs) where T : BaseDto
    {
        bs.ListChanged += (s, e) =>
        {
            // 获取当前这个 BindingSource 实例的静默状态，默认为 false
            if (_statusTable.TryGetValue(bs, out var box) && box.Value)
                return;

            if (e.ListChangedType == ListChangedType.ItemChanged)
            {
                if (e.PropertyDescriptor?.Name == nameof(BaseDto.视图状态)) return;

                if (e.NewIndex >= 0 && e.NewIndex < bs.Count)
                {
                    if (bs[e.NewIndex] is T dto && dto.视图状态 == RowState.正常)
                    {
                        dto.视图状态 = RowState.修改;
                    }
                }
            }
        };
    }

    public static void SilentUpdate(this BindingSource bs, Action action)
    {
        // 1. 获取你之前定义的那个状态盒子（ConditionalWeakTable）
        var box = _statusTable.GetOrCreateValue(bs);

        try
        {
            box.Value = true;       // 开启静默模式
            BaseDto.Loading = true; // 开启全局加载标记
            action?.Invoke();
        }
        finally
        {
            BaseDto.Loading = false;
            box.Value = false;      // 关闭静默模式

            // 2. 这是核心：因为 DTO 没通知，所以这里必须强制刷新界面
            bs.ResetBindings(false);
        }
    }

    /// <summary>
    /// 获取所有需要提交到数据库的脏数据（新增 + 修改）
    /// </summary>
    public static List<T> GetChanges<T>(this BindingSource bs) where T : BaseDto
    {
        // 关键：在获取数据前强制结束当前的编辑，确保 UI 上的最新值已写入 DTO
        bs.EndEdit();

        return bs.Cast<T>()
                 .Where(x => x.视图状态 == RowState.新增 || x.视图状态 == RowState.修改)
                 .ToList();
    }



}