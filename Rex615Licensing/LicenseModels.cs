namespace Rex615Licensing;

public sealed record LicenseRequest(
    string FileType,
    int Version,
    string ProductId,
    string RequestId,
    string MachineId,
    string MachineName,
    string UserName,
    DateTimeOffset CreatedAt);

public sealed record LicenseActivationPayload(
    string FileType,
    int Version,
    string ProductId,
    string RequestId,
    string MachineId,
    string MachineName,
    string LicensedTo,
    DateTimeOffset IssuedAt,
    DateTimeOffset? ExpiresAt);

public sealed record SignedLicenseActivation(
    LicenseActivationPayload Payload,
    string Signature);

public sealed record LicenseStatus(
    bool IsLicensed,
    string Message,
    LicenseActivationPayload? Activation,
    string LicensePath);

public sealed record EncryptedLicenseEnvelope(
    string Format,
    int Version,
    string Nonce,
    string CipherText,
    string Tag);
