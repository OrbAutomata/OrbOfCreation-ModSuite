#if SERVICE_CYCLE_PROFILE
using System;
using JObject = OrbAutomata.GameMcp.GameMcpObjectBuilder;

namespace OrbAutomata.GameMcp;

internal static class GameMcpTargetingProjection
{
    internal static GameMcpValue Project(in TargetingSubmission submission)
    {
        var result = new JObject();
        if (submission.Verified && submission.SubmittedTarget != Guid.Empty)
            result["submittedTargetUuid"] = submission.SubmittedTarget.ToString("D");
        if (submission.Verified) return result.Freeze();
        if (submission.CallOutcome.MutationAttempts > 0)
            result["missingOutcome"] = "target request settlement";
        return result.Freeze();
    }

    internal static Guid SubmittedTarget(GameMcpValue? value)
    {
        if (value is not GameMcpObject obj) return Guid.Empty;
        for (var index = 0; index < obj.Properties.Count; index++)
        {
            var property = obj.Properties[index];
            if (property.Name == "submittedTargetUuid" &&
                property.Value is GameMcpScalar scalar && scalar.Value is string text &&
                Guid.TryParse(text, out var id)) return id;
        }
        return Guid.Empty;
    }
}
#endif
