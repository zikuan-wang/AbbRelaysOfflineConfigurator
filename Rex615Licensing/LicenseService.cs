using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace Rex615Licensing;

public static class LicenseService
{
    public const string ProductId = "ZW_REX615_OFFLINE_CONFIGURATOR";
    public const string RequestFileType = "ZW_REX615_LICENSE_REQUEST";
    public const string ActivationFileType = "ZW_REX615_LICENSE_ACTIVATION";
    public const string RequestExtension = ".zwreq";
    public const string ActivationExtension = ".zwlic";

    private const int EnvelopeVersion = 1;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private static readonly byte[] AesKey = SHA256.HashData(Encoding.UTF8.GetBytes("zikuan-wang|REX615|offline-license|v1"));
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static string DefaultLicensePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ZikuanWang",
            "REX615 Offline Configurator",
            "license.zwlic");

    public static LicenseRequest CreateCurrentRequest()
    {
        var identity = WindowsIdentity.GetCurrent();
        return new LicenseRequest(
            RequestFileType,
            EnvelopeVersion,
            ProductId,
            Guid.NewGuid().ToString("N"),
            GetCurrentMachineId(),
            Environment.MachineName,
            identity?.Name ?? Environment.UserName,
            DateTimeOffset.Now);
    }

    public static string CreateRequestFileText(LicenseRequest request) =>
        EncryptToEnvelope(RequestFileType, JsonSerializer.SerializeToUtf8Bytes(request, JsonOptions));

    public static LicenseRequest ReadRequestFile(string path)
    {
        var bytes = DecryptEnvelope(File.ReadAllText(path, Encoding.UTF8), RequestFileType);
        var request = JsonSerializer.Deserialize<LicenseRequest>(bytes, JsonOptions)
            ?? throw new InvalidOperationException("授权申请文件内容为空。");
        if (!request.ProductId.Equals(ProductId, StringComparison.OrdinalIgnoreCase) ||
            !request.FileType.Equals(RequestFileType, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("授权申请文件不适用于本工具。");
        }

        return request;
    }

    public static string CreateActivationFileText(
        LicenseRequest request,
        string licensedTo,
        DateTimeOffset? expiresAt,
        string privateKeyXmlBase64)
    {
        if (!request.ProductId.Equals(ProductId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("授权申请文件不适用于本工具。");
        }

        var payload = new LicenseActivationPayload(
            ActivationFileType,
            EnvelopeVersion,
            ProductId,
            request.RequestId,
            request.MachineId,
            request.MachineName,
            string.IsNullOrWhiteSpace(licensedTo) ? request.UserName : licensedTo.Trim(),
            DateTimeOffset.Now,
            expiresAt);

        var payloadJson = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        using var rsa = RSA.Create();
        rsa.FromXmlString(DecodeXmlKey(privateKeyXmlBase64));
        var signature = Convert.ToBase64String(rsa.SignData(payloadJson, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        var activation = new SignedLicenseActivation(payload, signature);
        return EncryptToEnvelope(ActivationFileType, JsonSerializer.SerializeToUtf8Bytes(activation, JsonOptions));
    }

    public static LicenseStatus GetStatus(string publicKeyXmlBase64) =>
        GetStatus(DefaultLicensePath, publicKeyXmlBase64);

    public static LicenseStatus GetStatus(string licensePath, string publicKeyXmlBase64)
    {
        if (!File.Exists(licensePath))
        {
            return new LicenseStatus(false, "未激活。", null, licensePath);
        }

        try
        {
            var activation = ReadActivationFile(licensePath, publicKeyXmlBase64);
            if (!activation.Payload.MachineId.Equals(GetCurrentMachineId(), StringComparison.OrdinalIgnoreCase))
            {
                return new LicenseStatus(false, "激活文件不属于当前电脑。", activation.Payload, licensePath);
            }

            if (activation.Payload.ExpiresAt is { } expiresAt && expiresAt < DateTimeOffset.Now)
            {
                return new LicenseStatus(false, $"授权已过期：{expiresAt:yyyy-MM-dd}。", activation.Payload, licensePath);
            }

            var expireText = activation.Payload.ExpiresAt is { } value
                ? $"有效期至 {value:yyyy-MM-dd}"
                : "永久授权";
            return new LicenseStatus(true, $"已授权给 {activation.Payload.LicensedTo}，{expireText}。", activation.Payload, licensePath);
        }
        catch (Exception ex)
        {
            return new LicenseStatus(false, $"激活文件无效：{ex.Message}", null, licensePath);
        }
    }

    public static SignedLicenseActivation ReadActivationFile(string path, string publicKeyXmlBase64)
    {
        var bytes = DecryptEnvelope(File.ReadAllText(path, Encoding.UTF8), ActivationFileType);
        var activation = JsonSerializer.Deserialize<SignedLicenseActivation>(bytes, JsonOptions)
            ?? throw new InvalidOperationException("激活文件内容为空。");
        if (!activation.Payload.ProductId.Equals(ProductId, StringComparison.OrdinalIgnoreCase) ||
            !activation.Payload.FileType.Equals(ActivationFileType, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("激活文件不适用于本工具。");
        }

        var payloadJson = JsonSerializer.SerializeToUtf8Bytes(activation.Payload, JsonOptions);
        using var rsa = RSA.Create();
        rsa.FromXmlString(DecodeXmlKey(publicKeyXmlBase64));
        var signature = Convert.FromBase64String(activation.Signature);
        if (!rsa.VerifyData(payloadJson, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
        {
            throw new InvalidOperationException("激活文件签名校验失败。");
        }

        return activation;
    }

    public static void InstallActivationFile(string sourcePath, string publicKeyXmlBase64)
    {
        var activation = ReadActivationFile(sourcePath, publicKeyXmlBase64);
        if (!activation.Payload.MachineId.Equals(GetCurrentMachineId(), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("激活文件不属于当前电脑。");
        }

        var targetDirectory = Path.GetDirectoryName(DefaultLicensePath)
            ?? throw new InvalidOperationException("无法定位授权目录。");
        Directory.CreateDirectory(targetDirectory);
        File.Copy(sourcePath, DefaultLicensePath, true);
    }

    private static string EncryptToEnvelope(string format, byte[] plaintext)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipherText = new byte[plaintext.Length];
        var tag = new byte[TagSize];
        using var aes = new AesGcm(AesKey, TagSize);
        aes.Encrypt(nonce, plaintext, cipherText, tag);

        var envelope = new EncryptedLicenseEnvelope(
            format,
            EnvelopeVersion,
            Convert.ToBase64String(nonce),
            Convert.ToBase64String(cipherText),
            Convert.ToBase64String(tag));
        return JsonSerializer.Serialize(envelope, JsonOptions);
    }

    private static byte[] DecryptEnvelope(string fileText, string expectedFormat)
    {
        var envelope = JsonSerializer.Deserialize<EncryptedLicenseEnvelope>(fileText, JsonOptions)
            ?? throw new InvalidOperationException("文件格式错误。");
        if (!envelope.Format.Equals(expectedFormat, StringComparison.OrdinalIgnoreCase) ||
            envelope.Version != EnvelopeVersion)
        {
            throw new InvalidOperationException("文件类型或版本不匹配。");
        }

        var nonce = Convert.FromBase64String(envelope.Nonce);
        var cipherText = Convert.FromBase64String(envelope.CipherText);
        var tag = Convert.FromBase64String(envelope.Tag);
        var plaintext = new byte[cipherText.Length];
        using var aes = new AesGcm(AesKey, TagSize);
        aes.Decrypt(nonce, cipherText, tag, plaintext);
        return plaintext;
    }

    private static string DecodeXmlKey(string xmlBase64) =>
        Encoding.UTF8.GetString(Convert.FromBase64String(xmlBase64));

    private static string GetCurrentMachineId()
    {
        var identity = WindowsIdentity.GetCurrent();
        var raw = string.Join(
            "|",
            ProductId,
            Environment.MachineName,
            identity?.User?.Value ?? Environment.UserName,
            Environment.OSVersion.VersionString);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
    }
}
