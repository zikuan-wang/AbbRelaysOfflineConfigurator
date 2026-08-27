using System.IO;
using System.Reflection;

namespace AbbRelaysOfflineConfigurator.Services;

// 客户端发布边界的纵深防护：检测授权工具专用私钥文件或私钥提供器是否被误打包进主程序。
// 该检查不能替代发布流程的敏感文件扫描，但可阻止明显错误的安装包继续运行并暴露签名能力。
internal static class RuntimeSecurityGuard
{
    private static readonly string[] PrivateKeyFileNames =
    [
        "authorization-private-key.txt",
        "rex615-authorization-private-key.txt"
    ];

    private static readonly string[] EmbeddedPrivateKeyProviderTypes =
    [
        "AbbRelaysAuthorizationTool.AuthorizationPrivateKeyProvider",
        "Rex615AuthorizationTool.AuthorizationPrivateKeyProvider"
    ];

    public static void EnsureClientRuntimeSafe()
    {
        var findings = new List<string>();

        // BaseDirectory 是已发布程序实际目录，CurrentDirectory 可能由快捷方式或命令行改变；
        // 两处都检查可覆盖常见的复制运行和开发调试场景。
        foreach (var directory in RuntimeDirectories())
        {
            foreach (var fileName in PrivateKeyFileNames)
            {
                var path = Path.Combine(directory, fileName);
                if (File.Exists(path))
                {
                    findings.Add(path);
                }
            }
        }

        // 除落地私钥文件外，还要防止授权工具的私钥提供器源码被错误编译进客户端程序集。
        var assembly = Assembly.GetExecutingAssembly();
        findings.AddRange(EmbeddedPrivateKeyProviderTypes
            .Where(typeName => assembly.GetType(typeName, throwOnError: false) is not null)
            .Select(typeName => $"embedded:{typeName}"));

        if (findings.Count > 0)
        {
            // 私钥一旦随客户端交付，攻击者即可自行签发任意授权；因此这是不可降级为警告的启动失败。
            throw new InvalidOperationException(
                "检测到客户端程序目录包含授权签名私钥或私钥提供器。为避免授权体系被破解，主程序已停止启动。请从客户端安装目录移除以下内容：\n" +
                string.Join("\n", findings.Distinct(StringComparer.OrdinalIgnoreCase)));
        }
    }

    private static IEnumerable<string> RuntimeDirectories()
    {
        yield return AppContext.BaseDirectory;
        yield return Environment.CurrentDirectory;
    }
}
