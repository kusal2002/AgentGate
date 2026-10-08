using System.Text.Json;
using System.Text.Json.Nodes;

namespace AgentGate.Application.Audit;

public sealed class SensitiveDataRedactor : ISensitiveDataRedactor
{
    public string Redact(string json)
    {
        var node = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { MaxDepth = 64 });
        Visit(node);
        return node?.ToJsonString() ?? "null";
    }
    private static void Visit(JsonNode? node)
    {
        if (node is JsonObject obj)
            foreach (var pair in obj.ToArray())
                if (Sensitive(pair.Key)) obj[pair.Key] = "[REDACTED]";
                else Visit(pair.Value);
        else if (node is JsonArray array)
            foreach (var item in array) Visit(item);
    }
    private static bool Sensitive(string key)
    {
        var normalized = string.Concat(key.Where(char.IsLetterOrDigit)).ToLowerInvariant();
        return normalized.Contains("password") || normalized.Contains("secret") || normalized.Contains("token")
            || normalized.Contains("apikey") || normalized.Contains("authorization") || normalized.Contains("cookie")
            || normalized.Contains("privatekey") || normalized.Contains("keyhash") || normalized.Contains("creditcard")
            || normalized.Contains("cardnumber") || normalized.Contains("bankaccount") || normalized.Contains("routingnumber")
            || normalized is "cvv" or "cvc" or "iban" or "pan";
    }
}
