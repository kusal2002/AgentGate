using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AgentGate.Application.Errors;

namespace AgentGate.Application.Actions;

public sealed record CanonicalActionPayload(string Parameters, string Context, string Hash);
public static class ActionPayload
{
    public static CanonicalActionPayload ValidateAndCanonicalize(EvaluateActionRequest request)
    {
        if (!Identifier(request.Action, 100) || request.Resource is null || !Identifier(request.Resource.Type, 100)
            || !Text(request.Resource.Id, 200) || !Text(request.IdempotencyKey, 200))
            throw new RequestException(400, "Provide action, resource type/id, and an idempotency key within their length limits.");
        var parameters = CanonicalizeObject(request.Parameters, "Parameters");
        var context = request.Context.ValueKind == JsonValueKind.Undefined ? "{}" : CanonicalizeObject(request.Context, "Context");
        if (request.Action == "refund")
        {
            if (!request.Parameters.TryGetProperty("amount", out var amount) || amount.ValueKind != JsonValueKind.Number
                || !amount.TryGetDecimal(out var value) || value <= 0
                || !request.Parameters.TryGetProperty("currency", out var currency) || currency.ValueKind != JsonValueKind.String
                || !Regex.IsMatch(currency.GetString() ?? "", "^[A-Z]{3}$"))
                throw new RequestException(400, "Refund parameters require a positive decimal amount and a three-letter uppercase currency.");
        }
        // Serialize separate fields as an array, avoiding ambiguous concatenation.
        var canonical = JsonSerializer.Serialize(new[] { request.Action, request.Resource.Type, request.Resource.Id, parameters, context });
        return new(parameters, context, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))));
    }
    private static bool Identifier(string? value, int length) => Text(value, length) && Regex.IsMatch(value!, "^[a-z][a-z0-9_.:-]*$");
    private static bool Text(string? value, int length) => !string.IsNullOrWhiteSpace(value) && value.Length <= length && value == value.Trim() && !value.Any(char.IsControl);
    public static string CanonicalizeObject(JsonElement value, string name)
    {
        if (value.ValueKind != JsonValueKind.Object || Encoding.UTF8.GetByteCount(value.GetRawText()) > 32_768)
            throw new RequestException(400, $"{name} must be a JSON object of at most 32 KiB.");
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) WriteCanonical(writer, value, 0);
        return Encoding.UTF8.GetString(stream.ToArray());
    }
    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement value, int depth)
    {
        if (depth > 16) throw new RequestException(400, "Payload nesting exceeds 16 levels.");
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                var properties = value.EnumerateObject().OrderBy(x => x.Name, StringComparer.Ordinal).ToArray();
                if (properties.Select(x => x.Name).Distinct(StringComparer.Ordinal).Count() != properties.Length)
                    throw new RequestException(400, "Duplicate JSON properties are not supported.");
                foreach (var property in properties)
                {
                    if (property.Name.Contains('\0')) throw new RequestException(400, "JSON property names cannot contain null characters.");
                    writer.WritePropertyName(property.Name); WriteCanonical(writer, property.Value, depth + 1);
                }
                writer.WriteEndObject(); break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in value.EnumerateArray()) WriteCanonical(writer, item, depth + 1);
                writer.WriteEndArray(); break;
            case JsonValueKind.Number:
                // Decimal formatting removes insignificant zeroes (750 and 750.0 are the same request).
                if (!value.TryGetDecimal(out var number)) throw new RequestException(400, "Numbers must fit within decimal precision.");
                var formatted = number.ToString("G29", System.Globalization.CultureInfo.InvariantCulture);
                if (NormalizeNumber(value.GetRawText()) != NormalizeNumber(formatted))
                    throw new RequestException(400, "Numbers must fit within decimal precision without rounding.");
                writer.WriteRawValue(formatted); break;
            case JsonValueKind.String:
                var text = value.GetString();
                if (text?.Contains('\0') == true) throw new RequestException(400, "JSON strings cannot contain null characters.");
                writer.WriteStringValue(text); break;
            default: value.WriteTo(writer); break;
        }
    }
    private static string NormalizeNumber(string raw)
    {
        var parts = raw.ToLowerInvariant().Split('e');
        var exponent = 0;
        if (parts.Length == 2 && (!int.TryParse(parts[1], out exponent) || exponent is < -1000 or > 1000))
            throw new RequestException(400, "Number exponent is outside the supported range.");
        var mantissa = parts[0];
        var negative = mantissa.StartsWith('-');
        if (negative) mantissa = mantissa[1..];
        var point = mantissa.IndexOf('.');
        if (point >= 0) { exponent -= mantissa.Length - point - 1; mantissa = mantissa.Replace(".", ""); }
        mantissa = mantissa.TrimStart('0');
        if (mantissa.Length == 0) return "0";
        var trimmed = mantissa.TrimEnd('0');
        exponent += mantissa.Length - trimmed.Length;
        return (negative ? "-" : "") + trimmed + "e" + exponent.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
