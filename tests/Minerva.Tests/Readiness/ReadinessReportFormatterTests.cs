using Microsoft.Extensions.Logging;
using Minerva.Readiness;

namespace Minerva.Tests.Readiness;

[Trait("Category", "Readiness")]
public class ReadinessReportFormatterTests
{
    [Fact]
    public void LogReport_LogsOneErrorPerFailureAndOneSummary()
    {
        var logger = new RecordingLogger<ReadinessReportFormatterTests>();
        var report = new ReadinessReport(
        [
            new ReadinessCheckResult("a", ReadinessCategory.Storage, true,  "OK",                   null,        null),
            new ReadinessCheckResult("b", ReadinessCategory.Embedding, false, "MINERVA.B.FAIL",     "msg-b",     "fix-b"),
            new ReadinessCheckResult("c", ReadinessCategory.Llm,       false, "MINERVA.C.FAIL",     "msg-c",     "fix-c"),
        ]);

        ReadinessReportFormatter.LogReport(logger, report);

        var errors = logger.Entries.Where(e => e.Level == LogLevel.Error).ToList();
        Assert.Equal(2, errors.Count);
        Assert.Contains(errors, e => e.Message.Contains("'b'") && e.Message.Contains("MINERVA.B.FAIL") && e.Message.Contains("msg-b") && e.Message.Contains("fix-b"));
        Assert.Contains(errors, e => e.Message.Contains("'c'") && e.Message.Contains("MINERVA.C.FAIL") && e.Message.Contains("msg-c") && e.Message.Contains("fix-c"));

        var summaries = logger.Entries.Where(e => e.Level == LogLevel.Information).ToList();
        var summary = Assert.Single(summaries);
        Assert.Contains("1/3", summary.Message);
    }

    [Fact]
    public void LogReport_AllPass_LogsOnlySummary()
    {
        var logger = new RecordingLogger<ReadinessReportFormatterTests>();
        var report = new ReadinessReport(
        [
            new ReadinessCheckResult("a", ReadinessCategory.Storage, true, "OK", null, null),
        ]);

        ReadinessReportFormatter.LogReport(logger, report);

        Assert.DoesNotContain(logger.Entries, e => e.Level == LogLevel.Error);
        var summary = Assert.Single(logger.Entries, e => e.Level == LogLevel.Information);
        Assert.Contains("1/1", summary.Message);
    }
}
