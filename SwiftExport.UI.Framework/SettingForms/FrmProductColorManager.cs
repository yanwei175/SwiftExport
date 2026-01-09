using AutoMapper;
using SwiftExport.AppLayer.DTOs;
using SwiftExport.AppLayer.Interfaces;
using SwiftExport.UI.Framework.Common;
using SwiftExport.UI.Framework.FrmUtility;
using SwiftExport.UI.Framework.FrmUtility.UIStyle;
using SwiftExport.UI.Framework.FrmUtility.Validation;
using SwiftExport.Utility;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using CurrentDto = SwiftExport.AppLayer.DTOs.ProductColorDto;
using CurrentEntity = SwiftExport.Core.Entities.ProductColor;



namespace SwiftExport.UI.Framework.AppForms
{
    public partial class FrmProductColorManager : Form
    {
        private readonly IProductColorService _service;
        private readonly IMapper _mapper;
        private BindingList<CurrentDto> BdListAllData;
        private readonly BindingSource BdSource1 = new BindingSource();
        public FrmProductColorManager(IProductColorService service, IMapper mapper)
        {

            InitializeComponent();
            _service = service;
            _mapper = mapper;
            this.Shown += async (s, e) => await LoadDsv();
            Dgv1.AutoGenerateColumns = false; //关闭自动生成列
            BdSource1.DataSource = BdListAllData;
            Dgv1.DataSource = BdSource1;
            BdSource1.EnableDirtyTracking<CurrentDto>();//启用脏行标记
            Dgv1.EnableDoubleBuffered()
                .ApplyModernStyle()
                .AddLineNumberColumn("No.", 80)//添加序号列
                .EnableSilentDataError()//拦截错误信息
                .EnableAutoTrim()//自动去除前后空格
                .SetNotNullColumnsColumns(nameof(CurrentDto.唯一颜色代码), nameof(CurrentDto.颜色代码))//设置不能为空列
                .SetUniqueColumns(nameof(CurrentDto.唯一颜色代码))//设置唯一列
                .SetAutoUcaseColumn(nameof(CurrentDto.唯一颜色代码), nameof(CurrentDto.颜色代码))//设置大写列
                .EnableDeleteKey()//设置Dlete清空单元格
                                  //.EnableEnterAsTab()//自定义回车换行
                ;
            _filterTimer.Tick += FilterTimer_Tick;

        }

        public async Task LoadDsv()
        {
            // 1. 后台线程：获取数据 + 映射 DTO + 准备好 List
            var dtoList = await Task.Run(async () =>
            {
                var rst = await _service.GetAllAsync(); // 不要再套一层 Task.Run

                if (!rst.Success)
                    return null;

                // 数据加工也放后台
                return _mapper.Map<List<CurrentDto>>(rst.Data);
            });

            // 2. 如果失败，直接返回
            if (dtoList == null) return;

            // 3. 回到 UI 线程（await 自动切回）更新 BindingList
            ThreadHelper.UIInvoke(() =>
            {
                if (BdListAllData == null)
                {
                    BdListAllData = new BindingList<CurrentDto>();
                    BdSource1.DataSource = BdListAllData;
                }

                BdListAllData.RaiseListChangedEvents = false;
                BdListAllData.Clear();

                foreach (var d in dtoList)
                    BdListAllData.Add(d);

                BdListAllData.RaiseListChangedEvents = true;
                BdListAllData.ResetBindings();
            });
        }

        private void UpdateBindingList(List<CurrentDto> items)
        {
            if (BdListAllData == null)
            {
                BdListAllData = new BindingList<CurrentDto>(items);
                BdSource1.DataSource = BdListAllData;
                BdListAllData.RaiseListChangedEvents = true;
                BdListAllData.ResetBindings();
                return;
            }

            BdListAllData.RaiseListChangedEvents = false;
            BdListAllData.Clear();
            foreach (var d in items)
                BdListAllData.Add(d);
            BdListAllData.RaiseListChangedEvents = true;
            BdListAllData.ResetBindings();
        }


        private void BdSource1_CurrentChanged(object sender, EventArgs e)
        {
            LblTips.GoodNews("");
            //Btn_Save.Enabled = BdSource1.Current is CurrentDto current && current.视图状态 == RowState.修改;
        }

