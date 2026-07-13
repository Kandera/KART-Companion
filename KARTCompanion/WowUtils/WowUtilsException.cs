namespace KARTCompanion.WowUtils;

public sealed class WowUtilsException : Exception
{
    public int? StatusCode { get; }

    public WowUtilsException(string message, int? statusCode = null, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
    }
}
