namespace SwiftExport.Contracts.Apis.BaseApi
{
    /// <summary>
    /// 标准化 RESTful 动态路由生成器
    /// 只要后端遵循 api/{实体名}Action 的规范，前端就无需再手写任何路由字符串。
    /// </summary>
    public static class ApiRouteBuilder
    {
        // ================= 基础 RESTful 路由 =================

        // 用于: GET (获取列表), POST (新建单体)
        public static string Resource(string entityName) => $"api/{entityName}";

        // 用于: GET (获取单个), PUT (更新单体), DELETE (删除单体)
        public static string Resource(string entityName, object id) => $"api/{entityName}/{id}";

        // ================= 批量操作 (REST 的灰色地带) =================
        // REST 本质上是对单个资源的操作。对于批量操作，业界通常的做法是在资源后加上特定的“子操作”或动词
        public static string GetAll(string entityName) => $"api/{entityName}/all";

        // 用于: POST (批量保存/创建/更新/删除的混合包)
        // 专门对接 BatchRequestGeneric 混合数据包
        public static string BatchSave(string entityName) => $"api/{entityName}/batch-save";


        // 纯批量创建: POST api/{entity}/batch-create
        public static string BatchCreate(string entityName) => $"api/{entityName}/batch-create";

        // 纯批量更新: POST api/{entity}/batch-update
        public static string BatchUpdate(string entityName) => $"api/{entityName}/batch-update";

        // 纯批量删除: POST 或 DELETE api/{entity}/batch-delete
        public static string BatchDelete(string entityName) => $"api/{entityName}/batch-delete";
    }
}
