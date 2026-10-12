// 测试项目同时引用后端程序集时，明确 WPF Application 类型，避免被 SwiftExport.Application 命名空间遮蔽。
global using Application = global::System.Windows.Application;
