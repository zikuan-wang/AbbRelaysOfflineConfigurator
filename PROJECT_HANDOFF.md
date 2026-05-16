# PROJECT_HANDOFF

Last updated: 2026-05-16

## 维护规则

这是当前项目的交接文件。后续接手时，应以当前代码库实际状态为准更新本文档，不要反过来修改项目去匹配旧交接记录。

本次交接确认：项目代码保持用户当前最终状态；此前尝试移除主题切换的改动已回退。主题颜色切换功能当前保留。

## 工作目录

当前项目目录：

`C:\Users\zikuan-wang\OneDrive\Personal\03_project\ABBRelaysOfflineConfigurator`

主解决方案：

`C:\Users\zikuan-wang\OneDrive\Personal\03_project\ABBRelaysOfflineConfigurator\ABBRelaysOfflineConfigurator.sln`

GitHub 代码仓库：

`https://github.com/zikuan-wang/AbbRelaysOfflineConfigurator`

GitHub 在线更新 Release 仓库：

`https://github.com/zikuan-wang/AbbRelaysOfflineConfigurator_Release`

## 当前版本

当前产品版本：`2.0.8`

版本号已在以下位置同步：

- `AbbRelaysOfflineConfigurator\AbbRelaysOfflineConfigurator.csproj`
- `AbbRelaysLicensing\AbbRelaysLicensing.csproj`
- `AbbRelaysAuthorizationTool\AbbRelaysAuthorizationTool.csproj`
- `Tools\Package.ps1`
- `README.md`

本地打包产物：

- `Generated\Package\ABBRelaysOfflineConfigurator_2.0.8.msi`
- `Generated\Package\ABBRelaysOfflineConfigurator.msi`
- `Generated\Package\AuthorizationTool\ABBRelaysAuthorizationTool.exe`

历史 MSI/WiX 调试文件 `2.0.1` 到 `2.0.7` 仍保存在 `Generated\Package` 中；当前版本以 `2.0.8` 为准。

## 当前 Git 状态

当前分支：`main`

当前交接结论：代码文件无未提交修改；本次只新增/维护 `PROJECT_HANDOFF.md` 作为项目最终状态说明。

不要执行：

```powershell
git reset --hard
git checkout -- .
```

除非用户明确要求清理工作区。

## 技术栈

- `.NET 8`
- WPF
- `MaterialDesignInXamlToolkit` / `MaterialDesignThemes`
- 本地 XML/JSON 数据包
- 本地生成组合代码、订货码、模块订货清单、装置描述和导出文件
- 部分产品支持 ABB 在线校验或转换接口

## 项目结构

主要项目：

- `AbbRelaysOfflineConfigurator`：主 WPF 客户端
- `AbbRelaysLicensing`：授权/激活文件模型、加密、签名校验逻辑
- `AbbRelaysAuthorizationTool`：本地授权签发工具

关键目录：

- `AbbRelaysOfflineConfigurator\Data`：规则、功能清单和本地数据
- `AbbRelaysOfflineConfigurator\Data\TerminalDiagrams`：REX615/REX640 接线图 PNG，当前 49 个文件
- `AbbRelaysOfflineConfigurator\Data\Rio600Diagrams`：RIO600 接线图/尺寸图 PNG，当前 25 个文件
- `AbbRelaysOfflineConfigurator\Services`：规则加载、校验、导出、在线校验、更新、接线图等服务
- `AbbRelaysOfflineConfigurator\ViewModels`：各页面 ViewModel
- `AbbRelaysOfflineConfigurator\Views`：REX640、RIO600、SSC600、REX600、CN 选型和转换页面
- `Tools\Package.ps1`：打包脚本
- `Tools\Installer\Product.wxs`：WiX MSI 定义

## 当前导航状态

左侧抽屉导航显示顺序：

1. 首页
2. REX615 选型
3. REX640 选型
4. RIO600 选型
5. SSC600 选型
6. REX600 选型
7. 615/620 CN 选型
8. 615/620 转换
9. 授权 / 关于 / 更新

隐藏 `TabControl` 实际索引：

- `0` 首页
- `1` REX615
- `2` SSC600
- `3` RIO600
- `4` 615/620 CN
- `5` 615/620 转换
- `6` 授权/关于/更新
- `7` REX600
- `8` REX640

首页推荐卡片使用这些实际索引跳转，修改导航时要同步 `MainWindow.xaml.cs` 和 `HomeViewModel.cs`。

## 当前功能范围

