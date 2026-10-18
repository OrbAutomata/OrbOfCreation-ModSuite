using System;
using System.Collections.Generic;
using OrbModding.Common;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.World;

namespace OrbMentor;

internal static class MentorCycleEvaluator
{
    internal static WakePolicy Evaluate(
        GameWorldState world,
        in SuiteRuntimeConfiguration config,
        ref MentorCycleState state,
        ServiceActionWriter<MentorCycleAction> actions,
        out MentorDecisionMetrics metrics)
    {
        var inputs = world.MasteryExperience.AsSpan();
        if (!MentorConfigurationPolicy.IsOperational(config))
        {
            if (inputs.Length > 0) state.DiscardThrough(inputs[^1].Sequence);
            metrics = default;
            return WakePolicy.AfterDecision(MentorConfigurationPolicy.IdleInterval);
        }

        var inputIndex = FirstAfter(inputs, state.LastInputSequence);
        if (inputIndex < 0)
        {
            metrics = default;
            return WakePolicy.AfterDecision(MentorConfigurationPolicy.IdleInterval);
        }

        ref readonly var input = ref inputs[inputIndex];
        var missed = Math.Max(0, input.Sequence - state.LastInputSequence - 1);
        var candidates = 0;
        var recipients = new List<Guid>();
        if (MentorConfigurationPolicy.DomainEnabled(config, input.Domain) &&
            input.SourceEligible &&
            IsUnlocked(world, input.Domain))
        {
            SelectRecipients(world, in config, in input, recipients, out candidates);
        }

        var fraction = MentorConfigurationPolicy.ShareFraction(config, input.Domain);
        var amount = new MentorAmount(input.Amount.Mantissa, input.Amount.Exponent).Multiply(
            config.Mentor.EconomyMode == MentorEconomyMode.SharedPool && recipients.Count > 0
                ? fraction / recipients.Count
                : fraction);
        if (amount.IsValidPositive)
        {
            foreach (var recipient in recipients)
            {
                actions.Add(new MentorCycleAction(
                    input.Domain,
                    recipient,
                    amount,
                    input.SourceMastery,
                    world.CollectedAtEpoch));
            }
        }

        metrics = new MentorDecisionMetrics(
            input.Domain,
            input.Sequence,
            candidates,
            recipients.Count,
            actions.Count,
            missed);
        state.Observe(input.Sequence, missed, in metrics);
        return inputIndex + 1 < inputs.Length
            ? WakePolicy.OnPublication
            : WakePolicy.AfterDecision(MentorConfigurationPolicy.IdleInterval);
    }

    private static int FirstAfter(
        ReadOnlySpan<WorldMasteryExperience> inputs,
        long sequence)
    {
        for (var index = 0; index < inputs.Length; index++)
            if (inputs[index].Sequence > sequence) return index;
        return -1;
    }

    private static bool IsUnlocked(
        GameWorldState world,
        MasteryExperienceDomain domain) =>
        IsViewAvailable(world, KnownEntities.MasteriesEnabled.Uuid) &&
        IsViewAvailable(world, domain switch
        {
            MasteryExperienceDomain.Spell => KnownEntities.MagicSpellbook.Uuid,
            MasteryExperienceDomain.Artifact => KnownEntities.WorkshopArtifact.Uuid,
            _ => KnownEntities.AlchemyScreen.Uuid,
        });

    private static bool IsViewAvailable(GameWorldState world, Guid id) =>
        WorldLookup.TryFind(world.Views, id, out var view) && view.Available;

    private static void SelectRecipients(
        GameWorldState world,
        in SuiteRuntimeConfiguration config,
        in WorldMasteryExperience input,
        List<Guid> recipients,
        out int candidates)
    {
        switch (input.Domain)
        {
            case MasteryExperienceDomain.Spell:
                SelectSpellRecipients(world, in config, in input, recipients, out candidates);
                return;
            case MasteryExperienceDomain.Artifact:
                SelectArtifactRecipients(world, in input, recipients, out candidates);
                return;
            default:
                SelectAlchemyRecipients(world, in input, recipients, out candidates);
                return;
        }
    }

