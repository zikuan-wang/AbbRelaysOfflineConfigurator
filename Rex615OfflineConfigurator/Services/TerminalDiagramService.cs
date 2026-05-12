using System.IO;

namespace Rex615OfflineConfigurator.Services;

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
            candidates.Add(Path.Combine(current.FullName, "Rex615OfflineConfigurator", "Data", DiagramFolderName, fileName));
            candidates.Add(Path.Combine(current.FullName, "Data", DiagramFolderName, fileName));
            current = current.Parent;
        }

        return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
    }
}
