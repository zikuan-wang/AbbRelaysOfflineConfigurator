using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace AbbRelaysOfflineConfigurator.Services;

public sealed record OnlineValidationResult(bool IsValid, string? OrderingNumber, string? CompositionCode, string Message);
public sealed record LegacyOnlineConversionResult(bool IsValid, string? CompositionCode, string Message);

// ABB 在线接口的轻量适配层：只负责构造请求、兼容响应形态和规范化 PCL 后缀。
// 它不持有界面状态；请求期间选择是否已变化、结果是否仍可应用，由调用它的 ViewModel 判断。
public sealed class OnlineValidationService
{
    private static readonly Uri PricesEndpoint = new("https://relays.protection-control.abb/api/Prices");
    private static readonly Uri LegacyConvertEndpoint = new("https://relays.protection-control.abb/api/Products/ConvertCode");
    private static readonly HttpClient HttpClient = new()
    {
        // 复用单例客户端避免频繁建立连接；统一超时限制防止界面任务无限等待外部服务。
        Timeout = TimeSpan.FromSeconds(30)
    };

    public async Task<OnlineValidationResult> ValidateAsync(
        string combinationCode,
        CancellationToken cancellationToken = default)
    {
        // Prices 接口以 multipart 字段接收组合代码；GenerateOrderingCodes=true 才会返回可导出的订货号。
        using var request = new HttpRequestMessage(HttpMethod.Post, PricesEndpoint);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.ParseAdd("ABBRelaysOfflineConfigurator/1.0");

        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(combinationCode), "OrderingCodes");
        content.Add(new StringContent("true"), "GenerateOrderingCodes");
        content.Add(new StringContent("false"), "GetLeadTime");
        request.Content = content;

        // HTTP 非成功状态转换为业务结果；网络异常、取消和无法解析的成功响应继续抛出，
        // 由上层按具体工作流更新忙碌状态和错误文案。
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

    public static string LocalizeMessage(string message, bool english)
    {
        // 仅翻译本应用和接口适配层已知的状态前缀，未知服务器消息原样保留，
        // 避免语言切换时丢失对诊断有价值的原始信息。
        if (string.IsNullOrWhiteSpace(message))
        {
            return "";
        }

        var value = message.Trim();
        if (!english)
        {
            return value switch
            {
                "Not checked" => "未校验",
                "Checking online..." => "在线校验中...",
                "Online check passed" => "在线校验通过",
                "Combination code is invalid." => "组合代码错误",
                "Order code is invalid, or no ordering number was returned." => "订货号错误，或未返回订货号。",
                "Combination code is invalid, or no ordering number was returned." => "组合代码错误，或未返回订货号。",
                "Reverse lookup in progress..." => "订货号反查中...",
                "Reverse lookup passed" => "订货号反查通过",
                "Reverse lookup failed" => "订货号反查失败",
                "Online conversion passed." => "在线转换通过。",
                "Online conversion returned no content." => "在线转换未返回内容。",
                "Online conversion did not return a REX615 combination code." => "在线转换未返回 REX615 组合代码。",
                "Online check returned no product information." => "在线校验未返回产品信息。",
                _ when value.StartsWith("Online check failed:", StringComparison.OrdinalIgnoreCase) =>
                    "在线校验失败：" + value["Online check failed:".Length..].Trim(),
                _ when value.StartsWith("Order number reverse lookup failed:", StringComparison.OrdinalIgnoreCase) =>
                    "订货号反查失败：" + value["Order number reverse lookup failed:".Length..].Trim(),
                _ when value.StartsWith("Online conversion failed:", StringComparison.OrdinalIgnoreCase) =>
                    "在线转换失败：" + value["Online conversion failed:".Length..].Trim(),
                _ => value
            };
        }

        return value switch
        {
            "未校验" => "Not checked",
            "在线校验中..." => "Checking online...",
            "在线校验通过。" or "在线校验通过" => "Online check passed",
            "组合代码错误" => "Combination code is invalid.",
            "订货号错误，或未返回订货号。" => "Order code is invalid, or no ordering number was returned.",
            "组合代码错误，或未返回订货号。" => "Combination code is invalid, or no ordering number was returned.",
            "订货号反查中..." => "Reverse lookup in progress...",
            "订货号反查通过" => "Reverse lookup passed",
            "订货号反查失败" => "Reverse lookup failed",
            "在线转换通过。" => "Online conversion passed.",
            "在线转换未返回内容。" => "Online conversion returned no content.",
            "在线转换未返回 REX615 组合代码。" => "Online conversion did not return a REX615 combination code.",
            "在线校验未返回产品信息。" => "Online check returned no product information.",
            _ when value.StartsWith("在线校验失败：", StringComparison.OrdinalIgnoreCase) =>
                "Online check failed: " + value["在线校验失败：".Length..].Trim(),
            _ when value.StartsWith("订货号反查失败：", StringComparison.OrdinalIgnoreCase) =>
                "Order number reverse lookup failed: " + value["订货号反查失败：".Length..].Trim(),
            _ when value.StartsWith("在线转换失败：", StringComparison.OrdinalIgnoreCase) =>
                "Online conversion failed: " + value["在线转换失败：".Length..].Trim(),
            _ => value
        };
    }

