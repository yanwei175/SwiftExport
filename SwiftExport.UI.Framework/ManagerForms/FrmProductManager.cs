using AutoMapper;
using SwiftExport.AppLayer.Interfaces;
using SwiftExport.Core.Entities.OtherEntities;
using SwiftExport.UI.Framework.Common;
using SwiftExport.Utility;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SwiftExport.UI.Framework.ManagerForms
{
    public partial class FrmProductManager : Form
    {
        private readonly IProductService _service;
        private readonly IMapper _mapper;
        // 内存缓存
        private List<ProductSearchMeta> _searchCache;
        // 状态变量
        //private string _currentCategory = "ALL"; // 当前选中的分类
        //private string _currentSearchKey = "";   // 当前生效的搜索词

        // 防抖计时器 (用于联想)
        private readonly Timer _filterTimer = new Timer { Interval = 350 };



        public FrmProductManager(IProductService service, IMapper mapper)
        {
            InitializeComponent();
            _service = service;
            _mapper = mapper;
            dataSplitUserControl1.PageChanged += async (s, e) => await LoadDataAsync();
            TsBtn_Add.Image = IconProvider.GetToolBarIcons("Add", ".png");
        }

        //private async void FrmProductManager_Load(object sender, EventArgs e)
        //{
        //    // 1. 一次性拉取 1万条数据到内存
        //    _searchCache = await Task.Run(() => _service.GetAllSearchMetaAsync());
        //}
        private async void Set_searchCache()
        {
            _searchCache = await Task.Run(() => _service.GetAllSearchMetaAsync());
        }

        // 刷新 DGV
        private async Task LoadDataAsync()
        {
            //// 1. 从控件读取当前要查哪一段
            //int skip = dataSplitUserControl1.PageIndex * dataSplitUserControl1.PageSize;
            //int take = dataSplitUserControl1.PageSize;

            // 2. 调 Service 查库
            //var result = await _service.GetPagedListAsync(_searchKey, _category, skip, take);

            //if (result.Success)
            //{
            //    // 3. 绑定 Grid
            //    DgvProduct.DataSource = result.Data.Items;

            //    // 4. 反向更新分页控件的状态 (让控件自己去更新按钮和页码)
            //    dataSplitUserControl1.UpdateState(result.Data.TotalCount);
            //}
        }


        // 1. 输入联想 (防抖触发)
        private async void FilterTimer_Tick(object sender, EventArgs e)
        {
            //_filterTimer.Stop();
            //string text = Txt_FindResult.Text.Trim();

            //// 只有在输入且光标在搜索框内时才提示
            //if (text.Length >= 2 && Txt_FindResult.Focused)
            //{
            //    var suggestions = await _productService.GetSearchSuggestionsAsync(text);

            //    if (suggestions.Count > 0)
            //    {
            //        // 记住当前光标位置，防止 Items.Clear() 导致光标跳到最前
            //        int selectionStart = Txt_FindResult.SelectionStart;

            //        Txt_FindResult.Items.Clear();
            //        Txt_FindResult.Items.AddRange(suggestions.ToArray());

            //        // 展开下拉框
            //        Txt_FindResult.DroppedDown = true;

            //        // 恢复光标
            //        Txt_FindResult.SelectionStart = selectionStart;
            //    }
            //}
        }

        // 2. 回车执行搜索 (真正的刷新 DGV)
        private async void Txt_FindResult_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                Txt_FindResult.DroppedDown = false; // 关掉提示

                // 这里的逻辑：一旦手动搜索，就重置到第一页
                dataSplitUserControl1.UpdateState(0);
                await ExecuteQueryAsync();
            }
        }


        // ... 引用命名空间 ...

        private async Task ExecuteQueryAsync()
        {
            //// 1. 组装查询 DTO
            //var input = new ProductQueryInput
            //{
            //    // 映射分页控件 (0-based) 到 Core Pagination (1-based)
            //    Pager = new Pagination
            //    {
            //        Page = dataSplitUserControl1.PageIndex + 1,
            //        PageSize = dataSplitUserControl1.PageSize
            //    },
            //    GlobalSearchKey = _currentSearchKey
            //};

            //// 2. 映射左侧分类筛选
            //if (_currentCategory != "ALL" && !string.IsNullOrEmpty(_currentCategory))
            //{
            //    input.FilterConditions.Add("ProductClass", new QueryCondition
            //    {
            //        操作符 = 查询操作符.等于,
            //        值1 = _currentCategory
            //    });
            //}

            //// 3. 调用 Service
            //var result = await Task.Run(() => _service.GetPagedListAsync(input));

            //if (result.Success)
            //{
            //    // 4. 在 UI 层进行 Entity -> DTO 转换
            //    var dtos = _mapper.Map<List<ProductDto>>(result.Data.Items);
            //    DgvProduct.DataSource = new BindingList<ProductDto>(dtos);

            //    // 5. 更新分页控件状态
            //    dataSplitUserControl1.UpdateState(result.Data.TotalCount);
            //}
        }





        private void VisibleScPane2_Click(object sender, EventArgs e)
        {
            SplitContainer_Product.Panel2Collapsed = !SplitContainer_Product.Panel2Collapsed;
            VisibleScPane2.Text = SplitContainer_Product.Panel2Collapsed ? "▲ 显示BOM区" : "▼ 隐藏BOM区";
        }

        private async void TsBtn_Refresh_Click(object sender, EventArgs e)
        {
            if (_searchCache == null)
            {
                _searchCache = await Task.Run(() => _service.GetAllSearchMetaAsync());
            }

            Txt_FindResult.Items.Clear();

        }

        private void Txt_FindResult_TextChanged(object sender, EventArgs e)
        {
            // 1. 防抖检查：如果是代码自动赋值（比如选中项后），则不触发搜索
            if (!Txt_FindResult.Focused) return;

            string key = Txt_FindResult.Text.Trim().ToUpper();

            // 2. 记住当前光标位置
            int selectionStart = Txt_FindResult.SelectionStart;

            if (string.IsNullOrEmpty(key))
            {
                Txt_FindResult.Items.Clear();
                Txt_FindResult.DroppedDown = false;
                return;
            }

            // 3. 内存筛选
            var suggestions = _searchCache.Where(x =>
            {
                // 调用你的“空格模糊匹配”方法
                return 通用搜索工具.空格模糊匹配(x.SearchText.ToUpper(), key);

            }).ToArray();




            // 4. 更新 UI
            Txt_FindResult.BeginUpdate(); // 关键：停止重绘，减少闪烁

            Txt_FindResult.Items.Clear();
            if (suggestions.Any())
            {
                Txt_FindResult.Items.AddRange(suggestions);

                // 只有在没展开时才强制展开，避免打字时闪烁
                if (!Txt_FindResult.DroppedDown)
                    Txt_FindResult.DroppedDown = true;
            }
            else
            {
                Txt_FindResult.DroppedDown = false;
            }

            // 5. 还原光标位置
            // 重新填充 Items 会导致 Text 属性被重置或全选，这里强制恢复位置
            Txt_FindResult.SelectionStart = selectionStart;
            Txt_FindResult.SelectionLength = 0; // 确保没有文本被选中

            Txt_FindResult.EndUpdate();
        }





    }

}