        private async void TsBtn_Add_Click(object sender, EventArgs e)
        {
            this.LblTips.Text = "";
            BdSource1.EndEdit();
            Dgv1.EndEdit();

            // 确保数据已加载
            if (BdListAllData == null)
            {
                await LoadDsv();
                // 如果 LoadDsv 失败或者没数据，要防止下面报错
                if (BdListAllData == null) BdListAllData = new BindingList<CurrentDto>();
            }

            // 检查第一行是否为空（防止用户连续狂点新增）
            if (BdSource1.Count > 0)
            {
                var cell = Dgv1.Rows[0].Cells[nameof(CurrentDto.唯一颜色代码)];
                string cellValue = cell?.Value?.ToString();
                if (string.IsNullOrEmpty(cellValue))
                {
                    Dgv1.CurrentCell = cell;
                    Dgv1.BeginEdit(true);
                    return;
                }
            }

            // 创建新行
            var newSp = new CurrentDto
            {
                ID = -1,
                视图状态 = RowState.新增
            };

            // 插入到第一行
            BdListAllData.Insert(0, newSp);

            // 重新定位焦点
            if (Dgv1.RowCount > 0)
            {
                // 确保列名正确，建议用 index 或者 Dgv1.Columns[nameof(...)]
                var targetCell = Dgv1.Rows[0].Cells[nameof(CurrentDto.唯一颜色代码)];
                if (targetCell != null && targetCell.Visible)
                {
                    Dgv1.CurrentCell = targetCell;
                    Dgv1.BeginEdit(true);
                }
            }
        }

        private async void TsBtn_Refresh_Click(object sender, EventArgs e)
        {
            await LoadDsv();
        }
        private void Txt_FindResult_TextChanged(object sender, EventArgs e)
        {
            _filterTimer.Stop();
            _filterTimer.Start();
        }
        private async void TsBtn_Save_Click(object sender, EventArgs e)
        {
            // 1. 结束 DGV 编辑状态，确保所有数据已进入 DTO
            BdSource1.EndEdit();
            if (!Dgv1.ValidateAll())
            {
                LblTips.BadNews("数据验证不通过.");
                return;
            }

            // 2. 提取并分类数据
            var allDirty = BdListAllData.Where(x => x.视图状态 != RowState.正常).ToList();
            if (!allDirty.Any()) return;

            var addDtos = allDirty.Where(x => x.视图状态 == RowState.新增).ToList();
            var updateDtos = allDirty.Where(x => x.视图状态 == RowState.修改).ToList();
            var deleteDtos = allDirty.Where(x => x.视图状态 == RowState.删除).ToList();

            // 3. 映射为实体 (Entity)
            var addEntities = _mapper.Map<List<CurrentEntity>>(addDtos);
            var updateEntities = _mapper.Map<List<CurrentEntity>>(updateDtos);
            var deleteIds = deleteDtos.Select(x => x.ID).Where(id => id > 0).ToList();

            // 4. 调用你刚刚写好的 Service 方法
            var rst = await _service.BatchCUDByOnlyPkUniqueAsync(addEntities, updateEntities, deleteIds);
            ThreadHelper.UIInvoke(() =>
            {

                if (rst.Success)
                {
                    // 5. 使用你偏好的实例级静默更新开关
                    BdSource1.SilentUpdate(() =>
                    {
                        // A. 处理新增：利用仓库回写的 ID “转正”
                        for (int i = 0; i < addDtos.Count; i++)
                        {
                            addDtos[i].ID = addEntities[i].ID; // 仓库已回写，这里直接拿
                            addDtos[i].视图状态 = RowState.正常;
                        }

                        // B. 处理修改：直接“转正”
                        updateDtos.ForEach(x => x.视图状态 = RowState.正常);

                        // C. 处理删除：从 BindingList 中彻底移除
                        foreach (var d in deleteDtos)
                        {
                            BdListAllData.Remove(d);
                        }
                    });

                    this.LblTips.GoodNews($"全部保存成功！影响行数：{rst.Data.AffectedRowsTotal}");
                }
                else
                {
                    this.LblTips.BadNews("保存失败：" + rst.Message);
                }
            });

        }
        private async void TsBtn_Delete_Click(object sender, EventArgs e)
        {
            DataGridViewRow row = Dgv1.CurrentRow;
            if (row != null)
            {
                await DeleteAsync(row.DataBoundItem as CurrentDto);
            }
        }