    public async Task<LegacyOnlineConversionResult> ConvertLegacyCodeAsync(
        string orderingCode,
        CancellationToken cancellationToken = default)
    {
        // 旧订货号作为查询参数发送，必须转义后再拼入 URI，避免其中的保留字符改变请求结构。
        var uri = new Uri($"{LegacyConvertEndpoint}?orderingCode={Uri.EscapeDataString(orderingCode.Trim())}");
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.ParseAdd("ABBRelaysOfflineConfigurator/1.0");

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
        // ABB 反查与正向校验共用 Prices 接口；客户端先补齐当前 PCL 后缀，
        // 防止无版本订货号被服务端按错误的默认产品连接级别解释。
        var normalizedOrderingNumber = NormalizeOrderingNumber(orderingNumber, version);
        using var request = new HttpRequestMessage(HttpMethod.Post, PricesEndpoint);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.ParseAdd("ABBRelaysOfflineConfigurator/1.0");

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
            // 历史接口在不同部署中可能返回 JSON 字符串，也可能返回带 orderingCode/compositionCode 的对象。
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
            // 再兼容纯文本或被引号包裹的响应；只有明确的 REX615 前缀才作为成功结果接受。
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
        // 当前工作流一次只提交一个代码，因此只消费 products[0]；空数组表示接口未识别该产品。
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

        // 部分服务响应遗漏订货号的 PCL 后缀，从返回组合代码和原始请求中推断后补齐，
        // 使后续导出与反查始终携带同一版本语义。
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
        if (HasPclSuffix(value))
        {
            return value;
        }

        var normalizedVersion = string.IsNullOrWhiteSpace(version) ? "PCL1" : version.Trim().ToUpperInvariant();
        if (!IsKnownPclVersion(normalizedVersion))
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
        if (HasPclSuffix(value))
        {
            return value;
        }

        var version = InferPclVersion(versionSource);
        return string.IsNullOrWhiteSpace(version) ? value : $"{value}_{version}";
    }

    private static string? InferPclVersion(string? versionSource)
    {
        var source = versionSource ?? "";
        foreach (var version in new[] { "PCL7", "PCL6", "PCL5", "PCL3", "PCL2", "PCL1" })
        {
            if (source.Contains(version, StringComparison.OrdinalIgnoreCase))
            {
                return version;
            }
        }

        return null;
    }

    private static bool HasPclSuffix(string value) =>
        value.EndsWith("_PCL1", StringComparison.OrdinalIgnoreCase) ||
        value.EndsWith("_PCL2", StringComparison.OrdinalIgnoreCase) ||
        value.EndsWith("_PCL3", StringComparison.OrdinalIgnoreCase) ||
        value.EndsWith("_PCL5", StringComparison.OrdinalIgnoreCase) ||
        value.EndsWith("_PCL6", StringComparison.OrdinalIgnoreCase) ||
        value.EndsWith("_PCL7", StringComparison.OrdinalIgnoreCase);

    private static bool IsKnownPclVersion(string value) =>
        value is "PCL1" or "PCL2" or "PCL3" or "PCL5" or "PCL6" or "PCL7";
}
