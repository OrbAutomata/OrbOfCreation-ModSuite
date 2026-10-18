using System;
using System.Collections.Generic;
using OrbMentor;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.Tests.Services.Mentor.Runtime.ServiceCycle;

/// <summary>
/// The bridge is process-global and a grant's suppression scope silences it, so these run alone.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class MentorMasteryHookCollection
{
    public const string Name = "Mentor mastery hooks";
}

[Collection(MentorMasteryHookCollection.Name)]
public sealed class MentorMasteryPatchBridgeTests
{
    private readonly MentorMasteryEventJournal _journal = new();
    private readonly List<string> _failures = new();

    [Fact]
    public void AnEarnedSpellGainIsJournaledWithTheSourcesStableIdentityAndMastery()
    {
        Install();
        var spell = Spell();

        MentorSpellMasteryPatch.Postfix(spell, new BigDouble(5));

        var observations = Collect().MasteryExperience;
        Assert.Equal(1, observations.Count);
        var observation = observations[0];
        Assert.Equal(MasteryExperienceDomain.Spell, observation.Domain);
        Assert.Equal(Guid.Parse(spell.uuid), observation.SourceId);
        Assert.Equal(3, observation.SourceMastery);
        Assert.True(observation.SourceEligible);
        Assert.Empty(_failures);
    }

    [Fact]
    public void AnEquippedArtifactTickJournalsTheGainItsOwnContainerReceived()
    {
        Install();
        var equipment = new EquipmentSO { uuid = Guid.NewGuid().ToString("D"), masteryLevel = 2 };

        MentorArtifactTickPatch.Prefix(equipment);
        MentorArtifactExperiencePatch.Prefix(equipment.GetExperienceElement(), new BigDouble(4));
        Assert.Null(MentorArtifactTickPatch.Finalizer(null));

        var observations = Collect().MasteryExperience;
        Assert.Equal(1, observations.Count);
        var observation = observations[0];
        Assert.Equal(MasteryExperienceDomain.Artifact, observation.Domain);
        Assert.Equal(Guid.Parse(equipment.uuid), observation.SourceId);
        Assert.Equal(4d, observation.Amount.ToDouble());
        Assert.Empty(_failures);
    }

    [Fact]
    public void Every_missing_member_leaves_the_mastery_hooks_recording_nothing()
    {
        foreach (var missing in MentorMasteryObservationBindings.ContractIds)
        {
            _failures.Clear();
            Install(id => id != missing);

            MentorSpellMasteryPatch.Postfix(Spell(), new BigDouble(5));

            Assert.True(
                _failures.Count == 1 && _failures[0].Contains(missing, StringComparison.Ordinal),
                missing + " did not disable the set: " + string.Join(" | ", _failures));
            Assert.Equal(0, _journal.Count);
        }
    }

    [Fact]
    public void AnUnexpectedThrowInsideAMasteryHookIsLoggedOnceAndNeverReachesTheGame()
    {
        Install();
        var spell = Spell();
        spell.DiscoveredReadFailure = new NullReferenceException("injected discovery read failure");

        MentorSpellMasteryPatch.Postfix(spell, new BigDouble(5));
        MentorSpellMasteryPatch.Postfix(spell, new BigDouble(5));

        Assert.Contains("injected discovery read failure", Assert.Single(_failures), StringComparison.Ordinal);
        Assert.Equal(0, _journal.Count);
    }

    [Fact]
    public void AnExpectedNativeFailureInsideAMasteryHookDropsThatObservationQuietly()
    {
        Install();
        var spell = Spell();
        spell.DiscoveredReadFailure = new InvalidOperationException("injected discovery read failure");

        MentorSpellMasteryPatch.Postfix(spell, new BigDouble(5));

        Assert.Empty(_failures);
        Assert.Equal(0, _journal.Count);
    }

    private void Install(Func<string, bool>? includeContract = null) =>
        MentorMasteryPatchBridge.Install(_journal, static () => 7, _failures.Add, includeContract: includeContract);

    private static SpellRecipeSO Spell() =>
        new() { uuid = Guid.NewGuid().ToString("D"), discovered = true, masteryLevel = 3 };

    private GameWorldState Collect()
    {
        var collector = new GameWorldCollector(_ => null, () => 0.02d, _journal);
        var frame = new GameWorldCycleFrame { CollectedAtEpoch = 7 };
        collector.Collect(frame);
        return GameWorldFrameDeriver.Build(frame);
    }
}
