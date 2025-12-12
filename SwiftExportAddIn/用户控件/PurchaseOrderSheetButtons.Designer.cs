namespace SwiftExportAddIn.用户控件
{
    partial class PurchaseOrderSheetButtons
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
            this.Btn_PlanOrder = new System.Windows.Forms.Button();
            this.Btn_SaveToNewOrder = new System.Windows.Forms.Button();
            this.Btn_MergeToDb = new System.Windows.Forms.Button();
            this.Btn_ClearSheet = new System.Windows.Forms.Button();
            this.Btn_OrderForm = new System.Windows.Forms.Button();
            this.Btn_MatchCSku = new System.Windows.Forms.Button();
            this.Btn_InputPurchaseOrder = new System.Windows.Forms.Button();
            this.Btn_ExportExcelAndPDF = new System.Windows.Forms.Button();
            this.SuspendLayout();
            // 
            // Btn_PlanOrder
            // 
            this.Btn_PlanOrder.Location = new System.Drawing.Point(42, 565);
            this.Btn_PlanOrder.Name = "Btn_PlanOrder";
            this.Btn_PlanOrder.Size = new System.Drawing.Size(236, 105);
            this.Btn_PlanOrder.TabIndex = 1;
            this.Btn_PlanOrder.Text = "待采购计划单";
            this.Btn_PlanOrder.UseVisualStyleBackColor = true;
            // 
            // Btn_SaveToNewOrder
            // 
            this.Btn_SaveToNewOrder.Location = new System.Drawing.Point(345, 166);
            this.Btn_SaveToNewOrder.Name = "Btn_SaveToNewOrder";
            this.Btn_SaveToNewOrder.Size = new System.Drawing.Size(276, 105);
            this.Btn_SaveToNewOrder.TabIndex = 2;
            this.Btn_SaveToNewOrder.Text = "保存为新订单";
            this.Btn_SaveToNewOrder.UseVisualStyleBackColor = true;
            // 
            // Btn_MergeToDb
            // 
            this.Btn_MergeToDb.Location = new System.Drawing.Point(345, 295);
            this.Btn_MergeToDb.Name = "Btn_MergeToDb";
            this.Btn_MergeToDb.Size = new System.Drawing.Size(276, 105);
            this.Btn_MergeToDb.TabIndex = 3;
            this.Btn_MergeToDb.Text = "保存采购单至数据库";
            this.Btn_MergeToDb.UseVisualStyleBackColor = true;
            // 
            // Btn_ClearSheet
            // 
            this.Btn_ClearSheet.Location = new System.Drawing.Point(42, 295);
            this.Btn_ClearSheet.Name = "Btn_ClearSheet";
            this.Btn_ClearSheet.Size = new System.Drawing.Size(236, 105);
            this.Btn_ClearSheet.TabIndex = 4;
            this.Btn_ClearSheet.Text = "清空表单";
            this.Btn_ClearSheet.UseVisualStyleBackColor = true;
            // 
            // Btn_OrderForm
            // 
            this.Btn_OrderForm.Location = new System.Drawing.Point(345, 431);
            this.Btn_OrderForm.Name = "Btn_OrderForm";
            this.Btn_OrderForm.Size = new System.Drawing.Size(276, 105);
            this.Btn_OrderForm.TabIndex = 5;
            this.Btn_OrderForm.Text = "查询订单";
            this.Btn_OrderForm.UseVisualStyleBackColor = true;
            // 
            // Btn_MatchCSku
            // 
            this.Btn_MatchCSku.Location = new System.Drawing.Point(42, 166);
            this.Btn_MatchCSku.Name = "Btn_MatchCSku";
            this.Btn_MatchCSku.Size = new System.Drawing.Size(236, 105);
            this.Btn_MatchCSku.TabIndex = 6;
            this.Btn_MatchCSku.Text = "补充SKU";
            this.Btn_MatchCSku.UseVisualStyleBackColor = true;
            // 
            // Btn_InputPurchaseOrder
            // 
            this.Btn_InputPurchaseOrder.Location = new System.Drawing.Point(42, 24);
            this.Btn_InputPurchaseOrder.Name = "Btn_InputPurchaseOrder";
            this.Btn_InputPurchaseOrder.Size = new System.Drawing.Size(236, 105);
            this.Btn_InputPurchaseOrder.TabIndex = 7;
            this.Btn_InputPurchaseOrder.Text = "导入采购单";
            this.Btn_InputPurchaseOrder.UseVisualStyleBackColor = true;
            // 
            // Btn_ExportExcelAndPDF
            // 
            this.Btn_ExportExcelAndPDF.Location = new System.Drawing.Point(42, 710);
            this.Btn_ExportExcelAndPDF.Name = "Btn_ExportExcelAndPDF";
            this.Btn_ExportExcelAndPDF.Size = new System.Drawing.Size(236, 105);
            this.Btn_ExportExcelAndPDF.TabIndex = 4;
            this.Btn_ExportExcelAndPDF.Text = "导出为Excel和PDF";
            this.Btn_ExportExcelAndPDF.UseVisualStyleBackColor = true;
            // 
            // PurchaseOrderSheetButtons
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(14F, 31F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.Btn_PlanOrder);
            this.Controls.Add(this.Btn_SaveToNewOrder);
            this.Controls.Add(this.Btn_MergeToDb);
            this.Controls.Add(this.Btn_ExportExcelAndPDF);
            this.Controls.Add(this.Btn_ClearSheet);
            this.Controls.Add(this.Btn_OrderForm);
            this.Controls.Add(this.Btn_MatchCSku);
            this.Controls.Add(this.Btn_InputPurchaseOrder);
            this.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.Name = "PurchaseOrderSheetButtons";
            this.Size = new System.Drawing.Size(679, 930);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Button Btn_PlanOrder;
        private System.Windows.Forms.Button Btn_SaveToNewOrder;
        private System.Windows.Forms.Button Btn_MergeToDb;
        private System.Windows.Forms.Button Btn_ClearSheet;
        private System.Windows.Forms.Button Btn_OrderForm;
        private System.Windows.Forms.Button Btn_MatchCSku;
        private System.Windows.Forms.Button Btn_InputPurchaseOrder;
        private System.Windows.Forms.Button Btn_ExportExcelAndPDF;
    }
}
