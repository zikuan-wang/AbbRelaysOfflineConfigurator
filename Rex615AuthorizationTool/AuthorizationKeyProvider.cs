using System.IO;
using System.Reflection;

namespace Rex615AuthorizationTool;

internal static class AuthorizationKeyProvider
{
    private const string PrivateKeyEnvironmentVariable = "REX615_AUTH_PRIVATE_KEY_BASE64";
    private const string PrivateKeyFileName = "authorization-private-key.txt";

    public static string PrivateKeyXmlBase64 => LoadPrivateKey();

    private static string LoadPrivateKey()
    {
        var embedded = LoadEmbeddedPrivateKey();
        if (!string.IsNullOrWhiteSpace(embedded))
        {
            return embedded.Trim();
        }

        var environmentValue = Environment.GetEnvironmentVariable(PrivateKeyEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(environmentValue))
        {
            return environmentValue.Trim();
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
            $"未配置授权签名私钥。请设置环境变量 {PrivateKeyEnvironmentVariable}，或在授权工具目录放置 {PrivateKeyFileName}。");
    }

    private static string? LoadEmbeddedPrivateKey()
    {
        var providerType = typeof(AuthorizationKeyProvider).Assembly.GetType(
            "Rex615AuthorizationTool.AuthorizationPrivateKeyProvider",
            throwOnError: false);
        var field = providerType?.GetField("PrivateKeyXmlBase64", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        return field?.GetValue(null) as string;
    }

    private static IEnumerable<string> CandidateKeyPaths()
    {
        yield return Path.Combine(AppContext.BaseDirectory, PrivateKeyFileName);
        yield return Path.Combine(Environment.CurrentDirectory, PrivateKeyFileName);
        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ZikuanWang",
            "REX615 Authorization Tool",
            PrivateKeyFileName);
    }
}