        /// <summary>
        /// 单元格点击事件处理
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void Dgv1_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            try
            {
                int rowIndex = e.RowIndex;
                if (rowIndex < 0)
                {
                    return;
                }
                DataGridViewRow row = Dgv1.Rows[rowIndex];
                CurrentDto sp = null;
                switch (Dgv1.Columns[e.ColumnIndex].Name)
                {
                    case "Delete":
                        sp = Dgv1.CurrentRow.DataBoundItem as CurrentDto;
                        await DeleteAsync(sp);
                        break;
                    case "Save":
                        if (!Dgv1.ValidateRow(rowIndex))
                        {
                            this.LblTips.BadNews($"数据验证不通过,请检查输入数据!");
                            return;
                        }
                        sp = Dgv1.CurrentRow.DataBoundItem as CurrentDto;
                        if (sp.视图状态 == RowState.新增)
                        {
                            await AddAsync(sp);
                        }
                        else if (sp.视图状态 == RowState.修改)
                        {
                            await UpdateAsync(sp);
                        }
                        else
                        {
                            this.LblTips.GoodNews($"数据无变动,不需要提交修改!");
                        }
                        break;
                    default:

                        break;
                }
            }
            catch (Exception ex)
            {
                ThreadHelper.UIInvoke(() =>
                {
                    MessageBox.Show("发生错误：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                });
            }

        }
        private async Task UpdateAsync(CurrentDto sp)
        {
            try
            {
                var rst = await _service.UpdateAsync(_mapper.Map<CurrentEntity>(sp));
                ThreadHelper.UIInvoke(() =>
                {

                    if (rst.Success)
                    {
                        BdSource1.SilentUpdate(() => { _mapper.Map(rst.Data, sp); });
                        LblTips.GoodNews("保存成功.");
                        Dgv1.Invalidate();
                    }
                    else
                    {
                        this.LblTips.BadNews("保存失败:" + rst.Message);
                        MessageBox.Show("保存失败" + rst.Message);
                    }
                });
            }
            catch (Exception ex)
            {
                // 异常也要在 UI 线程报，否则 MessageBox 可能弹不出来或者卡死
                ThreadHelper.UIInvoke(() =>
                {
                    this.LblTips.BadNews("保存失败:" + ex.Message);
                    MessageBox.Show($"保存失败：{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                });
            }
        }
        private async Task AddAsync(CurrentDto sp)
        {
            BdSource1.EndEdit();
            var rst = await _service.AddAsync(_mapper.Map<CurrentEntity>(sp));
            ThreadHelper.UIInvoke(() =>
            {
                if (rst.Success)
                {
                    BdSource1.SilentUpdate(() => { _mapper.Map(rst.Data, sp); });
                    Dgv1.ValidateAll(); // 验证去红叉
                    MessageBox.Show("新增成功");
                }
                else
                {
                    MessageBox.Show("保存失败" + rst.Message);
                }
            });
        }
        private async Task DeleteAsync(CurrentDto sp)
        {
            try
            {
                if (sp.视图状态 == RowState.新增)
                {
                    BdSource1.Remove(sp);
                    this.LblTips.GoodNews("已删除新增数据");
                    return;
                }

                // 已保存的数据，调用API删除
                // 确认删除
                if (MessageBox.Show(
                    $"确定要删除 '{sp.唯一颜色代码}' 吗？",
                    "确认删除",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning) == DialogResult.No)
                {
                    return;
                }

                var rst = await _service.DeleteAsync(sp.ID);
                ThreadHelper.UIInvoke(() =>
                {
                    if (rst.Success)
                    {
                        BdSource1.Remove(sp);
                        this.LblTips.GoodNews("删除成功");
                        MessageBox.Show("删除成功", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    else
                    {
                        this.LblTips.BadNews("删除失败:" + rst.Message);
                        MessageBox.Show("删除失败：" + rst.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                });
            }
            catch (Exception ex)
            {
                ThreadHelper.UIInvoke(() =>
                {
                    this.LblTips.BadNews("删除异常:" + ex.Message);
                    MessageBox.Show($"删除异常：{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                });
            }
        }


        // 定义防抖计时器（300-500ms 是最佳体验）
        private readonly System.Windows.Forms.Timer _filterTimer = new System.Windows.Forms.Timer { Interval = 350 };
        private void FilterTimer_Tick(object sender, EventArgs e)
        {
            _filterTimer.Stop(); // 停止计时，防止重复触发

            string searchText = Txt_FindResult.Text.Trim().ToLower();

            // 执行筛选逻辑
            ApplyFilter(searchText);
        }
        private void ApplyFilter(string searchText)
        {
            BdSource1.SilentUpdate(() =>
            {
                if (string.IsNullOrWhiteSpace(searchText))
                {
                    // 搜索框为空，显示原始加载的全部数据
                    BdSource1.DataSource = BdListAllData;
                }
                else
                {
                    // 使用 LINQ 过滤内存中的 allData 集合
                    var filteredList = BdListAllData.Where(x =>
                    {
                        // 调用你的“空格模糊匹配”方法
                        return 通用搜索工具.空格模糊匹配(x.SearchKey, searchText);

                    }).ToList();

                    // 重新绑定过滤后的结果到界面
                    BdSource1.DataSource = new BindingList<CurrentDto>(filteredList);
                }
            });
        }

    }
}
