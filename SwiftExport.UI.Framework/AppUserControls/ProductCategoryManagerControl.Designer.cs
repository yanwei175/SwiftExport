namespace SwiftExport.UI.Framework.AppUserControls
{
    partial class ProductCategoryManagerControl
    {
        /// <summary> 
        /// 必需的设计器变量。
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary> 
        /// 清理所有正在使用的资源。
        /// </summary>
        /// <param name="disposing">如果应释放托管资源，为 true；否则为 false。</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region 组件设计器生成的代码

        /// <summary> 
        /// 设计器支持所需的方法 - 不要修改
        /// 使用代码编辑器修改此方法的内容。
        /// </summary>
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this._splitContainer = new System.Windows.Forms.SplitContainer();
            this._leftPanel = new System.Windows.Forms.Panel();
            this._treeView = new System.Windows.Forms.TreeView();
            this._editPanel = new System.Windows.Forms.Panel();
            this.groupBox1 = new System.Windows.Forms.GroupBox();
            this.label3 = new System.Windows.Forms.Label();
            this._txtNo = new System.Windows.Forms.TextBox();
            this.label2 = new System.Windows.Forms.Label();
            this._txtName = new System.Windows.Forms.TextBox();
            this.label1 = new System.Windows.Forms.Label();
            this._numSort = new System.Windows.Forms.NumericUpDown();
            this._lblCurrentAction = new System.Windows.Forms.Label();
            this._btnCancel = new System.Windows.Forms.Button();
            this._btnDelete = new System.Windows.Forms.Button();
            this._btnSave = new System.Windows.Forms.Button();
            this._btnAddChild = new System.Windows.Forms.Button();
            this._btnMoveDown = new System.Windows.Forms.Button();
            this._btnMoveUp = new System.Windows.Forms.Button();
            this._btnAddRoot = new System.Windows.Forms.Button();
            this.ContextMenuStrip1 = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.上移ToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.下移ToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            ((System.ComponentModel.ISupportInitialize)(this._splitContainer)).BeginInit();
            this._splitContainer.Panel1.SuspendLayout();
            this._splitContainer.Panel2.SuspendLayout();
            this._splitContainer.SuspendLayout();
            this._leftPanel.SuspendLayout();
            this._editPanel.SuspendLayout();
            this.groupBox1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this._numSort)).BeginInit();
            this.ContextMenuStrip1.SuspendLayout();
            this.SuspendLayout();
            // 
            // _splitContainer
            // 
            this._splitContainer.Dock = System.Windows.Forms.DockStyle.Fill;
            this._splitContainer.Location = new System.Drawing.Point(0, 0);
            this._splitContainer.Margin = new System.Windows.Forms.Padding(4);
            this._splitContainer.Name = "_splitContainer";
            // 
            // _splitContainer.Panel1
            // 
            this._splitContainer.Panel1.Controls.Add(this._leftPanel);
            // 
            // _splitContainer.Panel2
            // 
            this._splitContainer.Panel2.Controls.Add(this._editPanel);
            this._splitContainer.Size = new System.Drawing.Size(985, 782);
            this._splitContainer.SplitterDistance = 440;
            this._splitContainer.SplitterWidth = 5;
            this._splitContainer.TabIndex = 0;
            // 
            // _leftPanel
            // 
            this._leftPanel.Controls.Add(this._treeView);
            this._leftPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this._leftPanel.Location = new System.Drawing.Point(0, 0);
            this._leftPanel.Name = "_leftPanel";
            this._leftPanel.Padding = new System.Windows.Forms.Padding(0, 0, 1, 0);
            this._leftPanel.Size = new System.Drawing.Size(440, 782);
            this._leftPanel.TabIndex = 1;
            // 
            // _treeView
            // 
            this._treeView.AllowDrop = true;
            this._treeView.ContextMenuStrip = this.ContextMenuStrip1;
            this._treeView.Dock = System.Windows.Forms.DockStyle.Fill;
            this._treeView.FullRowSelect = true;
            this._treeView.HideSelection = false;
            this._treeView.ItemHeight = 50;
            this._treeView.Location = new System.Drawing.Point(0, 0);
            this._treeView.Margin = new System.Windows.Forms.Padding(4);
            this._treeView.Name = "_treeView";
            this._treeView.Size = new System.Drawing.Size(439, 782);
            this._treeView.TabIndex = 0;
            this._treeView.ItemDrag += new System.Windows.Forms.ItemDragEventHandler(this.OnTreeItemDrag);
            this._treeView.AfterSelect += new System.Windows.Forms.TreeViewEventHandler(this.OnTreeAfterSelect);
            this._treeView.DragDrop += new System.Windows.Forms.DragEventHandler(this.OnTreeDragDrop);
            this._treeView.DragEnter += new System.Windows.Forms.DragEventHandler(this.OnTreeDragEnter);
            this._treeView.DragOver += new System.Windows.Forms.DragEventHandler(this.OnTreeDragOver);
            // 
            // _editPanel
            // 
            this._editPanel.BackColor = System.Drawing.Color.WhiteSmoke;
            this._editPanel.Controls.Add(this.groupBox1);
            this._editPanel.Controls.Add(this._lblCurrentAction);
            this._editPanel.Controls.Add(this._btnCancel);
            this._editPanel.Controls.Add(this._btnDelete);
            this._editPanel.Controls.Add(this._btnSave);
            this._editPanel.Controls.Add(this._btnAddChild);
            this._editPanel.Controls.Add(this._btnMoveDown);
            this._editPanel.Controls.Add(this._btnMoveUp);
            this._editPanel.Controls.Add(this._btnAddRoot);
            this._editPanel.Cursor = System.Windows.Forms.Cursors.Default;
            this._editPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this._editPanel.Location = new System.Drawing.Point(0, 0);
            this._editPanel.Margin = new System.Windows.Forms.Padding(4);
            this._editPanel.Name = "_editPanel";
            this._editPanel.Size = new System.Drawing.Size(540, 782);
            this._editPanel.TabIndex = 0;
            // 
            // groupBox1
            // 
            this.groupBox1.Controls.Add(this.label3);
            this.groupBox1.Controls.Add(this._txtNo);
            this.groupBox1.Controls.Add(this.label2);
            this.groupBox1.Controls.Add(this._txtName);
            this.groupBox1.Controls.Add(this.label1);
            this.groupBox1.Controls.Add(this._numSort);
            this.groupBox1.ForeColor = System.Drawing.Color.Gray;
            this.groupBox1.Location = new System.Drawing.Point(20, 200);
            this.groupBox1.Name = "groupBox1";
            this.groupBox1.Size = new System.Drawing.Size(470, 322);
            this.groupBox1.TabIndex = 7;
            this.groupBox1.TabStop = false;
            this.groupBox1.Text = "详细信息";
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.ForeColor = System.Drawing.Color.Black;
            this.label3.Location = new System.Drawing.Point(11, 62);
            this.label3.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(51, 16);
            this.label3.TabIndex = 1;
            this.label3.Text = "分类名称";
            // 
            // _txtNo
            // 
            this._txtNo.Location = new System.Drawing.Point(160, 133);
            this._txtNo.Margin = new System.Windows.Forms.Padding(4);
            this._txtNo.Name = "_txtNo";
            this._txtNo.Size = new System.Drawing.Size(108, 21);
            this._txtNo.TabIndex = 5;
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.ForeColor = System.Drawing.Color.Black;
            this.label2.Location = new System.Drawing.Point(11, 138);
            this.label2.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(51, 16);
            this.label2.TabIndex = 1;
            this.label2.Text = "分类编号";
            // 
            // _txtName
            // 
            this._txtName.Location = new System.Drawing.Point(160, 62);
            this._txtName.Margin = new System.Windows.Forms.Padding(4);
            this._txtName.Name = "_txtName";
            this._txtName.Size = new System.Drawing.Size(108, 21);
            this._txtName.TabIndex = 5;
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.ForeColor = System.Drawing.Color.Black;
            this.label1.Location = new System.Drawing.Point(11, 211);
            this.label1.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(29, 16);
            this.label1.TabIndex = 1;
            this.label1.Text = "排序";
            // 
            // _numSort
            // 
            this._numSort.Location = new System.Drawing.Point(160, 206);
            this._numSort.Margin = new System.Windows.Forms.Padding(4);
            this._numSort.Name = "_numSort";
            this._numSort.Size = new System.Drawing.Size(130, 21);
            this._numSort.TabIndex = 6;
            // 
            // _lblCurrentAction
            // 
            this._lblCurrentAction.AutoSize = true;
            this._lblCurrentAction.ForeColor = System.Drawing.Color.DimGray;
            this._lblCurrentAction.Location = new System.Drawing.Point(15, 21);
            this._lblCurrentAction.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this._lblCurrentAction.Name = "_lblCurrentAction";
            this._lblCurrentAction.Size = new System.Drawing.Size(128, 16);
            this._lblCurrentAction.TabIndex = 1;
            this._lblCurrentAction.Text = "请选择左侧分类进行操作";
            // 
            // _btnCancel
            // 
            this._btnCancel.BackColor = System.Drawing.SystemColors.Control;
            this._btnCancel.Cursor = System.Windows.Forms.Cursors.Hand;
            this._btnCancel.FlatAppearance.BorderSize = 0;
            this._btnCancel.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this._btnCancel.Location = new System.Drawing.Point(311, 555);
            this._btnCancel.Margin = new System.Windows.Forms.Padding(4);
            this._btnCancel.Name = "_btnCancel";
            this._btnCancel.Size = new System.Drawing.Size(150, 50);
            this._btnCancel.TabIndex = 2;
            this._btnCancel.Text = "取消";
            this._btnCancel.UseVisualStyleBackColor = false;
            // 
            // _btnDelete
            // 
            this._btnDelete.BackColor = System.Drawing.Color.IndianRed;
            this._btnDelete.Cursor = System.Windows.Forms.Cursors.Hand;
            this._btnDelete.FlatAppearance.BorderSize = 0;
            this._btnDelete.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this._btnDelete.Location = new System.Drawing.Point(340, 70);
            this._btnDelete.Margin = new System.Windows.Forms.Padding(4);
            this._btnDelete.Name = "_btnDelete";
            this._btnDelete.Size = new System.Drawing.Size(150, 50);
            this._btnDelete.TabIndex = 4;
            this._btnDelete.Text = "删除选中";
            this._btnDelete.UseVisualStyleBackColor = false;
            this._btnDelete.Click += new System.EventHandler(this.OnDeleteClick);
            // 
            // _btnSave
            // 
            this._btnSave.BackColor = System.Drawing.Color.Teal;
            this._btnSave.Cursor = System.Windows.Forms.Cursors.Hand;
            this._btnSave.FlatAppearance.BorderSize = 0;
            this._btnSave.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this._btnSave.ForeColor = System.Drawing.Color.White;
            this._btnSave.Location = new System.Drawing.Point(120, 555);
            this._btnSave.Margin = new System.Windows.Forms.Padding(4);
            this._btnSave.Name = "_btnSave";
            this._btnSave.Size = new System.Drawing.Size(150, 50);
            this._btnSave.TabIndex = 2;
            this._btnSave.Text = "保存";
            this._btnSave.UseVisualStyleBackColor = false;
            this._btnSave.Click += new System.EventHandler(this.OnSaveClick);
            // 
            // _btnAddChild
            // 
            this._btnAddChild.BackColor = System.Drawing.SystemColors.Control;
            this._btnAddChild.Cursor = System.Windows.Forms.Cursors.Hand;
            this._btnAddChild.FlatAppearance.BorderSize = 0;
            this._btnAddChild.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this._btnAddChild.Location = new System.Drawing.Point(180, 70);
            this._btnAddChild.Margin = new System.Windows.Forms.Padding(4);
            this._btnAddChild.Name = "_btnAddChild";
            this._btnAddChild.Size = new System.Drawing.Size(150, 50);
            this._btnAddChild.TabIndex = 3;
            this._btnAddChild.Text = "新建子分类";
            this._btnAddChild.UseVisualStyleBackColor = false;
            // 
            // _btnMoveDown
            // 
            this._btnMoveDown.BackColor = System.Drawing.SystemColors.Control;
            this._btnMoveDown.Cursor = System.Windows.Forms.Cursors.Hand;
            this._btnMoveDown.FlatAppearance.BorderSize = 0;
            this._btnMoveDown.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this._btnMoveDown.Location = new System.Drawing.Point(180, 128);
            this._btnMoveDown.Margin = new System.Windows.Forms.Padding(4);
            this._btnMoveDown.Name = "_btnMoveDown";
            this._btnMoveDown.Size = new System.Drawing.Size(150, 50);
            this._btnMoveDown.TabIndex = 2;
            this._btnMoveDown.Text = "x";
            this._btnMoveDown.UseVisualStyleBackColor = false;
            // 
            // _btnMoveUp
            // 
            this._btnMoveUp.BackColor = System.Drawing.SystemColors.Control;
            this._btnMoveUp.Cursor = System.Windows.Forms.Cursors.Hand;
            this._btnMoveUp.FlatAppearance.BorderSize = 0;
            this._btnMoveUp.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this._btnMoveUp.Location = new System.Drawing.Point(20, 128);
            this._btnMoveUp.Margin = new System.Windows.Forms.Padding(4);
            this._btnMoveUp.Name = "_btnMoveUp";
            this._btnMoveUp.Size = new System.Drawing.Size(150, 50);
            this._btnMoveUp.TabIndex = 2;
            this._btnMoveUp.Text = "s";
            this._btnMoveUp.UseVisualStyleBackColor = false;
            // 
            // _btnAddRoot
            // 
            this._btnAddRoot.BackColor = System.Drawing.SystemColors.Control;
            this._btnAddRoot.Cursor = System.Windows.Forms.Cursors.Hand;
            this._btnAddRoot.FlatAppearance.BorderSize = 0;
            this._btnAddRoot.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this._btnAddRoot.Location = new System.Drawing.Point(20, 70);
            this._btnAddRoot.Margin = new System.Windows.Forms.Padding(4);
            this._btnAddRoot.Name = "_btnAddRoot";
            this._btnAddRoot.Size = new System.Drawing.Size(150, 50);
            this._btnAddRoot.TabIndex = 2;
            this._btnAddRoot.Text = "新建根分类";
            this._btnAddRoot.UseVisualStyleBackColor = false;
            // 
            // ContextMenuStrip1
            // 
            this.ContextMenuStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.上移ToolStripMenuItem,
            this.下移ToolStripMenuItem});
            this.ContextMenuStrip1.Name = "ContextMenuStrip1";
            this.ContextMenuStrip1.Size = new System.Drawing.Size(101, 48);
            // 
            // 上移ToolStripMenuItem
            // 
            this.上移ToolStripMenuItem.Name = "上移ToolStripMenuItem";
            this.上移ToolStripMenuItem.Size = new System.Drawing.Size(100, 22);
            this.上移ToolStripMenuItem.Text = "上移";
            // 
            // 下移ToolStripMenuItem
            // 
            this.下移ToolStripMenuItem.Name = "下移ToolStripMenuItem";
            this.下移ToolStripMenuItem.Size = new System.Drawing.Size(100, 22);
            this.下移ToolStripMenuItem.Text = "下移";
            // 
            // ProductCategoryManagerControl
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 16F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this._splitContainer);
            this.Font = new System.Drawing.Font("Microsoft YaHei UI", 8F);
            this.Margin = new System.Windows.Forms.Padding(4);
            this.Name = "ProductCategoryManagerControl";
            this.Size = new System.Drawing.Size(985, 782);
            this._splitContainer.Panel1.ResumeLayout(false);
            this._splitContainer.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this._splitContainer)).EndInit();
            this._splitContainer.ResumeLayout(false);
            this._leftPanel.ResumeLayout(false);
            this._editPanel.ResumeLayout(false);
            this._editPanel.PerformLayout();
            this.groupBox1.ResumeLayout(false);
            this.groupBox1.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this._numSort)).EndInit();
            this.ContextMenuStrip1.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.SplitContainer _splitContainer;
        private System.Windows.Forms.TreeView _treeView;
        private System.Windows.Forms.NumericUpDown _numSort;
        private System.Windows.Forms.TextBox _txtNo;
        private System.Windows.Forms.TextBox _txtName;
        private System.Windows.Forms.Button _btnDelete;
        private System.Windows.Forms.Button _btnAddChild;
        private System.Windows.Forms.Button _btnCancel;
        private System.Windows.Forms.Button _btnSave;
        private System.Windows.Forms.Button _btnAddRoot;
        private System.Windows.Forms.Label _lblCurrentAction;
        private System.Windows.Forms.Panel _editPanel;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Panel _leftPanel;
        private System.Windows.Forms.GroupBox groupBox1;
        private System.Windows.Forms.Button _btnMoveUp;
        private System.Windows.Forms.Button _btnMoveDown;
        private System.Windows.Forms.ContextMenuStrip ContextMenuStrip1;
        private System.Windows.Forms.ToolStripMenuItem 上移ToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem 下移ToolStripMenuItem;
    }
}
