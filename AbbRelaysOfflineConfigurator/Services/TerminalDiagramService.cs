using System.IO;

namespace AbbRelaysOfflineConfigurator.Services;

public sealed record TerminalDiagram(string Title, string ImagePath);

public static class TerminalDiagramService
{
    private const string DiagramFolderName = "TerminalDiagrams";

    private static readonly Dictionary<string, string[]> DiagramFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AIM3"] = ["REX615_AIM3.png"],
        ["AIM4"] = ["REX615_AIM4.png"],
        ["AIM5"] =
        [
            "REX615_AIM5_15_high-impedance restricted earth-fault.png",
            "REX615_AIM5_15_low-imbedance restricted earth-fault.png"
        ],
        ["AIM15"] =
        [
            "REX615_AIM5_15_high-impedance restricted earth-fault.png",
            "REX615_AIM5_15_low-imbedance restricted earth-fault.png"
        ],
        ["AIM6"] = ["REX615_AIM6.png"],
        ["AIM16"] = ["REX615_AIM1617.png"],
        ["AIM17"] = ["REX615_AIM1617.png"],
        ["AIM18"] = ["REX615_AIM1819.png"],
        ["AIM19"] = ["REX615_AIM1819.png"],
        ["BIO5"] = ["REX615_BIO5.png"],
        ["BIO6"] = ["REX615_BIO6.png"],
        ["BIO7"] = ["REX615_BIO7.png"],
        ["COM1"] = ["REX615_X000_COM1.png"],
        ["COM8"] = ["REX615_X000_COM8.png"],
        ["COM9"] = ["REX615_X000_COM9.png"],
        ["COM10"] = ["REX615_X000_COM10.png"],
        ["COM11"] = ["REX615_X000_COM11.png"],
        ["COM12"] = ["REX615_X000_COM12.png"],
        ["COM13"] = ["REX615_X000_COM13.png"],
        ["COM14"] = ["REX615_X000_COM14.png"],
        ["COM15"] = ["REX615_X000_COM15.png"],
        ["COM16"] = ["REX615_X000_COM16.png"],
        ["COM17"] = ["REX615_X000_COM17.png"],
        ["COM18"] = ["REX615_X000_COM18.png"],
        ["COM27"] = ["REX615_X000_COM27.png"],
        ["COM31"] = ["REX615_X000_COM31.png"],
        ["COM32"] = ["REX615_X000_COM32.png"],
        ["COM33"] = ["REX615_X000_COM33.png"],
        ["COM34"] = ["REX615_X000_COM34.png"],
        ["COM37"] = ["REX615_X000_COM37.png"],
        ["PSM3"] = ["REX615_PSM3_4.png"],
        ["PSM4"] = ["REX615_PSM3_4.png"],
        ["RTD1"] = ["REX615_RTD1.png"],
        ["RTD2"] = ["REX615_RTD2.png"],
        ["RTD3"] = ["REX615_RTD3.png"],
        ["SIM5"] = ["REX615_SIM5.png"],
    };

    public static IReadOnlyList<TerminalDiagram> GetDiagrams(string code)
    {
        if (string.IsNullOrWhiteSpace(code) ||
            !DiagramFiles.TryGetValue(code.Trim(), out var fileNames))
        {
            return [];
        }

        return fileNames
            .Select(fileName => new TerminalDiagram(BuildTitle(code, fileName), ResolveDiagramPath(fileName)))
            .Where(diagram => File.Exists(diagram.ImagePath))
            .ToList();
    }

    public static bool HasDiagram(string code) => GetDiagrams(code).Count > 0;

    private static string BuildTitle(string code, string fileName)
    {
        if (fileName.StartsWith("REX615_X000_", StringComparison.OrdinalIgnoreCase))
        {
            return $"{code} X000 通讯模块图";
        }

        if (fileName.Contains("high-impedance", StringComparison.OrdinalIgnoreCase))
        {
            return $"{code} 高阻接地差动";
        }

        if (fileName.Contains("low-imbedance", StringComparison.OrdinalIgnoreCase) ||
            fileName.Contains("low-impedance", StringComparison.OrdinalIgnoreCase))
        {
            return $"{code} 低阻接地差动";
        }

        return $"{code} 接线图";
    }

    private static string ResolveDiagramPath(string fileName)
    {
        var candidates = new List<string>
        {
            Path.Combine(AppContext.BaseDirectory, "Data", DiagramFolderName, fileName),
            Path.Combine(Environment.CurrentDirectory, "Data", DiagramFolderName, fileName)
        };

        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            candidates.Add(Path.Combine(current.FullName, "AbbRelaysOfflineConfigurator", "Data", DiagramFolderName, fileName));
            candidates.Add(Path.Combine(current.FullName, "Data", DiagramFolderName, fileName));
            current = current.Parent;
        }

        return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
    }
}
