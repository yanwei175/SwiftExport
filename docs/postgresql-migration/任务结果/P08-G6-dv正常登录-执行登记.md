# G6 dv正常登录只读执行登记

2026-10-06 05:44，主控串行。用户已保存dv应用密码并明确通知；默认沙箱看不到实际用户变量，授权实际用户环境只输出存在性，确认SWIFTEXPORT_RUNTIME_DV_PASSWORD(User)=存在。密码不输出、不持久化到脚本或结果，不重置账号/权限。

复用当前真实WebApi入口及编译产物，必要WebApi编译保证依赖与源码一致；不新增测试方法、不运行单元或全量。实际启动路径无播种/迁移/HostedService初始化，AuthSessionService.AuthenticateAsync只读账号/BCrypt验证并签发JWT；DevUser只通过本子进程配置关闭。连接保持192.168.3.12:5432/dv_next，追加default_transaction_read_only=on和唯一ApplicationName，用户appsettings不改。

运行前50表51989行完整摘要留存。仅匿名me、正常dv登录、原JWT me和产品分页四个HTTP请求，不调用业务写入端点、不建夹具、不取号、不改结构或备份。启动本人隐藏后台进程，绑定本机随机端口，finally只停止本人PID并恢复环境；实际密码和令牌仅内存使用。结束独立DBX核对50表数量/完整MD5与进程/连接退出。

只允许写本批脚本/证据/记录及主控状态/清单。A/B不续派、不提交合并。此场景只证明运行库正常登录及两个只读入口，不称运行库实际业务写入/P08最终/P09已完成。
