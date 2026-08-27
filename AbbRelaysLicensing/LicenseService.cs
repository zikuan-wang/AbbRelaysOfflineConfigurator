using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace AbbRelaysLicensing;

// 授权文件协议的唯一入口：客户端生成机器绑定申请，授权端签发带 RSA 签名的激活载荷，
// 客户端再依次完成封装解密、验签、机器标识和有效期校验。调用方不应绕过本类直接信任文件内容。
public static class LicenseService
{
    public const string ProductId = "ZW_ABB_RELAYS_OFFLINE_CONFIGURATOR";
    public const string RequestFileType = "ZW_ABB_RELAYS_LICENSE_REQUEST";
    public const string ActivationFileType = "ZW_ABB_RELAYS_LICENSE_ACTIVATION";
    public const string RequestExtension = ".zwreq";
    public const string ActivationExtension = ".zwlic";

    private const int EnvelopeVersion = 1;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    // 固定 AES 密钥只用于文件封装和历史格式兼容，不是发行者身份凭据；
    // 客户端二进制中的固定密钥不能构成可信根，激活真实性必须由未随客户端分发的 RSA 私钥签名保证。
    private static readonly byte[] AesKey = SHA256.HashData(Encoding.UTF8.GetBytes("zikuan-wang|ABBRelaysOfflineConfigurator|offline-license|v1"));
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static string DefaultLicensePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ZikuanWang",
            "ABB Relays Offline Configurator",
            "license.zwlic");

    public static LicenseRequest CreateCurrentRequest()
    {
        // 请求标识用于关联一次签发记录；机器标识用于最终绑定。机器名和用户名仅供授权人员识别设备，
        // 不能替代 MachineId，也不会被客户端单独作为授权依据。
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
            !request.FileType.Equals(RequestFileType, StringComparison.OrdinalIgnoreCase) ||
            request.Version != EnvelopeVersion)
        {
            throw new InvalidOperationException("授权申请文件不适用于本工具。");
        }

        ValidateRequest(request);

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

        // 签名覆盖序列化后的完整载荷，任何机器、用户、签发时间或有效期字段被修改都会导致客户端验签失败。
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
            // ReadActivationFile 已完成文件类型、版本、RSA 签名和字段完整性校验；
            // 此处只处理与当前运行环境相关的机器绑定及有效期判定，并转换为适合界面展示的状态。
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
        // 校验顺序是协议边界的一部分：先验证加密封装，再核对载荷的产品、文件类型和版本，
        // 随后验证发行者签名，最后检查其余业务字段，防止调用方信任尚未完成校验的数据。
        var bytes = DecryptEnvelope(File.ReadAllText(path, Encoding.UTF8), ActivationFileType);
        var activation = JsonSerializer.Deserialize<SignedLicenseActivation>(bytes, JsonOptions)
            ?? throw new InvalidOperationException("激活文件内容为空。");
        if (!activation.Payload.ProductId.Equals(ProductId, StringComparison.OrdinalIgnoreCase) ||
            !activation.Payload.FileType.Equals(ActivationFileType, StringComparison.OrdinalIgnoreCase) ||
            activation.Payload.Version != EnvelopeVersion)
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

        ValidateActivationPayload(activation.Payload);
        return activation;
    }

    public static void InstallActivationFile(string sourcePath, string publicKeyXmlBase64)
    {
        // 只有通过签名校验且属于本机的文件才能复制到固定运行路径；覆盖安装用于续期或重新签发。
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
        // 每个文件使用独立随机 nonce；AAD 将产品、文件类型和协议版本绑定到认证标签，
        // 因而申请文件与激活文件不能仅替换外层 Format 后互相冒充。
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipherText = new byte[plaintext.Length];
        var tag = new byte[TagSize];
        using var aes = new AesGcm(AesKey, TagSize);
        aes.Encrypt(nonce, plaintext, cipherText, tag, BuildEnvelopeAad(format, EnvelopeVersion));

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
        try
        {
            aes.Decrypt(nonce, cipherText, tag, plaintext, BuildEnvelopeAad(envelope.Format, envelope.Version));
        }
        catch (CryptographicException)
        {
            // 兼容引入 AAD 绑定前生成的历史申请/激活文件。回退只省略 AAD，AES-GCM 标签仍会校验密文完整性；
            // 激活文件解密后还必须通过独立 RSA 签名校验，因此不能把兼容分支当作签名绕过路径。
            plaintext = new byte[cipherText.Length];
            aes.Decrypt(nonce, cipherText, tag, plaintext);
        }

        return plaintext;
    }

    private static byte[] BuildEnvelopeAad(string format, int version) =>
        Encoding.UTF8.GetBytes($"{ProductId}|{format}|{version}");

    private static void ValidateRequest(LicenseRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RequestId) ||
            string.IsNullOrWhiteSpace(request.MachineId) ||
            string.IsNullOrWhiteSpace(request.MachineName) ||
            string.IsNullOrWhiteSpace(request.UserName))
        {
            throw new InvalidOperationException("授权申请文件缺少必要信息。");
        }

        if (request.CreatedAt > DateTimeOffset.Now.AddMinutes(10))
        {
            throw new InvalidOperationException("授权申请文件创建时间异常。");
        }
    }

    private static void ValidateActivationPayload(LicenseActivationPayload payload)
    {
        if (string.IsNullOrWhiteSpace(payload.RequestId) ||
            string.IsNullOrWhiteSpace(payload.MachineId) ||
            string.IsNullOrWhiteSpace(payload.MachineName) ||
            string.IsNullOrWhiteSpace(payload.LicensedTo))
        {
            throw new InvalidOperationException("激活文件缺少必要授权信息。");
        }

        if (payload.IssuedAt > DateTimeOffset.Now.AddMinutes(10))
        {
            throw new InvalidOperationException("激活文件签发时间异常。");
        }

        if (payload.ExpiresAt is { } expiresAt && expiresAt <= payload.IssuedAt)
        {
            throw new InvalidOperationException("激活文件有效期异常。");
        }
    }

    private static string DecodeXmlKey(string xmlBase64) =>
        Encoding.UTF8.GetString(Convert.FromBase64String(xmlBase64));

    private static string GetCurrentMachineId()
    {
        // 机器标识同时绑定产品、Windows 机器名、当前用户 SID 和系统版本。
        // 因此更换 Windows 账户或显著变更系统环境可能需要重新申请授权，这是当前协议的预期行为。
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
