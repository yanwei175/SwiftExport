namespace SwiftExport.UiWinForm
{
    partial class MainFrm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(MainFrm));
            statusStrip1 = new StatusStrip();
            toolStripStatusLabel1 = new ToolStripStatusLabel();
            menuStrip1 = new MenuStrip();
            toolStripMenuItem1 = new ToolStripMenuItem();
            编辑ToolStripMenuItem = new ToolStripMenuItem();
            销售ToolStripMenuItem = new ToolStripMenuItem();
            CustomerSetting = new ToolStripMenuItem();
            采购ToolStripMenuItem = new ToolStripMenuItem();
            供应商管理ToolStripMenuItem = new ToolStripMenuItem();
            excel字段映射管理ToolStripMenuItem = new ToolStripMenuItem();
            toolStripContainer1 = new ToolStripContainer();
            tableLayoutPanel1 = new TableLayoutPanel();
            Panel_Left1 = new Panel();
            Panel_Right2 = new Panel();
            button1 = new Button();
            toolStrip1 = new ToolStrip();
            toolStripLabel1 = new ToolStripLabel();
            toolStripButton1 = new ToolStripButton();
            产品颜色管理ToolStripMenuItem = new ToolStripMenuItem();
            statusStrip1.SuspendLayout();
            menuStrip1.SuspendLayout();
            toolStripContainer1.ContentPanel.SuspendLayout();
            toolStripContainer1.TopToolStripPanel.SuspendLayout();
            toolStripContainer1.SuspendLayout();
            tableLayoutPanel1.SuspendLayout();
            Panel_Right2.SuspendLayout();
            toolStrip1.SuspendLayout();
            SuspendLayout();
            // 
            // statusStrip1
            // 
            statusStrip1.ImageScalingSize = new Size(32, 32);
            statusStrip1.Items.AddRange(new ToolStripItem[] { toolStripStatusLabel1 });
            statusStrip1.Location = new Point(0, 419);
            statusStrip1.Name = "statusStrip1";
            statusStrip1.Padding = new Padding(1, 0, 16, 0);
            statusStrip1.Size = new Size(828, 22);
            statusStrip1.TabIndex = 0;
            statusStrip1.Text = "statusStrip1";
            // 
            // toolStripStatusLabel1
            // 
            toolStripStatusLabel1.Name = "toolStripStatusLabel1";
            toolStripStatusLabel1.Size = new Size(44, 17);
            toolStripStatusLabel1.Text = "状态栏";
            // 
            // menuStrip1
            // 
            menuStrip1.ImageScalingSize = new Size(32, 32);
            menuStrip1.Items.AddRange(new ToolStripItem[] { toolStripMenuItem1, 编辑ToolStripMenuItem, 销售ToolStripMenuItem, 采购ToolStripMenuItem });
            menuStrip1.Location = new Point(0, 0);
            menuStrip1.Name = "menuStrip1";
            menuStrip1.Padding = new Padding(7, 3, 0, 3);
            menuStrip1.Size = new Size(828, 27);
            menuStrip1.TabIndex = 1;
            menuStrip1.Text = "menuStrip1";
            // 
            // toolStripMenuItem1
            // 
            toolStripMenuItem1.Name = "toolStripMenuItem1";
            toolStripMenuItem1.Size = new Size(44, 21);
            toolStripMenuItem1.Text = "菜单";
            // 
            // 编辑ToolStripMenuItem
            // 
            编辑ToolStripMenuItem.Name = "编辑ToolStripMenuItem";
            编辑ToolStripMenuItem.Size = new Size(44, 21);
            编辑ToolStripMenuItem.Text = "编辑";
            // 
            // 销售ToolStripMenuItem
            // 
            销售ToolStripMenuItem.DropDownItems.AddRange(new ToolStripItem[] { CustomerSetting });
            销售ToolStripMenuItem.Name = "销售ToolStripMenuItem";
            销售ToolStripMenuItem.Size = new Size(44, 21);
            销售ToolStripMenuItem.Text = "销售";
            // 
            // CustomerSetting
            // 
            CustomerSetting.Name = "CustomerSetting";
            CustomerSetting.Size = new Size(180, 22);
            CustomerSetting.Text = "客户设置";
            CustomerSetting.Click += CustomerSetting_Click;
            // 
            // 采购ToolStripMenuItem
            // 
            采购ToolStripMenuItem.DropDownItems.AddRange(new ToolStripItem[] { 供应商管理ToolStripMenuItem, excel字段映射管理ToolStripMenuItem, 产品颜色管理ToolStripMenuItem });
            采购ToolStripMenuItem.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point, 134);
            采购ToolStripMenuItem.ForeColor = Color.Black;
            采购ToolStripMenuItem.Name = "采购ToolStripMenuItem";
            采购ToolStripMenuItem.Size = new Size(44, 21);
            采购ToolStripMenuItem.Text = "采购";
            // 
            // 供应商管理ToolStripMenuItem
            // 
            供应商管理ToolStripMenuItem.Name = "供应商管理ToolStripMenuItem";
            供应商管理ToolStripMenuItem.Size = new Size(180, 22);
            供应商管理ToolStripMenuItem.Text = "供应商管理";
            供应商管理ToolStripMenuItem.Click += 供应商管理ToolStripMenuItem_Click;
            // 
            // excel字段映射管理ToolStripMenuItem
            // 
            excel字段映射管理ToolStripMenuItem.Name = "excel字段映射管理ToolStripMenuItem";
            excel字段映射管理ToolStripMenuItem.Size = new Size(180, 22);
            excel字段映射管理ToolStripMenuItem.Text = "Excel字段映射管理";
            excel字段映射管理ToolStripMenuItem.Click += Excel字段映射管理ToolStripMenuItem_Click;
            // 
            // toolStripContainer1
            // 
            // 
            // toolStripContainer1.ContentPanel
            // 
            toolStripContainer1.ContentPanel.Controls.Add(tableLayoutPanel1);
            toolStripContainer1.ContentPanel.Size = new Size(828, 353);
            toolStripContainer1.Dock = DockStyle.Fill;
            toolStripContainer1.Location = new Point(0, 27);
            toolStripContainer1.Name = "toolStripContainer1";
            toolStripContainer1.Size = new Size(828, 392);
            toolStripContainer1.TabIndex = 2;
            toolStripContainer1.Text = "toolStripContainer1";
            // 
            // toolStripContainer1.TopToolStripPanel
            // 
            toolStripContainer1.TopToolStripPanel.Controls.Add(toolStrip1);
            // 
            // tableLayoutPanel1
            // 
            tableLayoutPanel1.CellBorderStyle = TableLayoutPanelCellBorderStyle.Single;
            tableLayoutPanel1.ColumnCount = 2;
            tableLayoutPanel1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 12.6143475F));
            tableLayoutPanel1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 87.38565F));
            tableLayoutPanel1.Controls.Add(Panel_Left1, 0, 0);
            tableLayoutPanel1.Controls.Add(Panel_Right2, 1, 0);
            tableLayoutPanel1.Dock = DockStyle.Fill;
            tableLayoutPanel1.Location = new Point(0, 0);
            tableLayoutPanel1.Name = "tableLayoutPanel1";
            tableLayoutPanel1.RowCount = 1;
            tableLayoutPanel1.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            tableLayoutPanel1.RowStyles.Add(new RowStyle(SizeType.Absolute, 1039F));
            tableLayoutPanel1.Size = new Size(828, 353);
            tableLayoutPanel1.TabIndex = 0;
            // 
            // Panel_Left1
            // 
            Panel_Left1.Dock = DockStyle.Fill;
            Panel_Left1.Location = new Point(4, 4);
            Panel_Left1.Name = "Panel_Left1";
            Panel_Left1.Size = new Size(98, 345);
            Panel_Left1.TabIndex = 2;
            // 
            // Panel_Right2
            // 
            Panel_Right2.BackColor = Color.Transparent;
            Panel_Right2.Controls.Add(button1);
            Panel_Right2.Dock = DockStyle.Fill;
            Panel_Right2.Location = new Point(109, 4);
            Panel_Right2.Name = "Panel_Right2";
            Panel_Right2.Size = new Size(715, 345);
            Panel_Right2.TabIndex = 3;
            // 
            // button1
            // 
            button1.Location = new Point(851, 287);
            button1.Margin = new Padding(5);
            button1.Name = "button1";
            button1.Size = new Size(163, 50);
            button1.TabIndex = 0;
            button1.Text = "button1";
            button1.UseVisualStyleBackColor = true;
            button1.Click += Button1_Click;
            // 
            // toolStrip1
            // 
            toolStrip1.Dock = DockStyle.None;
            toolStrip1.GripStyle = ToolStripGripStyle.Hidden;
            toolStrip1.ImageScalingSize = new Size(32, 32);
            toolStrip1.Items.AddRange(new ToolStripItem[] { toolStripLabel1, toolStripButton1 });
            toolStrip1.Location = new Point(6, 0);
            toolStrip1.Name = "toolStrip1";
            toolStrip1.RenderMode = ToolStripRenderMode.System;
            toolStrip1.Size = new Size(83, 39);
            toolStrip1.TabIndex = 0;
            // 
            // toolStripLabel1
            // 
            toolStripLabel1.Name = "toolStripLabel1";
            toolStripLabel1.Size = new Size(44, 36);
            toolStripLabel1.Text = "工具栏";
            // 
            // toolStripButton1
            // 
            toolStripButton1.DisplayStyle = ToolStripItemDisplayStyle.Image;
            toolStripButton1.Image = (Image)resources.GetObject("toolStripButton1.Image");
            toolStripButton1.ImageTransparentColor = Color.Magenta;
            toolStripButton1.Name = "toolStripButton1";
            toolStripButton1.Size = new Size(36, 36);
            toolStripButton1.Text = "toolStripButton1";
            // 
            // 产品颜色管理ToolStripMenuItem
            // 
            产品颜色管理ToolStripMenuItem.Name = "产品颜色管理ToolStripMenuItem";
            产品颜色管理ToolStripMenuItem.Size = new Size(180, 22);
            产品颜色管理ToolStripMenuItem.Text = "产品颜色管理";
            产品颜色管理ToolStripMenuItem.Click += 产品颜色管理ToolStripMenuItem_Click;
            // 
            // MainFrm
            // 
            AutoScaleDimensions = new SizeF(8F, 19F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(828, 441);
            Controls.Add(toolStripContainer1);
            Controls.Add(statusStrip1);
            Controls.Add(menuStrip1);
            Font = new Font("微软雅黑", 10F, FontStyle.Regular, GraphicsUnit.Point, 134);
            Name = "MainFrm";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "MainFrm";
            statusStrip1.ResumeLayout(false);
            statusStrip1.PerformLayout();
            menuStrip1.ResumeLayout(false);
            menuStrip1.PerformLayout();
            toolStripContainer1.ContentPanel.ResumeLayout(false);
            toolStripContainer1.TopToolStripPanel.ResumeLayout(false);
            toolStripContainer1.TopToolStripPanel.PerformLayout();
            toolStripContainer1.ResumeLayout(false);
            toolStripContainer1.PerformLayout();
            tableLayoutPanel1.ResumeLayout(false);
            Panel_Right2.ResumeLayout(false);
            toolStrip1.ResumeLayout(false);
            toolStrip1.PerformLayout();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private StatusStrip statusStrip1;
        private ToolStripStatusLabel toolStripStatusLabel1;
        private MenuStrip menuStrip1;
        private ToolStripMenuItem toolStripMenuItem1;
        private ToolStripContainer toolStripContainer1;
        private ToolStrip toolStrip1;
        private ToolStripLabel toolStripLabel1;
        private TableLayoutPanel tableLayoutPanel1;
        private ToolStripButton toolStripButton1;
        private Panel Panel_Left1;
        private Panel Panel_Right2;
        private Button button1;
        private ToolStripMenuItem 编辑ToolStripMenuItem;
        private ToolStripMenuItem 销售ToolStripMenuItem;
        private ToolStripMenuItem CustomerSetting;
        private ToolStripMenuItem 采购ToolStripMenuItem;
        private ToolStripMenuItem 供应商管理ToolStripMenuItem;
        private ToolStripMenuItem excel字段映射管理ToolStripMenuItem;
        private ToolStripMenuItem 产品颜色管理ToolStripMenuItem;
    }
}