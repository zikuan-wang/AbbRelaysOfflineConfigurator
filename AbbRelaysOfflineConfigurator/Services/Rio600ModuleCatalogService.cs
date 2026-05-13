using System.IO;

namespace AbbRelaysOfflineConfigurator.Services;

public static class Rio600ModuleCatalogService
{
    private static readonly Dictionary<string, Rio600ModuleDetail> Details = new(StringComparer.OrdinalIgnoreCase)
    {
        ["PSMH"] = Detail("PSMH", "High-power supply module", "MOD600APSMH07", "高压电源模块", DimensionStandard(),
            [Terminal("X1", ("1", "+", "辅助电源输入正端"), ("2", "NC", "未连接"), ("3", "-", "辅助电源输入负端"), ("4", "+", "辅助电源输入正端"), ("5", "NC", "未连接"), ("6", "-", "辅助电源输入负端"))]),
        ["PSML"] = Detail("PSML", "Low-power supply module", "MOD600APSML07", "低压电源模块", DimensionStandard(),
            [Terminal("X1", ("1", "+", "DC 辅助电源正端"), ("2", "-", "DC 辅助电源负端"), ("3", "+", "DC 辅助电源正端"), ("4", "-", "DC 辅助电源负端"), ("5", "+", "DC 辅助电源正端"), ("6", "-", "DC 辅助电源负端"))]),
        ["LECMIR"] = Detail("LECM", "Communication module, RJ-45", "MOD600GLECMIR", "RJ-45 电口通讯模块", DimensionLecm(),
            [Terminal("Ethernet", ("RJ-45", "10/100Base-TX", "屏蔽双绞线，至少 CAT5e"))], "LECMIR", hasConnectionImage: false),
        ["LECMFO"] = Detail("LECM", "Communication module, multimode LC", "MOD600CLECMFO", "多模光纤 LC 通讯模块", DimensionLecm(),
            [Terminal("Fiber", ("LC", "100 Mbit/s", "多模 62.5/125 μm 或 50/125 μm 玻璃光纤"))], "LECMFO", hasConnectionImage: false),
        ["DIM8H"] = Detail("DIM8H", "Digital input module, high voltage", "MOD600ADIM8H", "8 路高压开关量输入模块", DimensionStandard(),
            BinaryInputTerminals()),
        ["DIM8L"] = Detail("DIM8L", "Digital input module, low voltage", "MOD600ADIM8L", "8 路低压开关量输入模块", DimensionStandard(),
            BinaryInputTerminals()),
        ["DOM4"] = Detail("DOM4", "Digital output module", "MOD600ADOM4R", "4 路开关量输出模块", DimensionDom(),
            [Terminal("X1", ("1", "COM", "公共端"), ("2", "DO1", "输出 1"), ("3", "DO2", "输出 2"), ("4", "DO3", "输出 3"), ("5", "DO4", "输出 4"), ("6", "COM", "公共端"))]),
        ["RTD4"] = Detail("RTD4", "RTD/mA input module", "MOD600ARTD4", "4 路 RTD/mA 输入模块", DimensionStandard(),
            [Terminal("X1", ("1", "1C", "通道 1 公共端"), ("2", "1-", "通道 1 负端"), ("3", "1+", "通道 1 正端"), ("4", "2C", "通道 2 公共端"), ("5", "2-", "通道 2 负端"), ("6", "2+", "通道 2 正端")),
             Terminal("X2", ("1", "3C", "通道 3 公共端"), ("2", "3-", "通道 3 负端"), ("3", "3+", "通道 3 正端"), ("4", "4C", "通道 4 公共端"), ("5", "4-", "通道 4 负端"), ("6", "4+", "通道 4 正端"))]),
        ["AOM4"] = Detail("AOM4", "Analog output module", "MOD600AAOM4", "4 路模拟量输出模块", DimensionStandard(),
            [Terminal("X1", ("1", "1-", "mA 输出 1 负端"), ("2", "1+", "mA 输出 1 正端"), ("3", "NC", "未连接"), ("4", "NC", "未连接"), ("5", "2+", "mA 输出 2 正端"), ("6", "2-", "mA 输出 2 负端")),
             Terminal("X2", ("1", "3-", "mA 输出 3 负端"), ("2", "3+", "mA 输出 3 正端"), ("3", "NC", "未连接"), ("4", "NC", "未连接"), ("5", "4+", "mA 输出 4 正端"), ("6", "4-", "mA 输出 4 负端"))]),
        ["SIM8F"] = Detail("SIM8F", "Sensor input module with currents and voltages", "MOD600ASIM8F", "4 路电流和 3 路电压传感器输入模块", DimensionSim(),
            [Terminal("Sensor inputs", ("I0", "PIN4 / PIN8", "残余电流传感器输入"), ("I1", "PIN4 / PIN8", "相电流 L1 输入"), ("I2", "PIN4 / PIN8", "相电流 L2 输入"), ("I3", "PIN4 / PIN8", "相电流 L3 输入"), ("U1-U2", "PIN4 / PIN8", "电压传感器输入"))]),
        ["SIM4F"] = Detail("SIM4F", "Sensor input module with currents", "MOD600ASIM4F", "4 路电流传感器输入模块", DimensionSim(),
            [Terminal("Sensor inputs", ("I0", "PIN4 / PIN8", "残余电流传感器输入"), ("I1", "PIN4 / PIN8", "相电流 L1 输入"), ("I2", "PIN4 / PIN8", "相电流 L2 输入"), ("I3", "PIN4 / PIN8", "相电流 L3 输入"))]),
        ["SCM8H"] = Detail("SCM8H", "Smart control module, high voltage", "MOD600ASCM8H", "高压智能控制模块，4 路输入和 4 路高速输出", DimensionStandard(),
            ScmTerminals()),
        ["SCM8L"] = Detail("SCM8L", "Smart control module, low voltage", "MOD600ASCM8L", "低压智能控制模块，4 路输入和 4 路高速输出", DimensionStandard(),
            ScmTerminals())
    };

