namespace SwiftExport.UiKongJianFrameWork.AppUserControls
{
    partial class CustomerListUserControl
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
            this.Cbm_CustomerList = new System.Windows.Forms.ComboBox();
            this.customersBindingSource = new System.Windows.Forms.BindingSource(this.components);
            ((System.ComponentModel.ISupportInitialize)(this.customersBindingSource)).BeginInit();
            this.SuspendLayout();
            // 
            // Cbm_CustomerList
            // 
            this.Cbm_CustomerList.DataSource = this.customersBindingSource;
            this.Cbm_CustomerList.DisplayMember = "CustomerCode";
            this.Cbm_CustomerList.Dock = System.Windows.Forms.DockStyle.Fill;
            this.Cbm_CustomerList.FormattingEnabled = true;
            this.Cbm_CustomerList.ItemHeight = 28;
            this.Cbm_CustomerList.Location = new System.Drawing.Point(0, 0);
            this.Cbm_CustomerList.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.Cbm_CustomerList.Name = "Cbm_CustomerList";
            this.Cbm_CustomerList.Size = new System.Drawing.Size(268, 36);
            this.Cbm_CustomerList.TabIndex = 0;
            this.Cbm_CustomerList.ValueMember = "ID";
            // 
            // customersBindingSource
            // 
            this.customersBindingSource.DataSource = typeof(SwiftExport.Core.Entities.Customers);
            // 
            // CustomerListUserControl
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(13F, 28F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.SystemColors.Window;
            this.Controls.Add(this.Cbm_CustomerList);
            this.Font = new System.Drawing.Font("微软雅黑", 7.875F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.Name = "CustomerListUserControl";
            this.Size = new System.Drawing.Size(268, 35);
            ((System.ComponentModel.ISupportInitialize)(this.customersBindingSource)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.ComboBox Cbm_CustomerList;
        private System.Windows.Forms.BindingSource customersBindingSource;
    }
}
