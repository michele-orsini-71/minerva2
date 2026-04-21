using NetArchTest.Rules;

namespace Minerva.ArchitectureTests;

internal static class ArchAssert
{
    public static void Passes(TestResult result, string ruleExplanation)
    {
        if (result.IsSuccessful) return;

        var failing = result.FailingTypeNames is { Count: > 0 }
            ? "  - " + string.Join("\n  - ", result.FailingTypeNames)
            : "  (no type names reported)";

        Assert.Fail(
            $"Architecture rule violated: {ruleExplanation}\n" +
            $"Failing types:\n{failing}\n" +
            "See docs/architecture.md for the layer model.");
    }
}
