using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Rex615OfflineConfigurator.Services;

public sealed record OnlineValidationResult(bool IsValid, string? OrderingNumber, string? CompositionCode, string Message);
public sealed record LegacyOnlineConversionResult(bool IsValid, string? CompositionCode, string Message);

public sealed class OnlineValidationService
{
    private static readonly Uri PricesEndpoint = new("https://relays.protection-control.abb/api/Prices");
    private static readonly Uri LegacyConvertEndpoint = new("https://relays.protection-control.abb/api/Products/ConvertCode");
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    public async Task<OnlineValidationResult> ValidateAsync(
        string combinationCode,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, PricesEndpoint);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.ParseAdd("REX615OfflineConfigurator/1.0");

        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(combinationCode), "OrderingCodes");
        content.Add(new StringContent("true"), "GenerateOrderingCodes");
        content.Add(new StringContent("false"), "GetLeadTime");
        request.Content = content;

        using var response = await HttpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return new OnlineValidationResult(
                false,
                null,
                null,
                $"在线校验失败：HTTP {(int)response.StatusCode}");
        }

        return ParseResponse(responseBody, combinationCode);
    }

    public async Task<LegacyOnlineConversionResult> ConvertLegacyCodeAsync(
        string orderingCode,
        CancellationToken cancellationToken = default)
    {
        var uri = new Uri($"{LegacyConvertEndpoint}?orderingCode={Uri.EscapeDataString(orderingCode.Trim())}");
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.ParseAdd("REX615OfflineConfigurator/1.0");

        using var response = await HttpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return new LegacyOnlineConversionResult(
                false,
                null,
                $"在线转换失败：HTTP {(int)response.StatusCode}");
        }

        return ParseLegacyConversionResponse(responseBody);
    }

    public async Task<OnlineValidationResult> ReverseLookupAsync(
        string orderingNumber,
        string version,
        CancellationToken cancellationToken = default)
    {
        var normalizedOrderingNumber = NormalizeOrderingNumber(orderingNumber, version);
        using var request = new HttpRequestMessage(HttpMethod.Post, PricesEndpoint);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.ParseAdd("REX615OfflineConfigurator/1.0");

        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(normalizedOrderingNumber), "OrderingCodes");
        content.Add(new StringContent("true"), "GenerateOrderingCodes");
        content.Add(new StringContent("false"), "GetLeadTime");
        request.Content = content;

        using var response = await HttpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return new OnlineValidationResult(
                false,
                normalizedOrderingNumber,
                null,
                $"订货号反查失败：HTTP {(int)response.StatusCode}");
        }

        return ParseResponse(responseBody, normalizedOrderingNumber);
    }

    private static LegacyOnlineConversionResult ParseLegacyConversionResponse(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return new LegacyOnlineConversionResult(false, null, "在线转换未返回内容。");
        }

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            string? compositionCode = null;

            if (document.RootElement.ValueKind == JsonValueKind.String)
            {
                compositionCode = document.RootElement.GetString();
            }
            else if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                compositionCode = document.RootElement.TryGetProperty("orderingCode", out var orderingCode)
                    ? orderingCode.GetString()
                    : null;
                compositionCode ??= document.RootElement.TryGetProperty("compositionCode", out var compositionCodeElement)
                    ? compositionCodeElement.GetString()
                    : null;
            }

            if (!string.IsNullOrWhiteSpace(compositionCode) &&
                compositionCode.StartsWith("REX615", StringComparison.OrdinalIgnoreCase))
            {
                return new LegacyOnlineConversionResult(true, compositionCode, "在线转换通过。");
            }
        }
        catch (JsonException)
        {
            var value = responseBody.Trim().Trim('"');
            if (value.StartsWith("REX615", StringComparison.OrdinalIgnoreCase))
            {
                return new LegacyOnlineConversionResult(true, value, "在线转换通过。");
            }
        }

        return new LegacyOnlineConversionResult(false, null, "在线转换未返回 REX615 组合代码。");
    }

    private static OnlineValidationResult ParseResponse(string responseBody, string versionSource)
    {
        using var document = JsonDocument.Parse(responseBody);
        if (!document.RootElement.TryGetProperty("products", out var products) ||
            products.ValueKind != JsonValueKind.Array ||
            products.GetArrayLength() == 0)
        {
            return new OnlineValidationResult(false, null, null, "在线校验未返回产品信息。");
        }

        var product = products[0];
        var isValid = product.TryGetProperty("validationResult", out var validationResult) &&
            validationResult.ValueKind == JsonValueKind.True;
        var orderingNumber = product.TryGetProperty("orderingCode", out var orderingCode)
            ? orderingCode.GetString()
            : null;
        var compositionCode = product.TryGetProperty("compositionCode", out var compositionCodeElement)
            ? compositionCodeElement.GetString()
            : null;

        orderingNumber = EnsureOrderingNumberVersionSuffix(orderingNumber, $"{compositionCode} {versionSource}");

        if (isValid && !string.IsNullOrWhiteSpace(orderingNumber))
        {
            return new OnlineValidationResult(true, orderingNumber, compositionCode, "在线校验通过。");
        }

        return new OnlineValidationResult(false, orderingNumber, compositionCode, "组合代码错误");
    }

    private static string NormalizeOrderingNumber(string orderingNumber, string version)
    {
        var value = orderingNumber.Trim();
        if (value.EndsWith("_PCL1", StringComparison.OrdinalIgnoreCase) ||
            value.EndsWith("_PCL2", StringComparison.OrdinalIgnoreCase))
        {
            return value;
        }

        var normalizedVersion = string.IsNullOrWhiteSpace(version) ? "PCL1" : version.Trim().ToUpperInvariant();
        if (normalizedVersion is not "PCL1" and not "PCL2")
        {
            normalizedVersion = "PCL1";
        }

        return $"{value}_{normalizedVersion}";
    }

    private static string? EnsureOrderingNumberVersionSuffix(string? orderingNumber, string? versionSource)
    {
        if (string.IsNullOrWhiteSpace(orderingNumber))
        {
            return orderingNumber;
        }

        var value = orderingNumber.Trim();
        if (value.EndsWith("_PCL1", StringComparison.OrdinalIgnoreCase) ||
            value.EndsWith("_PCL2", StringComparison.OrdinalIgnoreCase))
        {
            return value;
        }

        var version = InferPclVersion(versionSource);
        return $"{value}_{version}";
    }

    private static string InferPclVersion(string? versionSource)
    {
        var source = versionSource ?? "";
        if (source.Contains("PCL2", StringComparison.OrdinalIgnoreCase))
        {
            return "PCL2";
        }

        return "PCL1";
    }
}
