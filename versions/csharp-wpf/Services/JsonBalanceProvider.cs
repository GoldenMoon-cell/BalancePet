using System.IO;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using BalancePet.Wpf.Models;

namespace BalancePet.Wpf.Services;

public sealed class JsonBalanceProvider(HttpClient http)
{
    public async Task<BalanceSnapshot> FetchAsync(PetSettings settings, string token, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, settings.Endpoint);
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.UserAgent.ParseAdd("BalancePet-CSharp/1.0");
        if (!string.IsNullOrWhiteSpace(token))
        {
            if (settings.AuthMode.Equals("x-api-key", StringComparison.OrdinalIgnoreCase))
                request.Headers.TryAddWithoutValidation("x-api-key", token);
            else if (settings.AuthMode.Equals("custom", StringComparison.OrdinalIgnoreCase))
                request.Headers.TryAddWithoutValidation(settings.HeaderName, token);
            else if (settings.AuthMode.Equals("authorization", StringComparison.OrdinalIgnoreCase))
                request.Headers.TryAddWithoutValidation("Authorization", NormalizeAuthorization(token));
            else
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", StripBearer(token));
                if (settings.AuthMode.Equals("websee-session", StringComparison.OrdinalIgnoreCase))
                {
                    var endpoint = new Uri(settings.Endpoint);
                    request.Headers.Referrer = new Uri(endpoint.GetLeftPart(UriPartial.Authority) + "/dashboard");
                    request.Headers.TryAddWithoutValidation("X-User-UI-Request", "1");
                    request.Headers.AcceptLanguage.ParseAdd("zh-CN,zh;q=0.9,en;q=0.8");
                }
            }
        }

        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"HTTP {(int)response.StatusCode} {response.ReasonPhrase}: {detail[..Math.Min(detail.Length, 180)]}");
        }
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var value = ReadPath(document.RootElement, settings.BalancePath);
        if (!value.HasValue || !TryParseAmount(value.Value, out var amount))
            throw new InvalidDataException($"JSON path not found: {settings.BalancePath}");
        return new BalanceSnapshot(amount, settings.Currency, DateTimeOffset.Now);
    }

    private static string StripBearer(string token) => token.Trim().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? token.Trim()[7..].Trim() : token.Trim();

    private static string NormalizeAuthorization(string token)
    {
        var value = token.Trim();
        return value.Contains(' ', StringComparison.Ordinal) ? value : $"Bearer {value}";
    }

    private static JsonElement? ReadPath(JsonElement root, string path)
    {
        var current = root;
        foreach (var part in path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (current.ValueKind == JsonValueKind.Object)
            {
                if (!current.TryGetProperty(part, out current)) return null;
            }
            else if (current.ValueKind == JsonValueKind.Array && int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var index))
            {
                if (index < 0 || index >= current.GetArrayLength()) return null;
                current = current[index];
            }
            else return null;
        }
        return current;
    }

    private static bool TryParseAmount(JsonElement value, out double amount)
    {
        var text = value.ValueKind switch
        {
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.String => value.GetString() ?? "",
            _ => ""
        };
        text = text.Trim().Replace(",", "", StringComparison.Ordinal)
            .Replace("$", "", StringComparison.Ordinal)
            .Replace("¥", "", StringComparison.Ordinal);
        foreach (var symbol in new[] { "USD", "CNY", "RMB" }) text = text.Replace(symbol, "", StringComparison.OrdinalIgnoreCase);
        return double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out amount);
    }
}

internal static class JsonElementExtensions
{
    public static string GetStringOrNumber(this JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Number => value.GetRawText(),
        JsonValueKind.String => value.GetString() ?? "",
        _ => ""
    };
}
