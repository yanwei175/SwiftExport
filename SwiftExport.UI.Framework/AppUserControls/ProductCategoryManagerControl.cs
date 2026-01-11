using SwiftExport.AppLayer.DTOs;
using SwiftExport.AppLayer.Interfaces;
using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SwiftExport.UI.Framework.AppUserControls
{
    public partial class ProductCategoryManagerControl : UserControl
    {
        private IProductCategoryService _service;

        // 状态变量
        private ProductCategoryDto _currentDto;
        private bool _isAddingNew = false;
        private int? _targetParentId = null;

        // 构造函数
        public ProductCategoryManagerControl()
        {
            InitializeComponent();
            BindEvents();
        }

        public ProductCategoryManagerControl(IProductCategoryService service) : this()
        {
            _service = service;
        }

        public void SetService(IProductCategoryService service)
        {
            _service = service;
            if (this.IsHandleCreated && !this.DesignMode) LoadTreeSafe();
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            if (!this.DesignMode && _service != null) LoadTreeSafe();
        }

        private void BindEvents()
        {
            _treeView.AllowDrop = true;
            _treeView.HideSelection = false;

            _treeView.AfterSelect += OnTreeAfterSelect;

            // 拖拽事件 (简化版)
            _treeView.ItemDrag += OnTreeItemDrag;
            _treeView.DragEnter += OnTreeDragEnter;
            _treeView.DragOver += OnTreeDragOver;
            _treeView.DragDrop += OnTreeDragDrop;

            // 按钮事件
            _btnAddRoot.Click += (s, e) => StartAddNew(null);
            _btnAddChild.Click += (s, e) => StartAddNew(_treeView.SelectedNode);
            _btnDelete.Click += OnDeleteClick;
            _btnSave.Click += OnSaveClick;
            _btnCancel.Click += (s, e) => ResetEditArea();

            // 【新增】排序按钮事件
            if (_btnMoveUp != null) _btnMoveUp.Click += OnMoveUpClick;
            if (_btnMoveDown != null) _btnMoveDown.Click += OnMoveDownClick;
        }

        // --- 数据加载 ---
        private void LoadTreeSafe()
        {
            if (!this.IsHandleCreated || this.IsDisposed) return;
            if (_treeView == null) return;

            int? selectedId = (_treeView.SelectedNode?.Tag as ProductCategoryDto)?.ID;

            Task.Run(async () =>
            {
                if (_service == null) return;
                try
                {
                    var rst = await _service.GetTreeListAsync();
                    if (!rst.Success) return;
                    var treeData = rst.Data;

                    this.BeginInvoke(new Action(() =>
                    {
                        if (this.IsDisposed || !this.IsHandleCreated) return;

                        _treeView.BeginUpdate();
                        _treeView.Nodes.Clear();
                        foreach (var dto in treeData)
                        {
                            var node = CreateTreeNode(dto);
                            _treeView.Nodes.Add(node);
                            if (selectedId.HasValue) FindAndSelectNode(node, selectedId.Value);
                        }
                        _treeView.EndUpdate();
                        _treeView.ExpandAll();

                        // 加载完后刷新按钮状态
                        UpdateMoveButtonsState();
                    }));
                }
                catch (Exception ex)
                {
                    this.BeginInvoke(new Action(() => MessageBox.Show("加载失败: " + ex.Message)));
                }
            });
        }

        private TreeNode CreateTreeNode(ProductCategoryDto dto)
        {
            var node = new TreeNode(dto.分类名称) { Tag = dto };
            foreach (var child in dto.子集列表)
            {
                node.Nodes.Add(CreateTreeNode(child));
            }
            return node;
        }

        private void FindAndSelectNode(TreeNode node, int id)
        {
            if (node.Tag is ProductCategoryDto dto && dto.ID == id)
            {
                _treeView.SelectedNode = node;
                _treeView.Focus(); // 确保焦点回来
                return;
            }
            foreach (TreeNode child in node.Nodes) FindAndSelectNode(child, id);
        }

        // --- 核心：极简版拖拽 (只处理父子关系，不卡顿) ---

        private void OnTreeItemDrag(object sender, ItemDragEventArgs e)
        {
            DoDragDrop(e.Item, DragDropEffects.Move);
        }

        private void OnTreeDragEnter(object sender, DragEventArgs e)
        {
            e.Effect = e.Data.GetDataPresent(typeof(TreeNode)) ? DragDropEffects.Move : DragDropEffects.None;
        }

        private void OnTreeDragOver(object sender, DragEventArgs e)
        {
            // 【优化】不再计算上下位置，只计算“当前鼠标下的节点”
            Point pt = _treeView.PointToClient(new Point(e.X, e.Y));
            TreeNode targetNode = _treeView.GetNodeAt(pt);

            // 选中它，给用户反馈
            if (targetNode != null && _treeView.SelectedNode != targetNode)
            {
                _treeView.SelectedNode = targetNode;
            }

            e.Effect = DragDropEffects.Move;
        }

        private async void OnTreeDragDrop(object sender, DragEventArgs e)
        {
            Point pt = _treeView.PointToClient(new Point(e.X, e.Y));
            TreeNode targetNode = _treeView.GetNodeAt(pt);
            TreeNode draggedNode = (TreeNode)e.Data.GetData(typeof(TreeNode));

            if (draggedNode == null) return;
            if (targetNode == draggedNode) return;

            var draggedDto = draggedNode.Tag as ProductCategoryDto;

            try
            {
                // 1. 拖到空白处 -> 变根节点
                if (targetNode == null)
                {
                    if (draggedDto.父级ID != null)
                    {
                        await _service.MoveCategoryAsync(draggedDto.ID, null);
                        LoadTreeSafe();
                    }
                }
                // 2. 拖到其他节点上 -> 变子节点
                else
                {
                    // 防死循环
                    if (IsContains(draggedNode, targetNode))
                    {
                        MessageBox.Show("不能移动到自己的子节点下");
                        return;
                    }

                    var targetDto = targetNode.Tag as ProductCategoryDto;

                    // 只有当父级真正改变时才提交
                    if (draggedDto.父级ID != targetDto.ID)
                    {
                        await _service.MoveCategoryAsync(draggedDto.ID, targetDto.ID);
                        LoadTreeSafe();
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("移动失败: " + ex.Message);
                LoadTreeSafe();
            }
        }

        private bool IsContains(TreeNode parent, TreeNode target)
        {
            if (target.Parent == null) return false;
            if (target.Parent == parent) return true;
            return IsContains(parent, target.Parent);
        }

        // --- 【新增】上下移动逻辑 ---

        private async void OnMoveUpClick(object sender, EventArgs e)
        {
            if (_currentDto == null) return;
            // 锁定界面防连点
            this.Enabled = false;
            try
            {
                await _service.MoveNodeUpAsync(_currentDto.ID);
                LoadTreeSafe();
            }
            finally { this.Enabled = true; }
        }

        private async void OnMoveDownClick(object sender, EventArgs e)
        {
            if (_currentDto == null) return;
            this.Enabled = false;
            try
            {
                await _service.MoveNodeDownAsync(_currentDto.ID);
                LoadTreeSafe();
            }
            finally { this.Enabled = true; }
        }

        private void UpdateMoveButtonsState()
        {
            // 可选：根据当前节点是否是第一个或最后一个，禁用按钮
            // 简单起见，可以先不做，反正后台有点了也不动
            if (_btnMoveUp == null || _btnMoveDown == null) return;

            bool hasSelection = _treeView.SelectedNode != null;
            _btnMoveUp.Enabled = hasSelection;
            _btnMoveDown.Enabled = hasSelection;
        }

        // --- 常规交互 ---

        private void OnTreeAfterSelect(object sender, TreeViewEventArgs e)
        {
            if (_isAddingNew) return;
            if (!(e.Node.Tag is ProductCategoryDto dto)) return;

            _currentDto = dto;
            _isAddingNew = false;
            _targetParentId = null;

            _lblCurrentAction.Text = $"正在编辑: {dto.分类名称}";
            _txtName.Text = dto.分类名称;
            _txtNo.Text = dto.分类编号;
            _numSort.Value = dto.排序编号;

            ToggleInputs(true);
            _btnAddChild.Enabled = true;
            _btnDelete.Enabled = true;

            UpdateMoveButtonsState();
        }

        private void StartAddNew(TreeNode parentNode)
        {
            _isAddingNew = true;
            _currentDto = null;
            _targetParentId = null;
            string parentName = "根节点";

            if (parentNode != null)
            {
                var parentDto = parentNode.Tag as ProductCategoryDto;
                _targetParentId = parentDto.ID;
                parentName = parentDto.分类名称;
            }

            _lblCurrentAction.Text = $"新增分类 (父级: {parentName})";
            _txtName.Clear();
            _txtNo.Clear();
            _numSort.Value = 0;
            _txtName.Focus();

            ToggleInputs(true);
            _btnDelete.Enabled = false;
            // 新增时禁用排序
            if (_btnMoveUp != null) _btnMoveUp.Enabled = false;
            if (_btnMoveDown != null) _btnMoveDown.Enabled = false;
        }

        private async void OnSaveClick(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_txtName.Text)) { MessageBox.Show("名称不能为空"); return; }
            _btnSave.Enabled = false;
            try
            {
                var dto = new ProductCategoryDto
                {
                    分类名称 = _txtName.Text.Trim(),
                    分类编号 = _txtNo.Text.Trim(),
                    排序编号 = (int)_numSort.Value,
                };

                if (_isAddingNew)
                {
                    dto.父级ID = _targetParentId;
                    await _service.CreateByDtoAsync(dto);
                }
                else
                {
                    if (_currentDto == null) return;
                    dto.ID = _currentDto.ID;
                    dto.父级ID = _currentDto.父级ID;
                    await _service.UpdateByDtoAsync(dto);
                }
                ResetEditArea();
                LoadTreeSafe();
                MessageBox.Show("保存成功");
            }
            catch (Exception ex) { MessageBox.Show("保存失败: " + ex.Message); }
            finally { _btnSave.Enabled = true; }
        }

        private async void OnDeleteClick(object sender, EventArgs e)
        {
            if (_currentDto == null) return;
            if (MessageBox.Show($"确定删除 [{_currentDto.分类名称}]?", "确认", MessageBoxButtons.YesNo) == DialogResult.Yes)
            {
                try
                {
                    await _service.DeleteAsync(_currentDto.ID);
                    ResetEditArea();
                    LoadTreeSafe();
                }
                catch (Exception ex) { MessageBox.Show("删除失败: " + ex.Message); }
            }
        }

        private void ResetEditArea()
        {
            _isAddingNew = false;
            _currentDto = null;
            _txtName.Clear();
            _txtNo.Clear();
            _lblCurrentAction.Text = "请选择左侧分类";
            ToggleInputs(false);
            _btnAddChild.Enabled = false;
            _btnDelete.Enabled = false;
            if (_btnMoveUp != null) _btnMoveUp.Enabled = false;
            if (_btnMoveDown != null) _btnMoveDown.Enabled = false;
        }

        private void ToggleInputs(bool enable)
        {
            _txtName.Enabled = enable;
            _txtNo.Enabled = enable;
            _numSort.Enabled = enable;
            _btnSave.Enabled = enable;
            _btnCancel.Enabled = enable;
        }
    }
}