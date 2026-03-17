namespace SwiftExport.VSTO.SwiftExportTaskPane
{
    /// <summary>
    /// 全局侧边栏唯一标识符枚举，绝对的强类型限定！
    /// </summary>
    public enum TaskPaneId
    {
        //BarcodePane,
        //FileOrderPane,

    }
    /// <summary>
    /// 集中注册中心：专门负责向系统注入各个模块的设计图纸
    /// </summary>
    public static class TaskPaneRegistrar
    {
        public static void RegisterAllPanes()
        {
            //TaskPaneManager.RegisterConfig(new TaskPaneConfig
            //{
            //    PaneId = TaskPaneId.BarcodePane,
            //    Title = "条码生成器",
            //    DockPosition = MsoCTPDockPosition.msoCTPDockPositionRight,
            //    DefaultWidth = 800,
            //    WpfControlCreator = () => new BarcodePaneControl()//注入自己写的WPF界面
            //});
            //TaskPaneManager.RegisterConfig(new TaskPaneConfig
            //{
            //    PaneId = TaskPaneId.FileOrderPane,
            //    Title = "文件提取",
            //    DockPosition = MsoCTPDockPosition.msoCTPDockPositionRight,
            //    DefaultWidth = 600,
            //    WpfControlCreator = () => new FileOrderPaneControl()//注入自己写的WPF界面
            //});

        }
    }
}
