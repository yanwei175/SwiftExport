using System;
using System.Windows.Forms;

public class ExcelWindowWrapper : IWin32Window
{
    private readonly IntPtr _hwnd;

    // 传入 Excel 主窗口的句柄
    public ExcelWindowWrapper(IntPtr handle)
    {
        _hwnd = handle;
    }

    public IntPtr Handle
    {
        get { return _hwnd; }
    }
}