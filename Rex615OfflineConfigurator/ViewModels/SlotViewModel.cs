using Rex615OfflineConfigurator.Models;
using Rex615OfflineConfigurator.Services;

namespace Rex615OfflineConfigurator.ViewModels;

public sealed class SlotViewModel(SlotAssignment assignment)
{
    public string SlotId { get; } = assignment.SlotId;
    public string Code { get; } = assignment.Code;
    public string Description { get; } = assignment.Description;
    public string? TargetGroupName { get; } = assignment.GroupName;
    public string? TargetOptionId { get; } = assignment.OptionId;
    public bool IsAssigned { get; } = assignment.IsAssigned;
    public bool IsHardware { get; } = assignment.IsHardware;
    public bool IsFixed { get; } = assignment.IsFixed;
    public int CodeOrder { get; } = assignment.CodeOrder;
    public bool HasTerminalDiagram => TerminalDiagramService.HasDiagram(Code);
    public string TerminalDiagramToolTip => HasTerminalDiagram ? "查看接线图" : "未配置接线图";
    public bool CanJump => IsAssigned &&
        !string.IsNullOrWhiteSpace(TargetGroupName) &&
        !string.IsNullOrWhiteSpace(TargetOptionId);
}
