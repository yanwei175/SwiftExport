namespace SwiftExport.UiWinForm
{
    partial class LoginFrm
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
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
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            txt_UserName = new TextBox();
            txt_passwd = new TextBox();
            SignIn = new Button();
            SignUp = new Button();
            tableLayoutPanelInnet = new TableLayoutPanel();
            Panel_pswd = new Panel();
            lbl_Pswd = new Label();
            PanelUsers = new Panel();
            Lbl_Users = new Label();
            tableLayoutPanelOutlet = new TableLayoutPanel();
            tableLayoutPanelInnet.SuspendLayout();
            Panel_pswd.SuspendLayout();
            PanelUsers.SuspendLayout();
            tableLayoutPanelOutlet.SuspendLayout();
            SuspendLayout();
            // 
            // txt_UserName
            // 
            txt_UserName.Dock = DockStyle.Top;
            txt_UserName.Location = new Point(0, 17);
            txt_UserName.Margin = new Padding(4);
            txt_UserName.Name = "txt_UserName";
            txt_UserName.Size = new Size(249, 23);
            txt_UserName.TabIndex = 0;
            // 
            // txt_passwd
            // 
            txt_passwd.Dock = DockStyle.Top;
            txt_passwd.Location = new Point(0, 17);
            txt_passwd.Margin = new Padding(4);
            txt_passwd.Name = "txt_passwd";
            txt_passwd.Size = new Size(249, 23);
            txt_passwd.TabIndex = 0;
            // 
            // SignIn
            // 
            SignIn.Dock = DockStyle.Fill;
            SignIn.Font = new Font("HarmonyOS Sans", 9F, FontStyle.Bold, GraphicsUnit.Point, 0);
            SignIn.Location = new Point(4, 106);
            SignIn.Margin = new Padding(4);
            SignIn.Name = "SignIn";
            SignIn.Size = new Size(247, 43);
            SignIn.TabIndex = 1;
            SignIn.Text = "登录";
            SignIn.UseVisualStyleBackColor = true;
            SignIn.Click += SignIn_Click;
            // 
            // SignUp
            // 
            SignUp.Dock = DockStyle.Fill;
            SignUp.Font = new Font("HarmonyOS Sans", 9F, FontStyle.Bold, GraphicsUnit.Point, 0);
            SignUp.Location = new Point(4, 157);
            SignUp.Margin = new Padding(4);
            SignUp.Name = "SignUp";
            SignUp.Size = new Size(247, 43);
            SignUp.TabIndex = 1;
            SignUp.Text = "注册";
            SignUp.UseVisualStyleBackColor = true;
            SignUp.Click += SignUp_Click;
            // 
            // tableLayoutPanelInnet
            // 
            tableLayoutPanelInnet.ColumnCount = 1;
            tableLayoutPanelInnet.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            tableLayoutPanelInnet.Controls.Add(Panel_pswd, 0, 1);
            tableLayoutPanelInnet.Controls.Add(SignUp, 0, 3);
            tableLayoutPanelInnet.Controls.Add(PanelUsers, 0, 0);
            tableLayoutPanelInnet.Controls.Add(SignIn, 0, 2);
            tableLayoutPanelInnet.Dock = DockStyle.Fill;
            tableLayoutPanelInnet.Location = new Point(90, 48);
            tableLayoutPanelInnet.Name = "tableLayoutPanelInnet";
            tableLayoutPanelInnet.RowCount = 4;
            tableLayoutPanelInnet.RowStyles.Add(new RowStyle(SizeType.Percent, 25F));
            tableLayoutPanelInnet.RowStyles.Add(new RowStyle(SizeType.Percent, 25F));
            tableLayoutPanelInnet.RowStyles.Add(new RowStyle(SizeType.Percent, 25F));
            tableLayoutPanelInnet.RowStyles.Add(new RowStyle(SizeType.Percent, 25F));
            tableLayoutPanelInnet.Size = new Size(255, 204);
            tableLayoutPanelInnet.TabIndex = 2;
            // 
            // Panel_pswd
            // 
            Panel_pswd.Controls.Add(txt_passwd);
            Panel_pswd.Controls.Add(lbl_Pswd);
            Panel_pswd.Dock = DockStyle.Fill;
            Panel_pswd.Location = new Point(3, 54);
            Panel_pswd.Name = "Panel_pswd";
            Panel_pswd.Size = new Size(249, 45);
            Panel_pswd.TabIndex = 5;
            // 
            // lbl_Pswd
            // 
            lbl_Pswd.AutoSize = true;
            lbl_Pswd.Dock = DockStyle.Top;
            lbl_Pswd.Location = new Point(0, 0);
            lbl_Pswd.Name = "lbl_Pswd";
            lbl_Pswd.Size = new Size(32, 17);
            lbl_Pswd.TabIndex = 1;
            lbl_Pswd.Text = "密码";
            // 
            // PanelUsers
            // 
            PanelUsers.Controls.Add(txt_UserName);
            PanelUsers.Controls.Add(Lbl_Users);
            PanelUsers.Dock = DockStyle.Fill;
            PanelUsers.Location = new Point(3, 3);
            PanelUsers.Name = "PanelUsers";
            PanelUsers.Size = new Size(249, 45);
            PanelUsers.TabIndex = 4;
            // 
            // Lbl_Users
            // 
            Lbl_Users.AutoSize = true;
            Lbl_Users.Dock = DockStyle.Top;
            Lbl_Users.Location = new Point(0, 0);
            Lbl_Users.Name = "Lbl_Users";
            Lbl_Users.Size = new Size(44, 17);
            Lbl_Users.TabIndex = 1;
            Lbl_Users.Text = "用户名";
            // 
            // tableLayoutPanelOutlet
            // 
            tableLayoutPanelOutlet.ColumnCount = 3;
            tableLayoutPanelOutlet.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20F));
            tableLayoutPanelOutlet.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60F));
            tableLayoutPanelOutlet.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20F));
            tableLayoutPanelOutlet.Controls.Add(tableLayoutPanelInnet, 1, 1);
            tableLayoutPanelOutlet.Dock = DockStyle.Fill;
            tableLayoutPanelOutlet.Location = new Point(0, 0);
            tableLayoutPanelOutlet.Name = "tableLayoutPanelOutlet";
            tableLayoutPanelOutlet.RowCount = 3;
            tableLayoutPanelOutlet.RowStyles.Add(new RowStyle(SizeType.Percent, 15F));
            tableLayoutPanelOutlet.RowStyles.Add(new RowStyle(SizeType.Percent, 70F));
            tableLayoutPanelOutlet.RowStyles.Add(new RowStyle(SizeType.Percent, 15F));
            tableLayoutPanelOutlet.Size = new Size(435, 301);
            tableLayoutPanelOutlet.TabIndex = 2;
            // 
            // LoginFrm
            // 
            AcceptButton = SignIn;
            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(435, 301);
            Controls.Add(tableLayoutPanelOutlet);
            Font = new Font("微软雅黑", 9F, FontStyle.Regular, GraphicsUnit.Point, 0);
            Margin = new Padding(4);
            Name = "LoginFrm";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "UserLogin";
            tableLayoutPanelInnet.ResumeLayout(false);
            Panel_pswd.ResumeLayout(false);
            Panel_pswd.PerformLayout();
            PanelUsers.ResumeLayout(false);
            PanelUsers.PerformLayout();
            tableLayoutPanelOutlet.ResumeLayout(false);
            ResumeLayout(false);
        }

        #endregion
        private System.Windows.Forms.TextBox txt_UserName;
        private System.Windows.Forms.TextBox txt_passwd;
        private System.Windows.Forms.Button SignIn;
        private System.Windows.Forms.Button SignUp;
        private System.Windows.Forms.TableLayoutPanel tableLayoutPanelInnet;
        private System.Windows.Forms.TableLayoutPanel tableLayoutPanelOutlet;
        private Panel PanelUsers;
        private Label Lbl_Users;
        private Panel Panel_pswd;
        private Label lbl_Pswd;
    }
}
