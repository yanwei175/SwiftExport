using SwiftExport.AppLayer.DTOs;
using SwiftExport.AppLayer.Interfaces;
using SwiftExport.Core.Entities;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SwiftExport.UiKongJianFrameWork
{
    public partial class ProductUserControl : UserControl
    {
        private  BindingSource _bindingSource = new BindingSource();
        private readonly IProductService _productService;

        private bool _isDataLoaded = false;
        private System.Windows.Forms.Timer _debounceTimer;
        private const int DEBOUNCE_DELAY = 300; // 300毫秒防抖
        private List<Product> _allProducts;       // 全部数据
        private List<Product> _filterProducts;    // 当前页数据
        private int _pageSize = 20;
        private int _currentPage = 1;

        public ProductUserControl(IProductService productService)
        {
            _productService = productService;
            InitializeComponent();
            SetupDataGridView();
            // 初始化防抖定时器
            _debounceTimer = new System.Windows.Forms.Timer
            {
                Interval = DEBOUNCE_DELAY
            };
            _debounceTimer.Tick += async (s, e) => await DebounceTimer_TickAsync();

            // 控件显示时加载数据
            this.VisibleChanged += async (sender, e) =>
            {
                if (this.Visible && !_isDataLoaded && !DesignMode)
                {
                    await LoadDataAsync();
                }
            };

        }
        //设置datagridviewUI
        private void SetupDataGridView()
        {
            
            dataGridView1.AutoGenerateColumns = false;
            dataGridView1.DataSource = _bindingSource;

            // 添加列
            dataGridView1.Columns.Add(new DataGridViewTextBoxColumn
            {
                DataPropertyName = "SKU",
                HeaderText = "SKU",
                Width = 200
            });

            dataGridView1.Columns.Add(new DataGridViewTextBoxColumn
            {
                DataPropertyName = "Description",
                HeaderText = "描述",
                Width = 400
            });

            dataGridView1.Columns.Add(new DataGridViewTextBoxColumn
            {
                DataPropertyName = "Finish",
                HeaderText = "表面处理",
                Width = 400
            });

            // 更多列...
        }

        // 从数据库加载数据
        private async Task LoadDataAsync()
        {
            if (_isDataLoaded || DesignMode) return;

            try
            {
                SetLoading(true);

                // 异步加载数据
                _allProducts = (await _productService.GetAll()).ToList();
                _filterProducts = _allProducts;
                TotalRecords = _filterProducts.Count;
                List<string> 客户代码列表= _allProducts.Select(p => p.CustomerCode).Distinct().ToList();
                Cmb_CustomerCode.Items.Clear();
                Cmb_CustomerCode.Items.AddRange(客户代码列表.ToArray());
                // 初始化数据绑定
                RefreshBdDataSource();

                _isDataLoaded = true;
            }
            catch (Exception ex)
            {
                // 错误处理
                MessageBox.Show($"加载数据失败{ex.Message}");
            }
            finally
            {
                SetLoading(false);
            }
        }

        private void SetLoading(bool isLoading)
        {
            this.Cursor = isLoading ? Cursors.WaitCursor : Cursors.Default;
            // 可以添加进度条等
        }

        //刷新绑定数据源
        private void RefreshBdDataSource()
        {
            _pageSize = int.TryParse(this.CbmEachItemPage.Text, out int size)
                            ? size
                            : 20; // 使用默认值

            var pageData = _filterProducts
                .Skip((_currentPage - 1) * _pageSize)
                .Take(_pageSize)
                .ToList();

            _bindingSource.DataSource = pageData;
            TotalRecords = _filterProducts.Count;

            // 更新分页信息
            UpdatePagerInfo();
        }
        //更新控件状态
        private void UpdatePagerInfo()
        {

            LblDatCount.Text = $"显示 {_bindingSource.Count} 条，共 {TotalRecords} 条{Environment.NewLine}第 {_currentPage} 页 / 共 {TotalPages} 页";

            // 更新按钮状态
            BtnFirst.Enabled = _currentPage > 1;
            BtnPrev.Enabled = _currentPage > 1;
            BtnNext.Enabled = _currentPage < TotalPages;
            BtnLast.Enabled = _currentPage < TotalPages;

            // 更新页码下拉框
            cmbPage.Items.Clear();
            for (int i = 1; i <= TotalPages; i++)
            {
                cmbPage.Items.Add(i);
            }
            cmbPage.SelectedItem = _currentPage;
        }
        //分页属性
        public int TotalPages => (int)Math.Ceiling(TotalRecords / (double)_pageSize);
        public int TotalRecords { get; private set; }
        
        // 分页按钮事件
        private void BtnFirst_Click(object sender, EventArgs e) => GoToPage(1);
        private void BtnPrev_Click(object sender, EventArgs e) => GoToPage(_currentPage - 1);
        private void BtnNext_Click(object sender, EventArgs e) => GoToPage(_currentPage + 1);
        private void BtnLast_Click(object sender, EventArgs e) => GoToPage(TotalPages);
        private void GoToPage(int page)
        {
            if (page < 1) page = 1;
            if (page > TotalPages) page = TotalPages;

            _currentPage = page;
            RefreshBdDataSource();
        }


        //搜索,刷新filterdata
        private void Txtbox_搜索结果_TextChanged(object sender, EventArgs e)
        {
            // 重置定时器
            _debounceTimer.Stop();
            _debounceTimer.Start();
        }

        //每页显示数量改变
        private void CbmEachItemPage_SelectedIndexChanged(object sender, EventArgs e)
        {
            RefreshBdDataSource();
        }

        // 异步搜索方法
        private async Task DebounceTimer_TickAsync()
        {
            _debounceTimer.Stop();

            string searchText = this.Txtbox_搜索结果.Text;

            // 如果搜索框为空，立即清除结果
            if (string.IsNullOrWhiteSpace(searchText))
            {
                _currentPage = 1;
                _filterProducts = _allProducts.ToList();

                // 必须在UI线程更新
                this.Invoke(new Action(() =>
                {
                    RefreshBdDataSource();
                }));
                return;
            }

            try
            {
                // 显示加载状态
                this.Invoke(new Action(() => this.Cursor = Cursors.WaitCursor));

                List<Product> filteredProducts = null;

                // 异步执行复杂的正则搜索
                filteredProducts = await Task.Run(() =>
                {
                    return FilterProductsAsync(searchText);
                });

                // 回到UI线程更新
                this.Invoke(new Action(() =>
                {
                    _currentPage = 1;
                    _filterProducts = filteredProducts;
                    RefreshBdDataSource();
                }));
            }
            catch (Exception ex)
            {
                // 异常处理
                this.Invoke(new Action(() =>
                {
                    MessageBox.Show($"搜索出错: {ex.Message}", "错误",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }));
            }
            finally
            {
                // 恢复光标
                this.Invoke(new Action(() => this.Cursor = Cursors.Default));
            }
        }

        // 异步过滤方法（从原RefreshFilterProducts改造）
        private List<Product> FilterProductsAsync(string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword))
                return _allProducts.ToList();

            // 1. 预处理关键字
            keyword = keyword.Trim();

            // 2. 空格替换为 *（根据你的需求）
            keyword = keyword.Replace(" ", "*");

            // 3. 支持多个连续 * 优化为单个 *
            keyword = Regex.Replace(keyword, @"\*+", "*");

            // 4. 转换为正则表达式
            string regexPattern = ConvertWildcardToRegex(keyword);

            // 5. 编译正则表达式
            var regex = new Regex(regexPattern,
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

            // 6. 执行查询 - 这里是在后台线程执行的
            return _allProducts.Where(p =>
            {
                // 安全拼接所有搜索字段
                string searchText = string.Concat(
                    p.SKU, p.CSKU, p.SSKU, p.Description, p.Finish);
                return regex.IsMatch(searchText);
            }).ToList();
        }
        //搜索匹配
        private static string ConvertWildcardToRegex(string wildcard)
        {
            return Regex.Escape(wildcard).Replace("\\*", ".*");
            //// 完整的通配符转换
            //return "^" + Regex.Escape(wildcard)
            //    .Replace("\\*", ".*")      // * -> .*
            //    .Replace("\\?", ".")       // ? -> .
            //    + "$";
        }

        // 原方法保留，但改为调用异步版本（可选）
        private void RefreshFilterProducts(string keyword)
        {
            // 这个方法现在只同步调用异步版本（简单处理）
            // 或者在不需要异步时使用
            _filterProducts = FilterProductsAsync(keyword);
        }

    }
}
