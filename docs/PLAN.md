# Interview Agent v0.1 实施记录

状态：第一版功能完成，进入发布验收。

## 已完成切片

- [x] 仓库、产品规格、威胁模型与性能预算
- [x] .NET 8 分层解决方案和核心领域模型
- [x] DOCX 安全解析、知识分块与真实面经导入
- [x] SQLite FTS5、BGE/字符 n-gram、RRF 混合检索
- [x] DeepSeek V4 Flash SSE 流式回答
- [x] 火山引擎 v3 双向流式 ASR 协议
- [x] WASAPI 系统回放与麦克风采集、16 kHz PCM16 转换
- [x] 4.5 秒本地兜底、请求取消与总超时
- [x] DPAPI 加密设置与最小化结构化日志
- [x] WPF 主界面、设置页、F8 全局热键、置顶和 DOCX 导入
- [x] BGE-small-zh-v1.5 ONNX 量化推理与哈希降级
- [x] 真实《面经.docx》、真实 BGE 模型、构建、测试和依赖安全审计
- [ ] 使用真实 DeepSeek/火山账号做线上联调（需要用户凭据和对应服务已开通）

## 发布门禁

每次发布都执行：

1. `codegraph sync` 并审查关键调用路径。
2. `dotnet restore InterviewAgent.sln`。
3. `dotnet test InterviewAgent.sln -c Release --no-restore`。
4. `dotnet build InterviewAgent.sln -c Release --no-restore`。
5. `dotnet list InterviewAgent.sln package --vulnerable --include-transitive`。
6. 发布 `win-x64` 框架依赖版本，附带文档、工具和校验过的 BGE 模型。
7. 启动发布版，确认进程存活、真实面经可检索、日志不含正文或密钥。

## 第二版候选项

- 自动静音检测和可配置热键。
- 火山 ASR 热词/上下文配置。
- 问题类型分类和回答长度快捷档位。
- RAG 离线评测集、召回率和 MRR 仪表板。
- 单文件安装器、自动更新和代码签名。