主程序当前覆盖：

- 首页产品推荐：按 ANSI code、ABB code、中英文功能名推荐产品
- REX615 选型：组合代码、订货号导入/在线校验、I/O 摘要、槽位分配、装置描述、导出、APP 推荐、附件/额外项目
- REX640 选型：PCL5/PCL6，组合代码、APP 推荐、I/O 摘要、槽位分配、接线图、在线校验、装置描述
- RIO600 选型：模块配置、模块订货清单、复制/导出 Excel、I/O 摘要、接线图/尺寸图、装置描述
- SSC600 / SSC600 SW 选型：订货码、在线校验、装置描述、应用包推荐、应用包功能清单
- REX600 选型：功能清单、I/O 摘要、装置描述、在线校验
- 615/620 CN 选型：订货号导入、I/O 摘要、标准配置推荐、保护功能清单
- 615/620 转换：内置规则批量转换，支持在线转换接口
- 授权/关于/更新：授权申请/导入、GitHub Release 更新检查和 MSI 下载

## UI 状态

当前主窗口保留：

- Material Design 风格
- 浅色界面
- 顶部设置弹窗
- 显示完整描述开关
- 中英文显示语言切换
- 主题颜色切换，当前可选色包括莱姆绿、提香红、马尔斯绿、克莱因蓝、勃艮第、申布伦黄、蒂芙尼蓝、中国红、凡戴克棕、爱马仕橙、普鲁士蓝

用户已明确：主题切换保留。

## 授权系统

授权库：`AbbRelaysLicensing`

授权工具：`AbbRelaysAuthorizationTool`

关键点：

- 请求文件扩展名：`.zwreq`
- 激活文件扩展名：`.zwlic`
- 加密封装：AES-GCM
- 激活签名：RSA
- 主程序校验加密封装、RSA 签名、机器指纹和有效期
- 私钥不嵌入客户端
- 授权工具私钥来源：`ABB_RELAYS_AUTH_PRIVATE_KEY_BASE64`、兼容旧变量 `REX615_AUTH_PRIVATE_KEY_BASE64`、或工具目录下 `authorization-private-key.txt`
- 授权工具导出激活文件后会写入 `authorized-devices.json`
- 授权记录文件路径为授权工具运行目录下的 `authorized-devices.json`

用户要求：授权工具 EXE 只本地保存，不上传 GitHub Release。

## 在线校验和更新

在线更新：

- `UpdateCheckService`
- Release 源固定为 `https://github.com/zikuan-wang/AbbRelaysOfflineConfigurator_Release`
- 只支持下载 GitHub Release 中的 MSI 安装包

在线校验/转换：

- `OnlineValidationService`
- 615/620 转换接口：`https://relays.protection-control.abb/api/Products/ConvertCode`
- 订货号和组合代码校验会处理 PCL 版本后缀

## 打包命令

普通构建：

```powershell
dotnet build .\ABBRelaysOfflineConfigurator.sln --no-restore /nr:false
```

Release 构建：

```powershell
dotnet build .\ABBRelaysOfflineConfigurator.sln -c Release /nr:false
```

完整打包当前版本：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Tools\Package.ps1 -ProductVersion 2.0.8
```

仅生成授权工具 EXE：

```powershell
dotnet publish .\AbbRelaysAuthorizationTool\AbbRelaysAuthorizationTool.csproj -c Release -r win-x64 --self-contained true -o .\Generated\Package\AuthorizationTool /p:Version=2.0.8 /p:AssemblyVersion=2.0.8.0 /p:FileVersion=2.0.8.0 /p:InformationalVersion=2.0.8 /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true /p:DebugType=None /p:DebugSymbols=false
```

发布到 Release 时只上传主程序 MSI，不上传授权工具。

## 本次验证

2026-05-16 已执行：

```powershell
dotnet build .\ABBRelaysOfflineConfigurator.sln --no-restore /nr:false
```

结果：

- `AbbRelaysLicensing` 构建成功
- `AbbRelaysAuthorizationTool` 构建成功
- `AbbRelaysOfflineConfigurator` 构建成功
- `0` 警告
- `0` 错误

## 后续接手注意

- 当前代码被用户认定为最终状态，不要主动做功能调整。
- 若只需要维护交接文档，只修改 `PROJECT_HANDOFF.md`。
- 涉及 UI 时，优先保持当前 Material Design 风格和主题切换能力。
- 打包、发布、上传 Release 只在用户明确要求时执行。
- 授权工具不要上传到 Release。
