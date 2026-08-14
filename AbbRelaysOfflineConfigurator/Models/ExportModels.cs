namespace AbbRelaysOfflineConfigurator.Models;

public sealed record SelectedOptionSummary(
    string GroupName,
    string Id,
    string Description);

public sealed record ExportSlotSummary(
    string SlotId,
    string Code,
    string Description);

public sealed record ExportIoSummary(
    string Name,
    string Value);

public sealed record ExportAppFunctionSummary(
    string AppId,
    string FunctionCode,
    string Ansi,
    string ChineseName,
    string EnglishName);

public sealed record ExportSnapshot(
    string CombinationCode,
    string OrderingNumber,
    string Status,
    string OnlineStatus,
    bool IsValid,
    IReadOnlyList<SelectedOptionSummary> Selections,
    IReadOnlyList<ExportIoSummary> IoSummary,
    string SelectedAppSummary,
    IReadOnlyList<ExportAppFunctionSummary> AppFunctions,
    IReadOnlyList<ExportSlotSummary> Slots,
    IReadOnlyList<string> Messages,
    string DeviceDescription,
    string ProductTitle = "ABB REX615 配置");
