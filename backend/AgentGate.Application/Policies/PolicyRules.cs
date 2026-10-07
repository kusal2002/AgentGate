using System.Text.Json;
using System.Text.RegularExpressions;
using AgentGate.Application.Errors;
using AgentGate.Domain.Actions;

namespace AgentGate.Application.Policies;

public static class PolicyRules
{
    public static readonly string[] Operators = ["equals", "not_equals", "greater_than", "greater_than_or_equal", "less_than", "less_than_or_equal", "contains", "not_contains", "in", "not_in"];
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow };
    public static void Validate(PolicyRequest request)
    {
        if (!Text(request.Name, 100) || request.Description is null || request.Description.Length > 2000 || request.Description.Contains('\0')
            || !Identifier(request.ActionType) || request.Priority is < 0 or > 10000 || request.Conditions is null || request.Conditions.Length > 32)
            throw new RequestException(400, "Provide a policy name, description, action type, priority (0–10000), and at most 32 conditions.");
        var decision = Parse<ActionDecision>(request.Decision, "Decision");
        Parse<ActionRiskLevel>(request.RiskLevel, "Risk level");
        if (decision == ActionDecision.Review ? request.ReviewerRole is not ("Owner" or "Admin" or "Reviewer") : request.ReviewerRole is not null)
            throw new RequestException(400, "Review policies require Owner, Admin, or Reviewer. Other decisions must not specify a reviewer.");
        foreach (var condition in request.Conditions) ValidateCondition(condition);
        if (JsonSerializer.SerializeToUtf8Bytes(request.Conditions, JsonOptions).Length > 16_384)
            throw new RequestException(400, "Policy conditions must fit within 16 KiB.");
    }
    public static T Parse<T>(string? value, string name) where T : struct, Enum => value is not null
        && Enum.TryParse<T>(value, true, out var parsed) && Enum.IsDefined(parsed) && !int.TryParse(value, out _) ? parsed
        : throw new RequestException(400, $"Invalid {name}.");
    public static void ValidateCondition(PolicyCondition? condition)
    {
        if (condition is null || condition.Field is null || condition.Field.Length > 200 || !ValidField(condition.Field)
            || !Operators.Contains(condition.Operator, StringComparer.Ordinal)) throw new RequestException(400, "Invalid condition field or operator.");
        var value = condition.Value;
        if (condition.Operator is "in" or "not_in")
        {
            if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() is < 1 or > 100)
                throw new RequestException(400, "In/not_in requires 1–100 values of the same scalar type.");
            var items = value.EnumerateArray().ToArray();
            foreach (var item in items) ValidateScalar(item);
            if (items.Any(item => ScalarType(item) != ScalarType(items[0]))) throw new RequestException(400, "In/not_in values must share one type.");
        }
        else
        {
            ValidateScalar(value);
            if (condition.Operator is "greater_than" or "greater_than_or_equal" or "less_than" or "less_than_or_equal"
                && value.ValueKind != JsonValueKind.Number) throw new RequestException(400, "Ordering operators require a decimal number.");
            if (condition.Operator is "contains" or "not_contains" && (value.ValueKind != JsonValueKind.String || value.GetString()!.Length == 0))
                throw new RequestException(400, "Contains/not_contains requires a nonempty string.");
        }
    }
    public static int ScalarType(JsonElement value) => value.ValueKind is JsonValueKind.True or JsonValueKind.False ? (int)JsonValueKind.True : (int)value.ValueKind;
    private static void ValidateScalar(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String && value.GetString() is { } text && text.Length <= 1000 && !text.Contains('\0')) return;
        if (value.ValueKind is JsonValueKind.True or JsonValueKind.False) return;
        if (value.ValueKind == JsonValueKind.Number)
        {
            // Reuse exact numeric validation, including rejection of silent decimal rounding.
            AgentGate.Application.Actions.ActionPayload.CanonicalizeObject(JsonSerializer.SerializeToElement(new { value }), "Condition value");
            return;
        }
        throw new RequestException(400, "Condition values must be strings (at most 1000 characters), exact decimal numbers, or booleans.");
    }
    public static bool ValidField(string field) => field is "resource.type" or "resource.id" or "agent.id" or "agent.environment"
        || Regex.IsMatch(field, "^(parameters|context)\\.[a-zA-Z_][a-zA-Z0-9_]*(\\.[a-zA-Z_][a-zA-Z0-9_]*){0,7}$");
    private static bool Identifier(string? value) => Text(value, 100) && Regex.IsMatch(value!, "^[a-z][a-z0-9_.:-]*$");
    private static bool Text(string? value, int max) => !string.IsNullOrWhiteSpace(value) && value.Length <= max && value == value.Trim() && !value.Any(char.IsControl);
}
