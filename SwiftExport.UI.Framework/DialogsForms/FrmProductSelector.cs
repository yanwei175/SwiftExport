using AutoMapper;
using SwiftExport.AppLayer.DTOs;
using SwiftExport.AppLayer.Interfaces;
using SwiftExport.Core.Common;
using SwiftExport.UI.Framework.Common;
using SwiftExport.UI.Framework.FrmUtility.UIStyle;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SwiftExport.UI.Framework.DialogsForms
{
    public partial class FrmProductSelector : Form
    {
        private readonly IProductService _productService;
        private readonly IMapper _mapper;

        // ==========================================
        // 左侧 (虚拟模式) 数据源
        // ==========================================
        private readonly List<ProductDto> _virtualSource = new List<ProductDto>();

        // ==========================================
        // 右侧 (普通模式) 数据源
        // ==========================================
        private readonly BindingList<ProductDto> _selectedList = new BindingList<ProductDto>();

        // 图片缓存 & 资源
        private static readonly Dictionary<string, Image> _imageCache = new Dictionary<string, Image>();
        private readonly Image _loadingImage;
        private readonly Image _errorImage;

        // 分页 & 状态
        private int _currentPage = 1;
        private readonly int _pageSize = 50; // 虚拟模式下可以适当增大每页数量
        private bool _hasMoreData = true;
        private bool _isLoading = false;
        private CancellationTokenSource _searchCts;

        // 对外公开属性
        public List<ProductDto> SelectedProducts => _selectedList.ToList();

        public FrmProductSelector(IProductService productService, IMapper mapper)
        {
            InitializeComponent();
            _productService = productService;
            _mapper = mapper;

            // 初始化图片资源 (建议换成实际的资源图标)
            _loadingImage = IconProvider.GetStatusIcons("Loading", "bmp");
            _errorImage = IconProvider.GetStatusIcons("Error", "bmp");
            //using (var g = Graphics.FromImage(_loadingImage)) { g.Clear(Color.WhiteSmoke); g.DrawString("Loading...", this.Font, Brushes.Gray, 0, 15); }
            //_errorImage = new Bitmap(50, 50);
            //using (var g = Graphics.FromImage(_errorImage)) { g.Clear(Color.MistyRose); g.DrawString("Error", this.Font, Brushes.Red, 0, 15); }
            //样式美化
            Dgv.EnableDoubleBuffered().ApplyModernStyle();
            SelectedDgv.EnableDoubleBuffered().ApplyModernStyle();

            // === 1. 设置左侧 DGV 为虚拟模式 ===
            Dgv.VirtualMode = true;
            Dgv.AllowUserToAddRows = false;
            Dgv.AllowUserToDeleteRows = false;
            Dgv.ReadOnly = true;
            Dgv.RowTemplate.Height = 120;
            Dgv.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            // 绑定虚拟数据提供事件
            Dgv.CellValueNeeded += Dgv_CellValueNeeded;
            // 滚动到底部加载更多
            Dgv.Scroll += Dgv_Scroll;

            // === 2. 设置右侧 DGV (普通模式) ===
            SelectedDgv.AutoGenerateColumns = false;
            SelectedDgv.RowTemplate.Height = 60;
            SelectedDgv.DataSource = _selectedList;
            SelectedDgv.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            // 右侧也需要处理图片显示(非虚拟模式依然推荐用 CellFormatting)
            SelectedDgv.CellFormatting += SelectedDgv_CellFormatting;

            // 事件绑定
            TxtSearch.TextChanged += TxtSearch_TextChanged;

            // =======================================================
            // ★ 补全 1: 绑定下拉框事件
            // =======================================================
            // 当选择改变时，直接触发搜索 (重置页码=true)
            // 使用 lambda 包装一下异步方法
            CmbLimitCustomer.SelectedIndexChanged += async (s, e) =>
            {
                // 防止加载数据源时触发事件导致查两次
                if (CmbLimitCustomer.Focused)
                {
                    await SearchDataAsync(resetPage: true);
                }
            };
            // 增加搜索便利性：支持输入自动补全
            CmbLimitCustomer.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
            CmbLimitCustomer.AutoCompleteSource = AutoCompleteSource.ListItems;
            // =======================================================
            // 补全 2: 窗体加载时获取客户列表
            // =======================================================
            this.Load += async (s, e) => await LoadCustomerDataAsync();

            // 绑定双击事件
            Dgv.CellDoubleClick += Dgv_CellDoubleClick;

            // 右侧已选列表也加上双击移除
            SelectedDgv.CellDoubleClick += SelectedDgv_CellDoubleClick;

            // 开启 ToolTip 显示
            Dgv.ShowCellToolTips = true;

            // 按钮逻辑
            Btn_ShiftSelect.Click += (s, e) => AddToSelected(GetSelectedFromVirtual());
            Btn_RemoveSelect.Click += (s, e) => RemoveFromSelected(GetSelectedFromBinding(SelectedDgv));
            Btn_ShiftAll.Click += (s, e) => AddToSelected(_virtualSource); // 全部添加
            Btn_RemoveAll.Click += (s, e) => _selectedList.Clear();

            BtnOK.Click += (s, e) => { this.DialogResult = DialogResult.OK; this.Close(); };
            BtnCancel.Click += (s, e) => { this.DialogResult = DialogResult.Cancel; this.Close(); };

            //把所有列设为“程序控制排序”
            foreach (DataGridViewColumn col in Dgv.Columns)
            {
                // 只有绑定了属性的列才允许排序，图片列跳过
                if (!string.IsNullOrEmpty(col.DataPropertyName))
                {
                    col.SortMode = DataGridViewColumnSortMode.Programmatic;
                }
            }

        }
        private async Task LoadCustomerDataAsync()
        {
            try
            {
                var customers = await _productService.GetAllCustomerCodeListAsync();

                // 在最前面插入一个空项，代表"全部/不限"
                customers.Insert(0, "");

                // 绑定数据源
                // 注意：赋值 DataSource 会触发 SelectedIndexChanged，
                // 所以我们在事件里加了 .Focused 判断，或者是暂时解绑事件再赋值
                CmbLimitCustomer.DataSource = customers;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载客户列表失败: {ex.Message}");
            }
        }

        #region Virtual Mode 核心逻辑 (左侧列表)

        /// <summary>
        /// 虚拟模式的数据供给站：DGV 需要画任何一个单元格时，都会问这个方法要数据
        /// </summary>
        private void Dgv_CellValueNeeded(object sender, DataGridViewCellValueEventArgs e)
        {
            // 1. 越界保护
            if (e.RowIndex >= _virtualSource.Count || e.RowIndex < 0) return;

            // 2. 获取当前行的 DTO
            var dto = _virtualSource[e.RowIndex];

            // 3. 获取当前列的信息
            var col = Dgv.Columns[e.ColumnIndex];

            // ============================================================
            // 情况 A: 处理没有绑定 DataPropertyName 的【图片列】
            // ============================================================
            if (col.Name == "图片") // 这里的 "图片" 必须是你 Designer 里该列的 (Name) 属性
            {
                // 传入 DTO 里的【主图】路径，去加载图片
                e.Value = GetImageForVirtualCell(dto.主图, e.RowIndex, e.ColumnIndex);
                return;
            }

            // ============================================================
            // 情况 B: 处理绑定了 DataPropertyName 的【文本列】(包括隐藏的路径列)
            // ============================================================
            // 这里的 switch case 全部对应 DTO 的中文属性名
            switch (col.DataPropertyName)
            {
                case nameof(ProductDto.SKU):
                    e.Value = dto.SKU;
                    break;

                case nameof(ProductDto.描述):
                    e.Value = dto.描述;
                    break;

                case nameof(ProductDto.颜色):
                    e.Value = dto.颜色;
                    break;

                case nameof(ProductDto.主图):
                    // 这是那个隐藏列，它需要字符串路径
                    // 虽然它不可见，但有时候代码里可能会去读取它，所以还是要给它赋值
                    e.Value = dto.主图;
                    break;

                // ... 其他列 ...
                case nameof(ProductDto.客户代码): e.Value = dto.客户代码; break;
                case nameof(ProductDto.客户SKU): e.Value = dto.客户SKU; break;
                case nameof(ProductDto.配置): e.Value = dto.配置; break;
            }
        }

        /// <summary>
        /// 专门处理虚拟模式下的图片获取
        /// </summary>
        private Image GetImageForVirtualCell(string imagePath, int rowIndex, int colIndex)
        {
            if (string.IsNullOrWhiteSpace(imagePath)) return _errorImage;

            // 1. 命中缓存直接返回
            // 读取时加锁，避免与写入冲突导致未命中或异常
            lock (_imageCache)
            {
                if (_imageCache.TryGetValue(imagePath, out Image img))
                {
                    return img;
                }
            }

            // 2. 未命中，触发异步加载，并暂时返回 Loading 图
            // 注意：LoadImageAsync 内部会去重，不会重复触发
            LoadImageAsync(imagePath, Dgv, rowIndex, colIndex);

            return _loadingImage;
        }

        #endregion

        #region Binding Mode 图片逻辑 (右侧列表)

        private void SelectedDgv_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            // 右侧列表还是普通模式，用 CellFormatting 处理图片
            if (SelectedDgv.Columns[e.ColumnIndex].Name == "已选图片")
            {
                if (SelectedDgv.Rows[e.RowIndex].DataBoundItem is ProductDto dto)
                {

                    e.Value = GetImageForVirtualCell(dto.主图, e.RowIndex, e.ColumnIndex);

                }
            }
        }

        #endregion

        #region 图片异步加载器 (通用)

        // 记录正在加载的任务，防止同一个URL重复发起 Task
        private static readonly HashSet<string> _loadingTasks = new HashSet<string>();

        private async void LoadImageAsync(string path, DataGridView dgv, int rowIndex, int colIndex)
        {
            // 简单锁机制，防止滚动时对同一张图发起几十次 IO 请求
            lock (_loadingTasks)
            {
                if (_loadingTasks.Contains(path)) return;
                _loadingTasks.Add(path);
            }

            await Task.Run(() =>
            {
                try
                {
                    if (File.Exists(path))
                    {
                        using (var original = Image.FromFile(path))
                        {
                            // 缩略图处理：400x400
                            var thumb = new Bitmap(original, new Size(400, 400));
                            lock (_imageCache)
                            {
                                if (!_imageCache.ContainsKey(path)) _imageCache[path] = thumb;
                                else
                                {
                                    //// 如果已有旧图，替换并释放旧资源（谨慎处理）
                                    //var old = _imageCache[path];
                                    //_imageCache[path] = thumb;
                                    //try { old.Dispose(); } catch { }
                                }
                            }
                        }
                    }
                    else
                    {
                        lock (_imageCache)
                        {
                            if (!_imageCache.ContainsKey(path)) _imageCache[path] = _errorImage;
                        }
                    }
                }
                catch
                {
                    lock (_imageCache)
                    {
                        if (!_imageCache.ContainsKey(path)) _imageCache[path] = _errorImage;
                    }
                }
                finally
                {
                    lock (_loadingTasks)
                    {
                        _loadingTasks.Remove(path);
                    }
                }
            });

            // 1. 检查控件是否还活着
            if (dgv.IsDisposed || dgv.Disposing) return;


            ThreadHelper.UIInvoke(() =>
            {

                // 3. 再次检查行索引有效性 (因为加载图片期间，用户可能已经重新搜索，导致行数变了)
                if (rowIndex >= 0 && rowIndex < dgv.RowCount)
                {
                    // 4. ★ 这一句是核心 ★
                    // 它的作用是：标记这个单元格为"脏"，强制 DGV 立即调用 CellValueNeeded 重新获取值
                    // 这时 CellValueNeeded 再去查 _imageCache，就能查到图了，而不是返回 Loading
                    dgv.InvalidateCell(colIndex, rowIndex);
                }
            });

            // 2. 使用 BeginInvoke (异步回到 UI 线程)，比 Invoke 更不容易死锁
            //dgv.BeginInvoke(new Action(() =>
            //{
            //    // 3. 再次检查行索引有效性 (因为加载图片期间，用户可能已经重新搜索，导致行数变了)
            //    if (rowIndex >= 0 && rowIndex < dgv.RowCount)
            //    {
            //        // 4. ★ 这一句是核心 ★
            //        // 它的作用是：标记这个单元格为"脏"，强制 DGV 立即调用 CellValueNeeded 重新获取值
            //        // 这时 CellValueNeeded 再去查 _imageCache，就能查到图了，而不是返回 Loading
            //        dgv.InvalidateCell(colIndex, rowIndex);
            //    }
            //}));
        }

        #endregion

        #region 搜索与分页 (占位逻辑)

        private async void TxtSearch_TextChanged(object sender, EventArgs e)
        {
            _searchCts?.Cancel();
            _searchCts = new CancellationTokenSource();
            var token = _searchCts.Token;

            try
            {
                await Task.Delay(500, token); // 防抖
                //string key = TxtSearch.Text.Trim();
                //if (key.Length < 2 && string.IsNullOrEmpty(this.CmbLimitCustomer.Text.Trim())) return;
                await SearchDataAsync(resetPage: true);
            }
            catch (TaskCanceledException) { }
        }

        private async Task SearchDataAsync(bool resetPage)
        {
            string keyword = TxtSearch.Text.Trim();
            string selectedCustomer = CmbLimitCustomer.Text.Trim();
            if (string.IsNullOrEmpty(keyword) && string.IsNullOrEmpty(selectedCustomer))
            {
                _virtualSource.Clear();
                Dgv.RowCount = _virtualSource.Count;
                return;
            }

            if (_isLoading) return;
            _isLoading = true;

            try
            {
                if (resetPage)
                {
                    _currentPage = 1;
                    _virtualSource.Clear(); // 清空虚拟源
                    Dgv.RowCount = 0;       // 重置行数
                    _hasMoreData = true;
                }

                // QueryCriteria查询对象
                var queryCriteria = new QueryCriteria();

                //1. Or 条件 在一个 AddOrGroup 里传入所有元组
                if (!string.IsNullOrEmpty(keyword) && keyword.Length >= 2) // 加上长度判断
                {
                    queryCriteria.AddOrGroup(
                        ("SKU", 查询操作符.模糊匹配, keyword),
                        ("Description", 查询操作符.模糊匹配, keyword),
                        ("CSKU", 查询操作符.模糊匹配, keyword),
                        ("Finish", 查询操作符.模糊匹配, keyword)
                    );
                }

                // =======================================================
                // 3: 下拉框条件
                // =======================================================

                if (!string.IsNullOrEmpty(selectedCustomer))
                {
                    // 这是一个 AND 条件：必须满足该客户代码
                    queryCriteria.Add("CustomerCode", 查询操作符.等于, selectedCustomer);
                }

                var result = await _productService.GetPagedListAsync(queryCriteria, _currentPage, _pageSize);
                if (!result.Success)
                {
                    // 如果后端明确返回失败 (比如数据库连接断开、权限不足)
                    // 直接提示错误信息，并中断后续逻辑
                    MessageBox.Show($"查询失败: {result.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                var mappedItems = _mapper.Map<List<ProductDto>>(result.Data.Items) ?? new List<ProductDto>();

                if (resetPage)
                {
                    _currentPage = 1;
                    _virtualSource.Clear();
                    Dgv.RowCount = 0;
                    _hasMoreData = true;

                    // ★ 优化用户体验: 搜索结果为空时提示
                    if (mappedItems.Count == 0)
                    {
                        // 这里可以不弹窗，而是在界面上显示一个 Label "暂无数据"
                        // MessageBox.Show("未找到符合条件的产品");
                    }
                }

                // 3. 判断是否还有更多页
                if (mappedItems.Count < _pageSize) _hasMoreData = false;

                // 4. 只有当确实有数据时才添加，减少不必要的重绘
                if (mappedItems.Count > 0)
                {
                    _virtualSource.AddRange(mappedItems);

                    // 重点：更新 DGV 行数
                    // VirtualMode 下，修改 RowCount 会触发重绘，所以只有真正变了才去赋值
                    ThreadHelper.UIInvoke(() =>
                    {
                        Dgv.RowCount = _virtualSource.Count;
                        _currentPage++;
                    });

                }



            }

            catch (Exception ex)
            {
                MessageBox.Show($"加载数据出错: {ex.Message}");
            }
            finally
            {
                _isLoading = false;
            }
        }

        private async void Dgv_Scroll(object sender, ScrollEventArgs e)
        {
            if (e.ScrollOrientation == ScrollOrientation.VerticalScroll)
            {
                // 虚拟模式下的触底判断
                if (Dgv.RowCount > 0 &&
                    Dgv.FirstDisplayedScrollingRowIndex + Dgv.DisplayedRowCount(false) >= Dgv.RowCount - 5)
                {
                    if (_hasMoreData && !_isLoading)
                    {
                        await SearchDataAsync(resetPage: false);
                    }
                }
            }
        }

        #endregion

        #region 左右移动辅助

        private List<ProductDto> GetSelectedFromVirtual()
        {
            var list = new List<ProductDto>();
            // 虚拟模式下 SelectedRows 依然可用，但获取到的 DataBoundItem 是 null
            // 所以我们需要通过 RowIndex 去 _virtualSource 查
            if (Dgv.SelectedRows.Count > 0)
            {
                foreach (DataGridViewRow row in Dgv.SelectedRows)
                {
                    if (row.Index < _virtualSource.Count)
                        list.Add(_virtualSource[row.Index]);
                }
            }
            return list;
        }

        private List<ProductDto> GetSelectedFromBinding(DataGridView dgv)
        {
            // 普通模式直接拿 DataBoundItem
            var list = new List<ProductDto>();
            foreach (DataGridViewRow row in dgv.SelectedRows)
            {
                if (row.DataBoundItem is ProductDto dto) list.Add(dto);
            }
            return list;
        }

        private void AddToSelected(List<ProductDto> items)
        {
            foreach (var item in items)
            {
                if (!_selectedList.Any(x => x.SKU == item.SKU))
                {
                    _selectedList.Add(item);
                }
            }
        }

        private void RemoveFromSelected(List<ProductDto> items)
        {
            foreach (var item in items)
            {
                _selectedList.Remove(item);
            }
        }

        private void Dgv_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            // 1. 排除表头点击 (RowIndex = -1)
            if (e.RowIndex < 0) return;

            // 2. 越界检查 (防止列表刚好被清空时触发)
            if (e.RowIndex >= _virtualSource.Count) return;

            // 3. 从虚拟数据源中拿到当前双击的对象
            var item = _virtualSource[e.RowIndex];

            // 4. 复用之前的 AddToSelected 逻辑
            // 注意：这里我们包装成一个 List 传进去
            AddToSelected(new List<ProductDto> { item });
        }

        //右侧列表双击移除逻辑
        private void SelectedDgv_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;

            // 右侧是普通模式，可以直接拿 DataBoundItem
            if (SelectedDgv.Rows[e.RowIndex].DataBoundItem is ProductDto item)
            {
                // 复用之前的 RemoveFromSelected 逻辑
                RemoveFromSelected(new List<ProductDto> { item });
            }
        }


        #endregion



        // 记录当前的排序状态，方便实现“升序/降序”切换
        private readonly Dictionary<string, SortOrder> _sortStatus = new Dictionary<string, SortOrder>();

        private void Dgv_ColumnHeaderMouseClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            var column = Dgv.Columns[e.ColumnIndex];
            string propertyName = column.DataPropertyName;

            // 1. 如果没有绑定属性（比如图片列），直接忽略
            if (string.IsNullOrEmpty(propertyName)) return;

            // 2. 确定排序方向 (默认升序，如果点过则反转)
            SortOrder currentOrder = SortOrder.Ascending;
            if (_sortStatus.ContainsKey(propertyName) && _sortStatus[propertyName] == SortOrder.Ascending)
            {
                currentOrder = SortOrder.Descending;
            }
            _sortStatus[propertyName] = currentOrder;

            // 3. 更新表头的小箭头图标
            column.HeaderCell.SortGlyphDirection = currentOrder;

            // 4. ★ 核心：对内存里的 List 进行排序 ★
            // 这里使用反射比较通用，如果你追求极致性能，可以用 Switch Case 写死
            var propInfo = typeof(ProductDto).GetProperty(propertyName);

            if (propInfo != null)
            {
                _virtualSource.Sort((x, y) =>
                {
                    object valX = propInfo.GetValue(x);
                    object valY = propInfo.GetValue(y);

                    // 处理 null 值
                    if (valX == null && valY == null) return 0;
                    if (valX == null) return -1;
                    if (valY == null) return 1;

                    // 字符串比较
                    if (valX is IComparable cX)
                    {
                        int result = cX.CompareTo(valY);
                        return currentOrder == SortOrder.Descending ? -result : result;
                    }
                    return 0;
                });
            }

            // 5. 强制重绘整个 DGV (因为行顺序全变了)
            Dgv.Invalidate();
        }
    }
}