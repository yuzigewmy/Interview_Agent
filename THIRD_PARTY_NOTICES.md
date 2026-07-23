# Third-party notices

Interview Agent 使用的 NuGet 包及其许可信息以各包发布内容为准，主要包括：

- DocumentFormat.OpenXml
- Microsoft.Data.Sqlite / SQLitePCLRaw
- Microsoft.ML.OnnxRuntime
- NAudio
- System.Security.Cryptography.ProtectedData
- xUnit、Microsoft.NET.Test.Sdk、coverlet（仅测试）

## BGE-small-zh-v1.5

- 原始模型：`BAAI/bge-small-zh-v1.5`
- 原始模型页面：https://huggingface.co/BAAI/bge-small-zh-v1.5
- 许可：MIT；原始模型卡声明可免费商用。
- ONNX 转换：`Xenova/bge-small-zh-v1.5`
- 固定提交：`fcecc3c5fef6becfa2b2bdda15c1c938857be534`
- 量化模型 SHA-256：`15b717c382bcb518ba457b93ea6850ede7f4f1cd8937454aa06972366cd19bcc`
- 词表 SHA-256：`45bbac6b341c319adc98a532532882e91a9cefc0329aa57bac9ae761c27b291c`

安装脚本通过固定提交下载，并在写入模型目录前验证 SHA-256。
