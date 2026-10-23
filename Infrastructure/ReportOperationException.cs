namespace JaliscoProblemsAPI.Infrastructure;

public sealed class ReportOperationException(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
}
