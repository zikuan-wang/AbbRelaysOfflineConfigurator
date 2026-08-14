# Unofficial ABB Relays Offline Configurator

非官方 ABB 继保离线选型工具，基于本地 XML/JSON 数据包实现组合代码生成、互斥校验、槽位分配、I/O 摘要、APP 功能推荐、SSC600/SSC600 SW 订货码生成、615/620 CN 选型、RE_630 选型和旧订货号转换。

This is an unofficial tool. It is not ABB official software and is not sponsored, endorsed, or authorized by ABB.

## Build

- .NET 8 SDK
- Windows
- WiX Toolset CLI for MSI packaging

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Tools\Verify.ps1
dotnet build .\ABBRelaysOfflineConfigurator.sln -c Release
powershell -NoProfile -ExecutionPolicy Bypass -File .\Tools\Package.ps1
```

## Notes

Generated installers, build output, ABB reference PDFs/DWGs and spreadsheet source documents are intentionally not committed.

Copyright belongs to zikuan wang. ABB, REX615, REX640, RE_630, SSC600, RIO600 and related product names belong to their respective owners.

This tool only implements local selection assistance and code generation. It does not copy ABB official online configurator pages or protected presentation forms, and it does not constitute an ABB official quotation, ordering confirmation, engineering design conclusion, or technical commitment.
