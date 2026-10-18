using OrbAutomata;

namespace OrbModding.Tests.Services.AutoScribe;

internal static class AutoScribeActionHealthAssertions
{
    /// <summary>Observes the submission and says whether it entered a new failure state.</summary>
    internal static bool EntersFailure(this AutoScribeActionHealth health, in AutoScribeSubmission submission)
    {
        var revision = health.Revision;
        health.Observe(in submission);
        return health.HasFailure && health.Revision != revision;
    }
}
