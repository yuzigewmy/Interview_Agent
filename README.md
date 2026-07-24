# Interview Agent v0.2

面向 Windows 在线面试场景的本地桌面辅助工具。可手动控制系统音频或麦克风采集，实时转写面试官问题，优先检索本地面经，再由用户配置的 OpenAI 兼容模型以候选人口吻流式组织回答。

> 请仅在取得参与者同意，并符合当地法律、公司制度及面试平台规则时使用。应用不会绕过会议软件、考试或面试平台的安全机制。

## 已实现

- WASAPI 系统回放与麦克风采集，可在界面中切换并刷新音源。
- 界面按钮和全局 `F8` 手动开始/停止，避免采集候选人自己的回答。
- 火山引擎大模型流式语音识别，支持新 API Key 和旧 AppKey/AccessKey 两种凭据。
- 《面经.docx》本地解析、分块、SQLite FTS5、BGE-small-zh-v1.5 语义向量与 RRF 混合排序。
- 模型提供商、Model Name、Base URL、API Key 和最大输出 Token 均可配置，支持 DeepSeek、通义千问、Moonshot、智谱、OpenAI、Ollama、LM Studio 等兼容服务。
- 4.5 秒首字兜底：云端尚未输出时，先展示命中的本地面经内容。
- 最近 6 轮问答作为内存短期上下文，支持面试官围绕上一题继续追问；程序退出自动清除。
- 自动记录本次面试的完整问题与回答，一键导出为 `<首道面试题>_<时间戳>.docx`。
- 固定候选人第一人称提示词，并把知识库内容按“不可信资料”处理，降低提示词注入风险。
- DPAPI 当前 Windows 用户级凭据加密；日志不记录音频、问题、答案或密钥。
- 无云服务凭据时可使用演示模式验证文本检索和界面流程。

## 直接使用

发布版入口：

```text
release\InterviewAgent-win-x64-v0.2\InterviewAgent.exe
```

1. 启动应用。首次运行时，如果桌面存在 `面经.docx`，会自动建立本地知识库。
2. 未配置云服务时保持“演示模式”，可直接输入问题并测试本地检索。
3. 点击右上角“设置”，填写模型提供商、Model Name、Base URL、可选 API Key，以及火山引擎凭据，然后关闭演示模式。
4. 选择“系统输出”可听取会议软件声音，选择“麦克风”可采集外部扬声器声音；接入新设备后点击音源右侧的刷新图标。
5. 面试官开始提问时按 `F8` 开始，问题结束时再按 `F8`；转写完成后会自动检索并生成回答。
6. 面试结束后点击“导出记录”，选择位置并保存本次问答 DOCX。

发布包已携带校验过的量化 BGE 模型。若模型被移除，可运行：

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\install-bge.ps1
```

详细凭据与故障排查见 [配置说明](docs/配置说明.md)，验证结果见 [测试报告](docs/测试报告.md)。

## 数据位置

应用运行数据位于：

```text
%LOCALAPPDATA%\InterviewAgent\
├─ data\knowledge.db
├─ logs\interview-agent-YYYYMMDD.jsonl
├─ models\bge-small-zh-v1.5\
└─ secrets.dat
```

- DOCX 正文和向量只保存在本地 SQLite。
- 音频帧只在内存中流转；云服务模式下会发送到配置的火山引擎 ASR。
- 只有短期问答上下文、当前问题和检索命中的少量知识片段会发送给用户配置的模型服务。
- 本次面试问答只保存在进程内存中，除非用户主动点击“导出记录”。
- `secrets.dat` 只能由保存它的当前 Windows 用户解密。

## 开发与构建

要求 Windows 10/11、.NET 8 SDK：

```powershell
dotnet restore InterviewAgent.sln
dotnet test InterviewAgent.sln -c Release
dotnet publish src\InterviewAgent.App\InterviewAgent.App.csproj `
  -c Release -r win-x64 --self-contained false `
  -o release\InterviewAgent
```

架构与验收边界见 [产品规格](docs/SPEC.md)、[实施记录](docs/PLAN.md) 和 [架构决策](docs/ADR-001-provider-and-retrieval.md)。
