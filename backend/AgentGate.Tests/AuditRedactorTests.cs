using System.Text.Json;
using AgentGate.Application.Audit;

namespace AgentGate.Tests;

public sealed class AuditRedactorTests
{
    [Fact]
    public void RedactsNestedObjectsAndArraysWithoutChangingSafeFields()
    {
        var redactor = new SensitiveDataRedactor();
        var result = JsonSerializer.Deserialize<JsonElement>(redactor.Redact("""
            {"amount":750,"children":[{"access_token":"test-access","Authorization":"Bearer test","safe":true}],"nested":{"api-key":"test-key","bank_account":"123","creditCardNumber":"456","password":"test-password"}}
            """));
        Assert.Equal(750, result.GetProperty("amount").GetInt32());
        Assert.True(result.GetProperty("children")[0].GetProperty("safe").GetBoolean());
        Assert.DoesNotContain("test-access", result.ToString()); Assert.DoesNotContain("test-password", result.ToString()); Assert.DoesNotContain("test-key", result.ToString());
        Assert.Equal("[REDACTED]", result.GetProperty("nested").GetProperty("bank_account").GetString());
    }
    [Theory]
    [InlineData("refreshToken")][InlineData("client_secret")][InlineData("Cookie")][InlineData("private-key")][InlineData("cvv")][InlineData("IBAN")][InlineData("routingNumber")][InlineData("keyHash")]
    public void RecognizesCredentialAndFinancialFieldVariants(string field)
    {
        var json = JsonSerializer.Serialize(new Dictionary<string, string> { [field] = "synthetic-sensitive-value" });
        Assert.DoesNotContain("synthetic-sensitive-value", new SensitiveDataRedactor().Redact(json));
    }
}
