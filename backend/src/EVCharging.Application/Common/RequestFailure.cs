namespace EVCharging.Application.Common;

public sealed class RequestFailure(int status, string code, string message) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}
