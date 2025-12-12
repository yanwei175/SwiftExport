namespace SwiftExport.UiKongJianFrameWork.AppUserControls
{
    partial class UserDataGrid
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(UserDataGrid));
            this.TSDataStatus = new System.Windows.Forms.ToolStrip();
            this.TSLblDataCount = new System.Windows.Forms.ToolStripLabel();
            this.TSLblPageInfo = new System.Windows.Forms.ToolStripLabel();
            this.TSlblPgUnit = new System.Windows.Forms.ToolStripLabel();
            this.TscmbPgIndex = new System.Windows.Forms.ToolStripComboBox();
            this.TslblGotoPg = new System.Windows.Forms.ToolStripLabel();
            this.FenGeFu = new System.Windows.Forms.ToolStripSeparator();
            this.TsbtnLstPg = new System.Windows.Forms.ToolStripButton();
            this.TsbtnNextPg = new System.Windows.Forms.ToolStripButton();
            this.TsbtnPrePg = new System.Windows.Forms.ToolStripButton();
            this.TSbtnFstPg = new System.Windows.Forms.ToolStripButton();
            this.AllSptContainer = new System.Windows.Forms.SplitContainer();
            this.Data = new System.Windows.Forms.SplitContainer();
            this.dataGridView1 = new System.Windows.Forms.DataGridView();
            this.button1 = new System.Windows.Forms.Button();
            this.TSDataStatus.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.AllSptContainer)).BeginInit();
            this.AllSptContainer.Panel1.SuspendLayout();
            this.AllSptContainer.Panel2.SuspendLayout();
            this.AllSptContainer.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.Data)).BeginInit();
            this.Data.Panel1.SuspendLayout();
            this.Data.Panel2.SuspendLayout();
            this.Data.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.dataGridView1)).BeginInit();
            this.SuspendLayout();
            // 
            // TSDataStatus
            // 
            this.TSDataStatus.Dock = System.Windows.Forms.DockStyle.Fill;
            this.TSDataStatus.ImageScalingSize = new System.Drawing.Size(32, 32);
            this.TSDataStatus.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.TSLblDataCount,
            this.TSLblPageInfo,
            this.TSlblPgUnit,
            this.TscmbPgIndex,
            this.TslblGotoPg,
            this.FenGeFu,
            this.TsbtnLstPg,
            this.TsbtnNextPg,
            this.TsbtnPrePg,
            this.TSbtnFstPg});
            this.TSDataStatus.Location = new System.Drawing.Point(0, 0);
            this.TSDataStatus.Name = "TSDataStatus";
            this.TSDataStatus.RenderMode = System.Windows.Forms.ToolStripRenderMode.Professional;
            this.TSDataStatus.Size = new System.Drawing.Size(2074, 66);
            this.TSDataStatus.TabIndex = 9;
            this.TSDataStatus.Text = "toolStrip2";
            // 
            // TSLblDataCount
            // 
            this.TSLblDataCount.Name = "TSLblDataCount";
            this.TSLblDataCount.Size = new System.Drawing.Size(96, 61);
            this.TSLblDataCount.Text = "总记录  条.";
            // 
            // TSLblPageInfo
            // 
            this.TSLblPageInfo.Name = "TSLblPageInfo";
            this.TSLblPageInfo.Size = new System.Drawing.Size(109, 61);
            this.TSLblPageInfo.Text = "当前第  / 页.";
            // 
            // TSlblPgUnit
            // 
            this.TSlblPgUnit.Alignment = System.Windows.Forms.ToolStripItemAlignment.Right;
            this.TSlblPgUnit.Name = "TSlblPgUnit";
            this.TSlblPgUnit.Size = new System.Drawing.Size(28, 61);
            this.TSlblPgUnit.Text = "页";
            // 
            // TscmbPgIndex
            // 
            this.TscmbPgIndex.Alignment = System.Windows.Forms.ToolStripItemAlignment.Right;
            this.TscmbPgIndex.Name = "TscmbPgIndex";
            this.TscmbPgIndex.Size = new System.Drawing.Size(111, 66);
            // 
            // TslblGotoPg
            // 
            this.TslblGotoPg.Alignment = System.Windows.Forms.ToolStripItemAlignment.Right;
            this.TslblGotoPg.Name = "TslblGotoPg";
            this.TslblGotoPg.Size = new System.Drawing.Size(46, 61);
            this.TslblGotoPg.Text = "前往";
            // 
            // FenGeFu
            // 
            this.FenGeFu.Name = "FenGeFu";
            this.FenGeFu.Size = new System.Drawing.Size(6, 66);
            // 
            // TsbtnLstPg
            // 
            this.TsbtnLstPg.Alignment = System.Windows.Forms.ToolStripItemAlignment.Right;
            this.TsbtnLstPg.Image = ((System.Drawing.Image)(resources.GetObject("TsbtnLstPg.Image")));
            this.TsbtnLstPg.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.TsbtnLstPg.Name = "TsbtnLstPg";
            this.TsbtnLstPg.Size = new System.Drawing.Size(82, 61);
            this.TsbtnLstPg.Text = "尾页";
            // 
            // TsbtnNextPg
            // 
            this.TsbtnNextPg.Alignment = System.Windows.Forms.ToolStripItemAlignment.Right;
            this.TsbtnNextPg.Image = ((System.Drawing.Image)(resources.GetObject("TsbtnNextPg.Image")));
            this.TsbtnNextPg.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.TsbtnNextPg.Name = "TsbtnNextPg";
            this.TsbtnNextPg.Size = new System.Drawing.Size(82, 61);
            this.TsbtnNextPg.Text = "下页";
            // 
            // TsbtnPrePg
            // 
            this.TsbtnPrePg.Alignment = System.Windows.Forms.ToolStripItemAlignment.Right;
            this.TsbtnPrePg.Image = ((System.Drawing.Image)(resources.GetObject("TsbtnPrePg.Image")));
            this.TsbtnPrePg.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.TsbtnPrePg.Name = "TsbtnPrePg";
            this.TsbtnPrePg.Size = new System.Drawing.Size(82, 61);
            this.TsbtnPrePg.Text = "上页";
            // 
            // TSbtnFstPg
            // 
            this.TSbtnFstPg.Alignment = System.Windows.Forms.ToolStripItemAlignment.Right;
            this.TSbtnFstPg.Image = ((System.Drawing.Image)(resources.GetObject("TSbtnFstPg.Image")));
            this.TSbtnFstPg.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.TSbtnFstPg.Name = "TSbtnFstPg";
            this.TSbtnFstPg.Size = new System.Drawing.Size(82, 61);
            this.TSbtnFstPg.Text = "首页";
            // 
            // AllSptContainer
            // 
            this.AllSptContainer.Dock = System.Windows.Forms.DockStyle.Fill;
            this.AllSptContainer.Location = new System.Drawing.Point(0, 0);
            this.AllSptContainer.Name = "AllSptContainer";
            this.AllSptContainer.Orientation = System.Windows.Forms.Orientation.Horizontal;
            // 
            // AllSptContainer.Panel1
            // 
            this.AllSptContainer.Panel1.Controls.Add(this.button1);
            // 
            // AllSptContainer.Panel2
            // 
            this.AllSptContainer.Panel2.Controls.Add(this.Data);
            this.AllSptContainer.Size = new System.Drawing.Size(2074, 1077);
            this.AllSptContainer.TabIndex = 10;
            // 
            // Data
            // 
            this.Data.Dock = System.Windows.Forms.DockStyle.Fill;
            this.Data.Location = new System.Drawing.Point(0, 0);
            this.Data.Name = "Data";
            this.Data.Orientation = System.Windows.Forms.Orientation.Horizontal;
            // 
            // Data.Panel1
            // 
            this.Data.Panel1.Controls.Add(this.dataGridView1);
            // 
            // Data.Panel2
            // 
            this.Data.Panel2.Controls.Add(this.TSDataStatus);
            this.Data.Size = new System.Drawing.Size(2074, 1023);
            this.Data.SplitterDistance = 953;
            this.Data.TabIndex = 0;
            // 
            // dataGridView1
            // 
            this.dataGridView1.BackgroundColor = System.Drawing.Color.WhiteSmoke;
            this.dataGridView1.ColumnHeadersBorderStyle = System.Windows.Forms.DataGridViewHeaderBorderStyle.Single;
            this.dataGridView1.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.dataGridView1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.dataGridView1.Location = new System.Drawing.Point(0, 0);
            this.dataGridView1.Name = "dataGridView1";
            this.dataGridView1.RowHeadersWidth = 62;
            this.dataGridView1.RowTemplate.Height = 37;
            this.dataGridView1.Size = new System.Drawing.Size(2074, 953);
            this.dataGridView1.TabIndex = 0;
            // 
            // button1
            // 
            this.button1.BackColor = System.Drawing.Color.WhiteSmoke;
            this.button1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.button1.Location = new System.Drawing.Point(0, 0);
            this.button1.Name = "button1";
            this.button1.Size = new System.Drawing.Size(2074, 50);
            this.button1.TabIndex = 0;
            this.button1.UseVisualStyleBackColor = false;
            // 
            // UserDataGrid
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(11F, 24F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.AllSptContainer);
            this.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.Name = "UserDataGrid";
            this.Size = new System.Drawing.Size(2074, 1077);
            this.TSDataStatus.ResumeLayout(false);
            this.TSDataStatus.PerformLayout();
            this.AllSptContainer.Panel1.ResumeLayout(false);
            this.AllSptContainer.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.AllSptContainer)).EndInit();
            this.AllSptContainer.ResumeLayout(false);
            this.Data.Panel1.ResumeLayout(false);
            this.Data.Panel2.ResumeLayout(false);
            this.Data.Panel2.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.Data)).EndInit();
            this.Data.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.dataGridView1)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.ToolStrip TSDataStatus;
        private System.Windows.Forms.ToolStripLabel TSLblDataCount;
        private System.Windows.Forms.ToolStripLabel TSLblPageInfo;
        private System.Windows.Forms.ToolStripLabel TSlblPgUnit;
        private System.Windows.Forms.ToolStripComboBox TscmbPgIndex;
        private System.Windows.Forms.ToolStripLabel TslblGotoPg;
        private System.Windows.Forms.ToolStripSeparator FenGeFu;
        private System.Windows.Forms.ToolStripButton TsbtnLstPg;
        private System.Windows.Forms.ToolStripButton TsbtnNextPg;
        private System.Windows.Forms.ToolStripButton TsbtnPrePg;
        private System.Windows.Forms.ToolStripButton TSbtnFstPg;
        private System.Windows.Forms.SplitContainer AllSptContainer;
        private System.Windows.Forms.SplitContainer Data;
        private System.Windows.Forms.DataGridView dataGridView1;
        private System.Windows.Forms.Button button1;
    }
}
