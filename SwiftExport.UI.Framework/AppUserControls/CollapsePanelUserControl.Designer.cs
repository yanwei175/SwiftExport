namespace SwiftExport.UI.Framework.AppUserControls
{
    partial class CollapsePanelUserControl
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
            this.CollapsePanel = new System.Windows.Forms.SplitContainer();
            this.CtrlVisiblePane2 = new System.Windows.Forms.Label();
            ((System.ComponentModel.ISupportInitialize)(this.CollapsePanel)).BeginInit();
            this.CollapsePanel.Panel1.SuspendLayout();
            this.CollapsePanel.SuspendLayout();
            this.SuspendLayout();
            // 
            // CollapsePanel
            // 
            this.CollapsePanel.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.CollapsePanel.Location = new System.Drawing.Point(0, 0);
            this.CollapsePanel.Margin = new System.Windows.Forms.Padding(4);
            this.CollapsePanel.Name = "CollapsePanel";
            this.CollapsePanel.Orientation = System.Windows.Forms.Orientation.Horizontal;
            // 
            // CollapsePanel.Panel1
            // 
            this.CollapsePanel.Panel1.Controls.Add(this.CtrlVisiblePane2);
            this.CollapsePanel.Panel1.Cursor = System.Windows.Forms.Cursors.Arrow;
            this.CollapsePanel.Panel1MinSize = 47;
            this.CollapsePanel.Size = new System.Drawing.Size(213, 571);
            this.CollapsePanel.SplitterDistance = 47;
            this.CollapsePanel.SplitterWidth = 5;
            this.CollapsePanel.TabIndex = 0;
            // 
            // CtrlVisiblePane2
            // 
            this.CtrlVisiblePane2.BackColor = System.Drawing.Color.Transparent;
            this.CtrlVisiblePane2.Cursor = System.Windows.Forms.Cursors.Hand;
            this.CtrlVisiblePane2.Dock = System.Windows.Forms.DockStyle.Right;
            this.CtrlVisiblePane2.Location = new System.Drawing.Point(49, 0);
            this.CtrlVisiblePane2.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.CtrlVisiblePane2.MinimumSize = new System.Drawing.Size(162, 55);
            this.CtrlVisiblePane2.Name = "CtrlVisiblePane2";
            this.CtrlVisiblePane2.Size = new System.Drawing.Size(162, 55);
            this.CtrlVisiblePane2.TabIndex = 14;
            this.CtrlVisiblePane2.Text = "▼ 隐藏";
            this.CtrlVisiblePane2.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            this.CtrlVisiblePane2.Click += new System.EventHandler(this.CtrlVisiblePane2_Click);
            // 
            // CollapsePanelUserControl
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(13F, 28F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.CollapsePanel);
            this.Font = new System.Drawing.Font("Microsoft YaHei UI", 8F);
            this.Margin = new System.Windows.Forms.Padding(4);
            this.MinimumSize = new System.Drawing.Size(109, 117);
            this.Name = "CollapsePanelUserControl";
            this.Size = new System.Drawing.Size(279, 703);
            this.CollapsePanel.Panel1.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.CollapsePanel)).EndInit();
            this.CollapsePanel.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.SplitContainer CollapsePanel;
        private System.Windows.Forms.Label CtrlVisiblePane2;
    }
}
