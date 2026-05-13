# ABB Relays Offline Configurator

ABB 继保离线选型工具，基于本地 XML/JSON 数据包实现组合代码生成、互斥校验、槽位分配、I/O 摘要、APP 功能推荐、615/620 CN 选型和旧订货号转换。

## Build

- .NET 8 SDK
- Windows
- WiX Toolset CLI for MSI packaging

```powershell
dotnet build .\ABBRelaysOfflineConfigurator.sln -c Release
powershell -NoProfile -ExecutionPolicy Bypass -File .\Tools\Package.ps1 -ProductVersion 1.3.2
```

## Notes

Generated installers, build output, ABB reference PDFs/DWGs and spreadsheet source documents are intentionally not committed.

Copyright belongs to zikuan wang. ABB, REX615, RIO600 and related product names belong to their respective owners.