    public static Rio600ModuleDetail? GetDetail(string key) =>
        Details.TryGetValue(key, out var detail) ? detail : null;

    public static string OrderNumberFor(string key) => GetDetail(key)?.OrderNumber ?? "";

    public static double WidthFor(string key) => GetDetail(key)?.Dimensions.A ?? 46;

    private static Rio600ModuleDetail Detail(
        string code,
        string name,
        string orderNumber,
        string description,
        Rio600Dimension dimensions,
        IReadOnlyList<Rio600TerminalGroup> terminals,
        string? imageKey = null,
        bool hasConnectionImage = true)
    {
        var key = string.IsNullOrWhiteSpace(imageKey) ? code : imageKey;
        return new(
            code,
            name,
            orderNumber,
            description,
            dimensions,
            terminals,
            hasConnectionImage ? ImagePath($"{key}_connection.png") : null,
            ImagePath($"{key}_dimension.png"));
    }

    private static string ImagePath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "Data", "Rio600Diagrams", fileName);

    private static IReadOnlyList<Rio600TerminalGroup> BinaryInputTerminals() =>
    [
        Terminal("X1", ("1", "COM", "DI1-DI4 公共端"), ("2", "DI1", "开关量输入 1"), ("3", "DI2", "开关量输入 2"), ("4", "DI3", "开关量输入 3"), ("5", "DI4", "开关量输入 4"), ("6", "COM", "DI1-DI4 公共端")),
        Terminal("X2", ("1", "COM", "DI5-DI8 公共端"), ("2", "DI5", "开关量输入 5"), ("3", "DI6", "开关量输入 6"), ("4", "DI7", "开关量输入 7"), ("5", "DI8", "开关量输入 8"), ("6", "COM", "DI5-DI8 公共端"))
    ];

    private static IReadOnlyList<Rio600TerminalGroup> ScmTerminals() =>
    [
        Terminal("X1", ("1", "COM", "DI1-DI2 公共端"), ("2", "DI1", "开关量输入 1"), ("3", "DI2", "开关量输入 2"), ("4", "DI3", "开关量输入 3"), ("5", "DI4", "开关量输入 4"), ("6", "COM", "DI3-DI4 公共端")),
        Terminal("X2", ("1", "L+", "高速输出供电正端"), ("2", "HSO1", "高速输出 1"), ("3", "HSO2", "高速输出 2"), ("4", "HSO3", "高速输出 3"), ("5", "HSO4", "高速输出 4"), ("6", "L-", "高速输出供电负端"))
    ];

    private static Rio600TerminalGroup Terminal(string connector, params (string Number, string Label, string Description)[] terminals) =>
        new(connector, terminals.Select(item => new Rio600Terminal(item.Number, item.Label, item.Description)).ToList());

    private static Rio600Dimension DimensionStandard() =>
        new(46, 4.5, 51, 81, 146, 99, "PSMH/PSML, DIM8H/DIM8L, RTD4, AOM4, SCM8H/SCM8L");

    private static Rio600Dimension DimensionDom() =>
        new(27.5, 4.5, 33, 81, 146, 99, "DOM4");

    private static Rio600Dimension DimensionLecm() =>
        new(27.5, 4.5, 33, 81, 146, 81, "LECM");

    private static Rio600Dimension DimensionSim() =>
        new(46, 4.25, 51, 81, 145.5, 85, "SIM8F/SIM4F");
}

public sealed record Rio600ModuleDetail(
    string Code,
    string Name,
    string OrderNumber,
    string Description,
    Rio600Dimension Dimensions,
    IReadOnlyList<Rio600TerminalGroup> Terminals,
    string? ConnectionImagePath,
    string DimensionImagePath)
{
    public bool HasConnectionImage => !string.IsNullOrWhiteSpace(ConnectionImagePath);
};

public sealed record Rio600Dimension(double A, double B, double C, double D, double E, double F, string AppliesTo)
{
    public string AText => $"{A:g} mm";
    public string BText => $"{B:g} mm";
    public string CText => $"{C:g} mm";
    public string DText => $"{D:g} mm";
    public string EText => $"{E:g} mm";
    public string FText => $"{F:g} mm";
}

public sealed record Rio600TerminalGroup(string Connector, IReadOnlyList<Rio600Terminal> Terminals);

public sealed record Rio600Terminal(string Number, string Label, string Description);
