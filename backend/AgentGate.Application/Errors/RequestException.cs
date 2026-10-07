namespace AgentGate.Application.Errors;

public class RequestException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
