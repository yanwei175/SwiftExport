namespace SwiftExport.UiKongJianFrameWork.AppUserControls
{
    partial class DataSplitUserControl
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
            this.TSDataStatus = new System.Windows.Forms.ToolStrip();
            this.TSLblDataCount = new System.Windows.Forms.ToolStripLabel();
            this.TSLblPageInfo = new System.Windows.Forms.ToolStripLabel();
            this.TSlblPgUnit = new System.Windows.Forms.ToolStripLabel();
            this.TscmbPgIndex = new System.Windows.Forms.ToolStripComboBox();
            this.TslblGotoPg = new System.Windows.Forms.ToolStripLabel();
            this.toolStripSeparator2 = new System.Windows.Forms.ToolStripSeparator();
            this.TsbtnLstPg = new System.Windows.Forms.ToolStripButton();
            this.TsbtnNextPg = new System.Windows.Forms.ToolStripButton();
            this.TsbtnPrePg = new System.Windows.Forms.ToolStripButton();
            this.TSbtnFstPg = new System.Windows.Forms.ToolStripButton();
            this.TsLbl_每页显示 = new System.Windows.Forms.ToolStripLabel();
            this.TsCmb_每页条数 = new System.Windows.Forms.ToolStripComboBox();
            this.TSDataStatus.SuspendLayout();
            this.SuspendLayout();
            // 
            // TSDataStatus
            // 
            this.TSDataStatus.BackColor = System.Drawing.SystemColors.Window;
            this.TSDataStatus.Dock = System.Windows.Forms.DockStyle.Fill;
            this.TSDataStatus.ImageScalingSize = new System.Drawing.Size(32, 32);
            this.TSDataStatus.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.TSLblDataCount,
            this.TSLblPageInfo,
            this.TSlblPgUnit,
            this.TscmbPgIndex,
            this.TslblGotoPg,
            this.toolStripSeparator2,
            this.TsbtnLstPg,
            this.TsbtnNextPg,
            this.TsbtnPrePg,
            this.TSbtnFstPg,
            this.TsLbl_每页显示,
            this.TsCmb_每页条数});
            this.TSDataStatus.Location = new System.Drawing.Point(0, 0);
            this.TSDataStatus.Name = "TSDataStatus";
            this.TSDataStatus.RenderMode = System.Windows.Forms.ToolStripRenderMode.Professional;
            this.TSDataStatus.Size = new System.Drawing.Size(1742, 66);
            this.TSDataStatus.TabIndex = 8;
            this.TSDataStatus.Text = "toolStrip2";
            // 
            // TSLblDataCount
            // 
            this.TSLblDataCount.Name = "TSLblDataCount";
            this.TSLblDataCount.Size = new System.Drawing.Size(130, 60);
            this.TSLblDataCount.Text = "总记录  条.";
            // 
            // TSLblPageInfo
            // 
            this.TSLblPageInfo.Name = "TSLblPageInfo";
            this.TSLblPageInfo.Size = new System.Drawing.Size(147, 60);
            this.TSLblPageInfo.Text = "当前第  / 页.";
            // 
            // TSlblPgUnit
            // 
            this.TSlblPgUnit.Alignment = System.Windows.Forms.ToolStripItemAlignment.Right;
            this.TSlblPgUnit.Name = "TSlblPgUnit";
            this.TSlblPgUnit.Size = new System.Drawing.Size(38, 60);
            this.TSlblPgUnit.Text = "页";
            // 
            // TscmbPgIndex
            // 
            this.TscmbPgIndex.Alignment = System.Windows.Forms.ToolStripItemAlignment.Right;
            this.TscmbPgIndex.Name = "TscmbPgIndex";
            this.TscmbPgIndex.Size = new System.Drawing.Size(140, 66);
            this.TscmbPgIndex.SelectedIndexChanged += new System.EventHandler(this.TscmbPgIndex_SelectedIndexChanged);
            // 
            // TslblGotoPg
            // 
            this.TslblGotoPg.Alignment = System.Windows.Forms.ToolStripItemAlignment.Right;
            this.TslblGotoPg.Name = "TslblGotoPg";
            this.TslblGotoPg.Size = new System.Drawing.Size(62, 60);
            this.TslblGotoPg.Text = "前往";
            // 
            // toolStripSeparator2
            // 
            this.toolStripSeparator2.Name = "toolStripSeparator2";
            this.toolStripSeparator2.Size = new System.Drawing.Size(6, 66);
            // 
            // TsbtnLstPg
            // 
            this.TsbtnLstPg.Alignment = System.Windows.Forms.ToolStripItemAlignment.Right;
            this.TsbtnLstPg.AutoSize = false;
            this.TsbtnLstPg.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.TsbtnLstPg.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.TsbtnLstPg.Name = "TsbtnLstPg";
            this.TsbtnLstPg.Size = new System.Drawing.Size(100, 60);
            this.TsbtnLstPg.Text = "尾页";
            this.TsbtnLstPg.Click += new System.EventHandler(this.TsbtnLstPg_Click);
            // 
            // TsbtnNextPg
            // 
            this.TsbtnNextPg.Alignment = System.Windows.Forms.ToolStripItemAlignment.Right;
            this.TsbtnNextPg.AutoSize = false;
            this.TsbtnNextPg.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.TsbtnNextPg.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.TsbtnNextPg.Name = "TsbtnNextPg";
            this.TsbtnNextPg.Size = new System.Drawing.Size(100, 60);
            this.TsbtnNextPg.Text = "下页";
            this.TsbtnNextPg.Click += new System.EventHandler(this.TsbtnNextPg_Click);
            // 
            // TsbtnPrePg
            // 
            this.TsbtnPrePg.Alignment = System.Windows.Forms.ToolStripItemAlignment.Right;
            this.TsbtnPrePg.AutoSize = false;
            this.TsbtnPrePg.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.TsbtnPrePg.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.TsbtnPrePg.Name = "TsbtnPrePg";
            this.TsbtnPrePg.Size = new System.Drawing.Size(100, 60);
            this.TsbtnPrePg.Text = "上页";
            this.TsbtnPrePg.Click += new System.EventHandler(this.TsbtnPrePg_Click);
            // 
            // TSbtnFstPg
            // 
            this.TSbtnFstPg.Alignment = System.Windows.Forms.ToolStripItemAlignment.Right;
            this.TSbtnFstPg.AutoSize = false;
            this.TSbtnFstPg.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.TSbtnFstPg.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.TSbtnFstPg.Name = "TSbtnFstPg";
            this.TSbtnFstPg.Size = new System.Drawing.Size(100, 60);
            this.TSbtnFstPg.Text = "首页";
            this.TSbtnFstPg.Click += new System.EventHandler(this.TSbtnFstPg_Click);
            // 
            // TsLbl_每页显示
            // 
            this.TsLbl_每页显示.Name = "TsLbl_每页显示";
            this.TsLbl_每页显示.Size = new System.Drawing.Size(116, 60);
            this.TsLbl_每页显示.Text = "每页条数:";
            // 
            // TsCmb_每页条数
            // 
            this.TsCmb_每页条数.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.TsCmb_每页条数.Items.AddRange(new object[] {
            "10",
            "20",
            "50",
            "100",
            "150",
            "200"});
            this.TsCmb_每页条数.Name = "TsCmb_每页条数";
            this.TsCmb_每页条数.Size = new System.Drawing.Size(121, 66);
            this.TsCmb_每页条数.SelectedIndexChanged += new System.EventHandler(this.TsCmb_每页条数_SelectedIndexChanged);
            // 
            // DataSplitUserControl
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(19F, 41F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.TSDataStatus);
            this.Font = new System.Drawing.Font("微软雅黑", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.Margin = new System.Windows.Forms.Padding(4);
            this.Name = "DataSplitUserControl";
            this.Size = new System.Drawing.Size(1742, 66);
            this.TSDataStatus.ResumeLayout(false);
            this.TSDataStatus.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.ToolStrip TSDataStatus;
        private System.Windows.Forms.ToolStripLabel TSLblDataCount;
        private System.Windows.Forms.ToolStripLabel TSLblPageInfo;
        private System.Windows.Forms.ToolStripLabel TSlblPgUnit;
        private System.Windows.Forms.ToolStripComboBox TscmbPgIndex;
        private System.Windows.Forms.ToolStripLabel TslblGotoPg;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator2;
        private System.Windows.Forms.ToolStripButton TsbtnLstPg;
        private System.Windows.Forms.ToolStripButton TsbtnNextPg;
        private System.Windows.Forms.ToolStripButton TsbtnPrePg;
        private System.Windows.Forms.ToolStripButton TSbtnFstPg;
        private System.Windows.Forms.ToolStripLabel TsLbl_每页显示;
        private System.Windows.Forms.ToolStripComboBox TsCmb_每页条数;
    }
}
