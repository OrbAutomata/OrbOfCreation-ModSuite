using System;
using OrbAutomata;
using Xunit;

namespace OrbModding.Tests.Services.RunTransition;

public sealed class AgentSettingsNormalizationTests : IDisposable
{
    public AgentSettingsNormalizationTests() => SettingsManager.instance = new SettingsManager();

    public void Dispose() => SettingsManager.instance = new SettingsManager();

    [Fact]
    public void A_load_leaves_the_three_settings_every_documented_verb_assumes()
    {
        SettingsManager.instance.enableQueueResearch.SetValue(false);
        SettingsManager.instance.cancellableSpells.SetValue(false);

        Assert.True(AgentSettingsNormalization.TryNormalize(out var reason, Resolve), reason);

        Assert.True(SettingsManager.IsResearchQueueMode());
        Assert.True(SettingsManager.CanCancelSpells());
        Assert.Equal("Scientific", SettingsManager.GetNumberDisplayOption());
    }

    /// <summary>
    /// A load that changes nothing writes nothing: the normalization is the settings dropdown's own
    /// write, and pressing a dropdown that already holds the value is not what a player does.
    /// </summary>
    [Fact]
    public void A_game_already_in_that_shape_is_left_alone()
    {
        Assert.True(AgentSettingsNormalization.TryNormalize(out _, Resolve));
        var queueWrites = SettingsManager.instance.enableQueueResearch.SetCalls;
        var notationWrites = SettingsManager.instance.numDisplay.SetCalls;

        Assert.True(AgentSettingsNormalization.TryNormalize(out var reason, Resolve), reason);

        Assert.Equal(queueWrites, SettingsManager.instance.enableQueueResearch.SetCalls);
        Assert.Equal(notationWrites, SettingsManager.instance.numDisplay.SetCalls);
    }

    [Fact]
    public void A_setting_that_does_not_stay_written_names_itself_rather_than_reporting_success()
    {
        SettingsManager.instance.cancellableSpells.SetValue(false);
        SettingsManager.instance.cancellableSpells.SuppressSet = true;

        Assert.False(AgentSettingsNormalization.TryNormalize(out var reason, Resolve));

        Assert.Contains("Cancellable Spells", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void An_incomplete_contract_set_is_fail_closed_and_names_the_missing_member()
    {
        foreach (var missing in AgentSettingsNormalization.ContractIds)
        {
            Assert.False(AgentSettingsNormalization.TryNormalize(
                out var reason, Resolve, id => id != missing));
            Assert.Contains(missing, reason, StringComparison.Ordinal);
        }
    }

    private static Type? Resolve(string name) => name switch
    {
        "SettingsManager" => typeof(SettingsManager),
        "BoolVariable" => typeof(BoolVariable),
        "StringVariable" => typeof(StringVariable),
        _ => null,
    };
}
