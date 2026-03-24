namespace SwiftExport.View.FW.Mvvm
{
    /// <summary>
    /// UI 层实体数据的追踪状态
    /// </summary>
    public enum ModelState
    {
        /// <summary>
        /// 未发生任何改变的原始数据
        /// </summary>
        Unchanged,

        /// <summary>
        /// 用户在前端新增，尚未提交到后端的数据
        /// </summary>
        Added,

        /// <summary>
        /// 用户在前端修改过，尚未提交到后端的数据
        /// </summary>
        Modified,

        /// <summary>
        /// 用户在前端标记删除，尚未提交到后端的数据
        /// </summary>
        Deleted
    }
}

