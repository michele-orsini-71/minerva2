using Microsoft.Extensions.Logging;

namespace Minerva.Readiness;

public static class ReadinessReportFormatter
{
    public static void LogReport(ILogger logger, ReadinessReport report)
    {
        foreach (var result in report.Results.Where(r => !r.Passed))
        {
            logger.LogError(
                "Readiness check '{Name}' failed [{Code}]: {Message}. Remediation: {Remediation}",
                result.Name, result.Code, result.Message, result.Remediation);
        }

        var passed = report.Results.Count(r => r.Passed);
        logger.LogInformation("Readiness: {Passed}/{Total} checks passed", passed, report.Results.Count);
    }
}
