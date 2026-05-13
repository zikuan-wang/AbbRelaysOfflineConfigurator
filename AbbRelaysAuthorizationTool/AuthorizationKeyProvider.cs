using System.IO;
using System.Reflection;

namespace AbbRelaysAuthorizationTool;

internal static class AuthorizationKeyProvider
{
    private const string PrivateKeyEnvironmentVariable = "ABB_RELAYS_AUTH_PRIVATE_KEY_BASE64";
    private const string LegacyPrivateKeyEnvironmentVariable = "REX615_AUTH_PRIVATE_KEY_BASE64";
    private const string PrivateKeyFileName = "authorization-private-key.txt";

    public static string PrivateKeyXmlBase64 => LoadPrivateKey();

    private static string LoadPrivateKey()
    {
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
