using System.IO;
using System.Reflection;

namespace AbbRelaysAuthorizationTool;

// 授权工具专用的私钥解析入口。兼容嵌入提供器、环境变量和受控本地文件三种部署方式；
// 该入口仅供授权工具使用。客户端 RuntimeSecurityGuard 会拒绝误打包的私钥文件，
// 以及名为 AuthorizationPrivateKeyProvider 的私钥提供器。
internal static class AuthorizationKeyProvider
{
    private const string PrivateKeyEnvironmentVariable = "ABB_RELAYS_AUTH_PRIVATE_KEY_BASE64";
    private const string LegacyPrivateKeyEnvironmentVariable = "REX615_AUTH_PRIVATE_KEY_BASE64";
    private const string PrivateKeyFileName = "authorization-private-key.txt";

    public static string PrivateKeyXmlBase64 => LoadPrivateKey();

    private static string LoadPrivateKey()
    {
        // 查找顺序体现部署优先级：受构建控制的嵌入提供器最高，其次是当前进程环境，
        // 最后才读取磁盘兼容路径。找到首个非空值后立即停止，避免多个来源含义不清。
        var embedded = LoadEmbeddedPrivateKey();
        if (!string.IsNullOrWhiteSpace(embedded))
        {
            return embedded.Trim();
        }

        foreach (var environmentVariable in CandidateEnvironmentVariables())
        {
            var environmentValue = Environment.GetEnvironmentVariable(environmentVariable);
            if (!string.IsNullOrWhiteSpace(environmentValue))
            {
                return environmentValue.Trim();
            }
        }

        foreach (var path in CandidateKeyPaths())
        {
            if (!File.Exists(path))
            {
                continue;
            }

            var fileValue = File.ReadAllText(path).Trim();
            if (!string.IsNullOrWhiteSpace(fileValue))
            {
                return fileValue;
            }
        }

        throw new InvalidOperationException(
            $"未配置授权签名私钥。请设置环境变量 {PrivateKeyEnvironmentVariable}，或在授权工具目录放置 {PrivateKeyFileName}。" +
            $"兼容旧环境变量 {LegacyPrivateKeyEnvironmentVariable}。");
    }

    private static string? LoadEmbeddedPrivateKey()
    {
        // 通过反射兼容新旧授权工具命名空间，使私钥实现文件可保持为仅本地文件，
        // 主项目无需在公开源码中静态引用包含秘密的具体类型。
        foreach (var typeName in new[]
                 {
                     "AbbRelaysAuthorizationTool.AuthorizationPrivateKeyProvider",
                     "Rex615AuthorizationTool.AuthorizationPrivateKeyProvider"
                 })
        {
            var providerType = typeof(AuthorizationKeyProvider).Assembly.GetType(typeName, throwOnError: false);
            var field = providerType?.GetField("PrivateKeyXmlBase64", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (field?.GetValue(null) is string value && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static IEnumerable<string> CandidateEnvironmentVariables()
    {
        yield return PrivateKeyEnvironmentVariable;
        yield return LegacyPrivateKeyEnvironmentVariable;
    }

    private static IEnumerable<string> CandidateKeyPaths()
    {
        // 程序目录/当前目录支持便携授权站，本地应用数据目录支持不随程序升级覆盖的长期配置；
        // 这些路径都不应包含在客户端安装包或源码提交中。
        yield return Path.Combine(AppContext.BaseDirectory, PrivateKeyFileName);
        yield return Path.Combine(Environment.CurrentDirectory, PrivateKeyFileName);
        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ZikuanWang",
            "ABB Relays Authorization Tool",
            PrivateKeyFileName);
        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ZikuanWang",
            "REX615 Authorization Tool",
            PrivateKeyFileName);
    }
}
