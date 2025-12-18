using AutoMapper;
using SwiftExport.AppLayer.DTOs;
using SwiftExport.AppLayer.Interfaces;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities;
using SwiftExprot.Utility;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SwiftExport.UiKongJianFrameWork.AppForms
{
    public partial class FrmCustomersManager : Form
    {
        private readonly ICustomersService _customersService;
        // 保存完整的客户列表（查询结果）
        private List<CustomersDto> allCustomers;
        private readonly IMapper _mapper;
        private List<CustomersDto> filteredCustomers; // 保存筛选后的结果
        private System.Threading.CancellationTokenSource _filterCts; // 用于防抖
        //private BindingSource  _bindingSource;
        //构造函数
        public FrmCustomersManager(ICustomersService customersService, IMapper mapper)
        {
            InitializeComponent();

            _customersService = customersService;
            _mapper = mapper;
            初始化顶部按钮状态();
            // 初始化用户控件
            this.DoubleBuffered = true;// 启用双缓冲，减少闪烁

            // 绑定分页控件的数据加载方法（DataSplitUserControl.BinDingDataGridViewAsync 是 Func<Task>）
            this.dataSplitUserControl1.刷新DataGridAsync = async () => await 刷新DataGridAsync();
            this.dataSplitUserControl1.PageSize = 20;
            // 加载字段下拉（异步方法为 void，因为构造函数不能 await）
            字段下拉框赋值();
            DataGridView1.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            DataGridView1.MultiSelect = false;
            DataGridView1.ColumnHeadersHeight = 60;
            DataGridView1.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.EnableResizing;
        }
        
        //初始化顶部按钮状态
        private void 初始化顶部按钮状态()
        {
            // 此处可根据需要启用或禁用按钮
            ToolStripButton_Save.Enabled = false;
            ToolStripButton_Delete.Enabled = false;
            this.操作提示.Text = "";
        }

        // 查询客户（带条件或全部）
        private async Task 查询客户Async(string field,string value)
        {
            this.操作提示.Text = "";
            try
            {
                switch (field)
                {
                    case "客户代码":
                        field = "CustomerCode";
                        break;
                    case "客户名称":
                        field = "CustomerName";
                        break;
                    case "联系人":
                        field = "CustomerContact";
                        break;
                }

                    var 条件字典 = new Dictionary<string, QueryCondition>
                    {
                        { field, new QueryCondition { 操作符 = 查询操作符.模糊匹配, 值1 = value } }
                    };

                    var rst = await _customersService.GetByUserWhereAsync(条件字典);

                    if (!rst.Success)
                    {
                        MessageBox.Show("查询客户数据时出错: " + rst.Message);
                        return;
                    }

                // 保存完整列表
                allCustomers = _mapper.Map<List<CustomersDto>>(rst.Data.ToList());
                filteredCustomers = null; // 确保清除筛选状态

                // 更新分页控件
                dataSplitUserControl1.TotalRecords = allCustomers.Count;
                dataSplitUserControl1.PageIndex = 0;
                dataSplitUserControl1.改变控件显示状态();

                await 刷新DataGridAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载客户数据时出错: {ex.Message}");
            }
        }
        // 字段查询的下拉框
        private async void 字段下拉框赋值()
        {
            try
            {
                // 初始化可查询字段列表
                var strings = new List<string> { "--请选择--", "客户代码", "客户名称", "联系人" };
                this.ToolStripComboBox_QueryField.Items.Clear();
                this.ToolStripComboBox_QueryField.Items.AddRange(strings.ToArray());
            }
            catch (Exception ex)
            {
                MessageBox.Show($"初始化字段列表时出错: {ex.Message}");
            }
        }
        // 根据分页控件状态加载当前页数据
        private async Task 刷新DataGridAsync()
        {
            // 假设：
            // 1. DataGridView1 已经绑定到 customersDtoBindingSource。
            // 2. allCustomers 是主列表。
            // 3. filteredCustomers 是筛选后的列表（如果存在）。

            try
            {
                // 确保使用当前活动的列表（如果filteredCustomers有值，则使用它）
                var source = filteredCustomers ?? allCustomers;

                // 【修正点 1】判断 source 是否为空：如果为空，说明没有数据，应该清除视图。
                if (source == null)
                {
                    customersDtoBindingSource.DataSource = null; // 清除数据绑定
                    dataSplitUserControl1.TotalRecords = 0;
                    dataSplitUserControl1.改变控件显示状态();
                    return;
                }

                // 【修正点 2】确保数据源总记录数和状态是最新的
                dataSplitUserControl1.TotalRecords = source.Count;
                dataSplitUserControl1.改变控件显示状态();

                // 1. 获取分页参数
                var pageIndex = dataSplitUserControl1.PageIndex;
                var pageSize = Math.Max(1, dataSplitUserControl1.PageSize);

                // 2. 执行内存分页
                var paged = source
                    .Skip(pageIndex * pageSize)
                    .Take(pageSize)
                    .ToList();

                // 3. 【修正点 3】将当前页数据子集绑定到 BindingSource
                // 这是连接分页逻辑和 DataGridView 的关键步骤
                customersDtoBindingSource.DataSource = paged;

                // 4. 重置绑定，确保 DataGridView 立即更新（通常在设置 DataSource 后不需要，但可以作为保障）
                customersDtoBindingSource.ResetBindings(false);

                // 5. 可选：确保 DataGridView 状态（列宽、只读等）设置正确
                // SetupDataGridViewColumns(); 
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载客户数据时出错: {ex.Message}");
            }
        }

        // 新增一行（将一个空 DTO 加入内存列表并刷新当前页）
        private async void ToolStripButton_Add_Click(object sender, EventArgs e)
        {
            this.操作提示.Text = "";
            customersDtoBindingSource.EndEdit();
            DataGridView1.EndEdit();
            // 1. 检查主列表是否存在且非空
            if (allCustomers != null && allCustomers.Any())
            {
                // 2. 检查主列表的第一个实体（当前页的第一条记录）的 ID 
                // 假设 ID <= 0 表示这是尚未保存的新记录
                if ( string.IsNullOrEmpty(allCustomers[0].客户代码))
                {
                    this.操作提示.Text = "列表中已存在一个待新增的空行,请先完成编辑或取消操作.";
                    DataGridView1.CurrentCell = DataGridView1.Rows[0].Cells[4];
                    DataGridView1.BeginEdit(true);
                    return;
                }
            }
            // 确保主列表初始化
            if (allCustomers == null)
            {
                allCustomers = new List<CustomersDto>();
            }

            // 1. 创建新的空 DTO 并设置视图状态为新增
            var newCustomer = new CustomersDto
            {
                视图状态 = RowState.新增 // 关键：标记为新增状态
            };
            // 2. 在主列表（内存数据源）的索引 0 处插入新实体
            //此时 DataGridView/BindingSource 尚未感知到变化！
            allCustomers.Insert(0, newCustomer);

            // 3. 更新分页控件状态
            dataSplitUserControl1.TotalRecords = allCustomers.Count;
            dataSplitUserControl1.PageIndex = 0; // 强制切换到第一页，确保新记录显示
            dataSplitUserControl1.改变控件显示状态(); // 假设这个方法会更新分页按钮状态等

            // 4. 刷新 DataGridView，使其显示包含新记录的第一页数据
            // 刷新 DataGridView
            await 刷新DataGridAsync();
            await SaveBtn状态刷新Async();

            // 将焦点设置到第一行的第一个可编辑单元格，方便用户立即开始输入
            DataGridView1.CurrentCell = DataGridView1.Rows[0].Cells[4];
            DataGridView1.BeginEdit(true);


        }
        // 保存
        private async void ToolStripButton_Save_Click(object sender, EventArgs e)
        {
            this.操作提示.Text = "";
            // 1. 强制结束任何正在进行的编辑/新增操作
            // 这一步非常重要，确保用户在 DataGridView 中输入的值已同步到 DTO 实例中
            customersDtoBindingSource.EndEdit();

            // 假设 DataGridView 的 DataSource 属性直接或间接绑定了当前页的 CustomersDto 列表
            var currentPageSource = customersDtoBindingSource.List;

            if (!(currentPageSource is IList<CustomersDto> currentPageCustomers))
            {
                MessageBox.Show("无法获取当前页面的有效数据源，保存失败。", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // 初始化 Service 批量操作需要的参数列表
            List<Customers> createList = new List<Customers>();
            List<Customers> updateList = new List<Customers>();
            List<Customers> deleteList = new List<Customers>();
            List<int> oldIdList=new List<int>();
            foreach (var customerDto in currentPageCustomers)
            {
                if (customerDto == null) continue;
                switch (customerDto.视图状态)
                {
                    case RowState.新增:
                        createList.Add(_mapper.Map<Customers>(customerDto));
                        break;
                    case RowState.修改:
                        // 确保更新操作有 ID，Service 层会再次校验
                        if (customerDto.ID <= 0)
                        {
                            MessageBox.Show("发现待更新记录缺少有效 ID，请检查数据完整性。", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            return;
                        }
                        updateList.Add(_mapper.Map<Customers>(customerDto));
                        break;
                    case RowState.删除:
                        // 确保删除操作有 ID，Service 层会再次校验
                        if (customerDto.ID <= 0)
                        {
                            MessageBox.Show("发现待删除记录缺少有效 ID，请检查数据完整性。", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            return;
                        }
                        deleteList.Add(_mapper.Map<Customers>(customerDto));
                        break;
                    case RowState.无变化:
                        oldIdList.Add(customerDto.ID);
                    break;
                }
            }

            if (!createList.Any() && !updateList.Any() && !deleteList.Any())
            {
                MessageBox.Show("当前页面没有检测到任何需要保存的更改。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }


            try
            {
                // 1. 调用 Service 执行批量事务操作
                var rst = await _customersService.批量操作客户Async(createList, updateList, deleteList);
                if(rst.Success)
                {
                    MessageBox.Show($"保存成功！共新增 {rst.Data.AddIIds.Count} 条记录,修改 {rst.Data.UpdateIIds.Count} 条记录,共",
                                "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);

                    var iDList = rst.Data.GetAllAffectedIDs();
                    oldIdList.AddRange(iDList);
                    var rst2 = await _customersService.GetByIdsAsync(oldIdList);
                    allCustomers = _mapper.Map<List<CustomersDto>>(rst2.Data);
                    filteredCustomers = allCustomers;
                    // 重新调用刷新DataGridAsync，让它使用恢复后的 PageIndex 重新绑定数据
                    await 刷新DataGridAsync();
                }
                else
                {
                    MessageBox.Show($"保存失败！{rst.Message}",
                                "失败", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }

            }
            catch (Exception ex)
            {
                // Service 层的回滚逻辑会处理数据库事务失败
                MessageBox.Show($"保存失败。原因: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        // 删除
        private async void ToolStripButton_Delete_Click(object sender, EventArgs e)
        {
            this.操作提示.Text = "";
            //获取选中的行
            var selectedRows = this.DataGridView1.SelectedRows;
            if (selectedRows.Count == 0)
            {
                MessageBox.Show("请选择要删除的记录。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            //获取选中行对应的 DTO
            if (DataGridView1.CurrentRow.DataBoundItem is CustomersDto customersDto)
            {
                //弹窗用户确认是否执行删除
                if (MessageBox.Show($"确定要删除{customersDto.客户代码}吗？", "提示", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                {
                    return;
                }
                var rst= await _customersService.DeleteAsync(customersDto.ID);
                if (rst.Success)
                {
                    MessageBox.Show("删除成功！", "成功", MessageBoxButtons.OK, MessageBoxIcon.Information); 
                    allCustomers.Remove(customersDto);
                    await 刷新DataGridAsync();
                }
                else
                { MessageBox.Show($"删除失败！{rst.Message}", "失败", MessageBoxButtons.OK, MessageBoxIcon.Error);}
            }

        }
        // 刷新数据
        private async void ToolStripButton_Refresh_Click(object sender, EventArgs e)
        {
            this.操作提示.Text = "";
            await 刷新DataGridAsync();
        }
        //查询客户
        private async void ToolStripButton_Query_Click(object sender, EventArgs e)
        {
            this.操作提示.Text = "";
            await 查询客户Async(this.ToolStripComboBox_QueryField.Text, this.ToolStripTextBox_QueryResult.Text);
        }
        //筛选总的结果
        private async void ToolStripTextBox_FindResult_TextChanged(object sender, EventArgs e)
        {
            string filterText = this.ToolStripTextBox_FindResult.Text;
            // 取消上一次的筛选任务
            _filterCts?.Cancel();
            _filterCts = new System.Threading.CancellationTokenSource();
            var token = _filterCts.Token;

            try
            {
                // 防抖：延迟 300ms，如果期间有新的输入则取消
                await Task.Delay(300, token);

                // 如果 allCustomers 为空，直接返回
                if (allCustomers == null || allCustomers.Count == 0)
                    return;

                // 执行筛选
                if (string.IsNullOrWhiteSpace(filterText))
                {
                    filteredCustomers = allCustomers;
                }
                else
                {
                    filteredCustomers = allCustomers
                        .Where(c =>
                        {        // 拼接三个字段作为源文本
                            string source = string.Concat(c.客户代码, " ", c.客户名称, " ", c.联系人);
                            return 通用搜索工具.空格模糊匹配(source, filterText);
                        }
                        )
                        .ToList();
                }

                // 更新分页控件
                dataSplitUserControl1.TotalRecords = filteredCustomers.Count;
                dataSplitUserControl1.PageIndex = 0;
                dataSplitUserControl1.改变控件显示状态();

                // 刷新显示第一页
                await 刷新DataGridAsync();
            }
            catch (TaskCanceledException)
            {
                // 被取消，忽略
            }
        }

        // DataGridView 绑定完成 数据绑定完成后，更新删除按钮状态
        private void DataGridView1_DataBindingComplete(object sender, DataGridViewBindingCompleteEventArgs e)
        {
            if (this.DataGridView1.Rows.Count > 0)
            {
                this.ToolStripButton_Delete.Enabled = true;
            }
            else
            {
                this.ToolStripButton_Delete.Enabled = false;
            }
        }

        //刷新保存按钮状态
        private async Task SaveBtn状态刷新Async()
        {
            if (this.DataGridView1.Rows.Count > 0)
            {
                foreach (DataGridViewRow row in this.DataGridView1.Rows)
                {
                    if (row.DataBoundItem is CustomersDto dto)
                    {
                        if (dto.视图状态 == RowState.新增 || dto.视图状态 == RowState.修改 || dto.视图状态 == RowState.删除)
                        {
                            this.ToolStripButton_Save.Enabled = true;
                            return;
                        }
                    }
                }
            }
            else
            {
                this.ToolStripButton_Save.Enabled = false;
            }
        }
        private async void DataGridView1_CellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            this.操作提示.Text = "";
            await SaveBtn状态刷新Async();
            
        }       
        private async void DataGridView1_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
           await SaveBtn状态刷新Async();
           
        }

        private void DataGridView1_CellBeginEdit(object sender, DataGridViewCellCancelEventArgs e)
        {
            //string hh = "1";
        }
    }
}