    private static void SelectSpellRecipients(
        GameWorldState world,
        in SuiteRuntimeConfiguration config,
        in WorldMasteryExperience input,
        List<Guid> recipients,
        out int candidates)
    {
        candidates = 0;
        if (!WorldLookup.TryFind(world.SpellRecipes, input.SourceId, out var source) ||
            !source.Discovered ||
            source.MasteryLevel != input.SourceMastery)
            return;

        if (config.Mentor.SpellSourcePolicy == MentorSpellSourcePolicy.EquippedSpells)
        {
            var equipped = false;
            var slots = world.SpellSlots.AsSpan();
            for (var index = 0; index < slots.Length; index++)
                equipped |= slots[index].Occupied && slots[index].SpellRecipeId == input.SourceId;
            if (!equipped) return;
        }
        else
        {
            var highest = int.MinValue;
            var rows = world.SpellRecipes.AsSpan();
            for (var index = 0; index < rows.Length; index++)
                if (rows[index].Discovered) highest = Math.Max(highest, rows[index].MasteryLevel);
            if (input.SourceMastery < highest) return;
        }

        var recipes = world.SpellRecipes.AsSpan();
        for (var index = 0; index < recipes.Length; index++)
        {
            ref readonly var recipe = ref recipes[index];
            if (!recipe.Discovered) continue;
            candidates++;
            if (recipe.SpellRecipeId != input.SourceId &&
                recipe.MasteryLevel < input.SourceMastery)
                recipients.Add(recipe.SpellRecipeId);
        }
    }

    private static void SelectArtifactRecipients(
        GameWorldState world,
        in WorldMasteryExperience input,
        List<Guid> recipients,
        out int candidates)
    {
        candidates = 0;
        if (!WorldLookup.TryFind(world.Equipment, input.SourceId, out var source) ||
            !source.IsCreated ||
            source.MasteryLevel != input.SourceMastery)
            return;
        var highest = int.MinValue;
        var rows = world.Equipment.AsSpan();
        for (var index = 0; index < rows.Length; index++)
            if (rows[index].IsCreated) highest = Math.Max(highest, rows[index].MasteryLevel);
        if (input.SourceMastery < highest) return;
        for (var index = 0; index < rows.Length; index++)
        {
            ref readonly var equipment = ref rows[index];
            if (!equipment.IsCreated) continue;
            candidates++;
            if (equipment.EquipmentId != input.SourceId &&
                equipment.MasteryLevel < input.SourceMastery)
                recipients.Add(equipment.EquipmentId);
        }
    }

    private static void SelectAlchemyRecipients(
        GameWorldState world,
        in WorldMasteryExperience input,
        List<Guid> recipients,
        out int candidates)
    {
        candidates = 0;
        if (!WorldLookup.TryFind(world.AlchemyRecipes, input.SourceId, out var source) ||
            !source.Discovered ||
            source.MasteryLevel != input.SourceMastery ||
            !IsOrdinaryAlchemy(world, in source))
            return;
        var highest = int.MinValue;
        var rows = world.AlchemyRecipes.AsSpan();
        for (var index = 0; index < rows.Length; index++)
            if (rows[index].Discovered && IsOrdinaryAlchemy(world, in rows[index]))
                highest = Math.Max(highest, rows[index].MasteryLevel);
        if (input.SourceMastery < highest) return;
        for (var index = 0; index < rows.Length; index++)
        {
            ref readonly var recipe = ref rows[index];
            if (!recipe.Discovered || !IsOrdinaryAlchemy(world, in recipe)) continue;
            candidates++;
            if (recipe.RecipeId != input.SourceId &&
                recipe.MasteryLevel < input.SourceMastery)
                recipients.Add(recipe.RecipeId);
        }
    }

    private static bool IsOrdinaryAlchemy(
        GameWorldState world,
        in WorldAlchemyRecipe recipe)
    {
        var concepts = world.ConceptRecipes.AsSpan();
        for (var index = 0; index < concepts.Length; index++)
            if (concepts[index].RecipeId == recipe.RecipeId) return false;
        return AlchemyGameplayDomainClassifier.ClassifyTypeUuid(recipe.CoreTypeId) ==
               AlchemyGameplayDomain.OrdinaryAlchemy;
    }
}
