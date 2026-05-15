using System.IO;
using System.Reflection;

namespace AbbRelaysOfflineConfigurator.Services;

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

        var assembly = Assembly.GetExecutingAssembly();
        findings.AddRange(EmbeddedPrivateKeyProviderTypes
            .Where(typeName => assembly.GetType(typeName, throwOnError: false) is not null)
            .Select(typeName => $"embedded:{typeName}"));

        if (findings.Count > 0)
        {
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
