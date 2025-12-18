using AutoMapper;
using SwiftExport.AppLayer.DTOs;
using SwiftExport.AppLayer.Interfaces;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities;
using SwiftExport.UiKongJianFrameWork.AppUserControls;
using SwiftExport.UiKongJianFrameWork.FrmUtility;
using SwiftExport.UiKongJianFrameWork.FrmUtility.UIStyle;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SwiftExport.UiKongJianFrameWork.AppForms
{
    public partial class FrmSuppliersManager : Form
    {
        private readonly ISuppliersService _supServer;
        private readonly IMapper _mapper;
        private BindingList<SuppliersDto> allData;
        public FrmSuppliersManager(ISuppliersService supplierService,IMapper mapper)
        {
            InitializeComponent();
            _supServer = supplierService;
            _mapper = mapper;

            Dgv1.EnableDoubleBuffered()
                .ApplyModernStyle()
                .AddLineNumberColumn()
                .EnableAutoTrim()
                .SetNotNullColumnsColumns("供应商代码DataGridViewTextBoxColumn1")
                .SetUniqueColumns("供应商代码DataGridViewTextBoxColumn1")
                .EnableDeleteKey()
                ;
        }
        
        public async Task LoadDsv()
        {
                var rst = await _supServer.GetAllAsync();
                if (rst.Success)
                {
                    allData=new BindingList<SuppliersDto>(_mapper.Map<List<SuppliersDto>>(rst.Data));
                    BdSourceSuppliers.DataSource = allData;
                }
        }

        private async void AddSupplier(object sender, EventArgs e)
        {

        }


        private void BdSourceSuppliers_CurrentChanged(object sender, EventArgs e)
        {
            
        }

        private async void Btn_Add_Click(object sender, EventArgs e)
        {
            this.操作提示.Text = "";
            BdSourceSuppliers.EndEdit();
            Dgv1.EndEdit();

            if (allData == null)
               await LoadDsv();

            // 1. 检查主列表是否存在且非空
            if (BdSourceSuppliers == null)
            {
                var newSp = new SuppliersDto
                {
                    ID=-1,
                    视图状态 = RowState.新增 
                };
                allData.Insert (0,newSp);
            }
            else
            {
                if (BdSourceSuppliers.Count > 0)
                {
                    var cell = Dgv1.Rows[0].Cells[5];
                    string cellValue = cell?.Value?.ToString();
                    if (string.IsNullOrEmpty(cellValue))
                    {
                        Dgv1.CurrentCell = cell;
                        Dgv1.BeginEdit(true);
                        return;
                    }
                }
                var newSp2 = new SuppliersDto
                {
                    ID = -1,
                    视图状态 = RowState.新增
                };
                allData.Insert(0,newSp2);
            }


            //// 3. 更新分页控件状态
            //dataSplitUserControl1.TotalRecords = allCustomers.Count;
            //dataSplitUserControl1.PageIndex = 0; // 强制切换到第一页，确保新记录显示
            //dataSplitUserControl1.改变控件显示状态(); // 假设这个方法会更新分页按钮状态等

            //// 4. 刷新 DataGridView，使其显示包含新记录的第一页数据
            //// 刷新 DataGridView
            //await 刷新DataGridAsync();
            //await SaveBtn状态刷新Async();

            // 将焦点设置到第一行的第一个可编辑单元格，方便用户立即开始输入
            Dgv1.CurrentCell = Dgv1.Rows[0].Cells[5];
            Dgv1.BeginEdit(true);
        }

        private async void Btn_Refresh_Click(object sender, EventArgs e)
        {
            await LoadDsv();
        }
    }
}
