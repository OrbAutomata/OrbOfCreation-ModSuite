using System;
using OrbModding.Common;

namespace OrbAutomata;

internal sealed class ResearchNativeBindings
{
    internal static readonly string[] ContractIds =
    {
        "research-action.research.type-action", "research-action.cost.type-action",
        "research-action.settings.type-action", "research-action.level-action",
        "research-action.self-bonus-action", "research-action.active-action",
        "research-action.developing-action", "research-action.max-level-action",
        "research-action.can-develop-action", "research-action.can-bonus-action",
        "research-action.queued-levels-action", "research-action.free-bonus-action",
        "research-action.development-cost-action", "research-action.cost-enough-action",
        "research-action.queue-mode-action", "research-action.purchase-action",
        "research-action.pause-action", "research-action.resume-action",
        "research-action.cancel-action", "research-action.submit-bonus-action",
        "research-action.has-max-level-action", "research-action.development-cost-at-level-action",
        "research-action.within-range-at-action", "research-action.cost-add-action",
        "research-action.is-complete-action", "research-action.meets-requirements-action",
        "research-action.still-has-leeway-action", "research-action.below-artificial-max-action",
        "research-action.below-investment-max-action", "research-action.is-visible-action",
    };

    private ResearchNativeBindings(Type researchType,
        Func<object, int> level, Func<object, int> selfBonus,
        Func<object, bool> active, Func<object, bool> developing,
        Func<object, int> maximum, Func<object, bool> canDevelop,
        Func<object, bool> canBonus, Func<object, int> queued, Func<object, int> freeBonus,
        Func<object, object?> developmentCost, Func<object, bool> enough,
        Func<bool> queueMode,
        Action<object> purchase, Action<object> pause, Action<object> resume,
        Action<object> cancel, Action<object> submitBonus, Func<object, bool> hasMaxLevel,
        Func<object, int, object?> developmentCostAtLevel,
        Func<object, int, bool> withinRangeAt, Func<object, object, object?> addCost,
        Func<object, bool> complete, Func<object, bool> meetsLevelRequirements,
        Func<object, bool> stillHasLeeway, Func<object, bool> belowArtificialMaxLevel,
        Func<object, bool> belowMaxInvestmentLevel, Func<object, bool> isVisible)
    {
        IsVisible = isVisible;
        Complete = complete; MeetsLevelRequirements = meetsLevelRequirements;
        StillHasLeeway = stillHasLeeway; BelowArtificialMaxLevel = belowArtificialMaxLevel;
        BelowMaxInvestmentLevel = belowMaxInvestmentLevel;
        ResearchType = researchType; Level = level;
        SelfBonusLevels = selfBonus; IsActive = active; IsDeveloping = developing;
        MaxLevel = maximum; CanDevelop = canDevelop;
        CanApplyBonusLevel = canBonus; QueuedLevels = queued;
        FreeBonusLevels = freeBonus; DevelopmentCost = developmentCost;
        HasEnough = enough; QueueMode = queueMode;
        Purchase = purchase; Pause = pause; Resume = resume; Cancel = cancel;
        SubmitBonus = submitBonus; HasMaxLevel = hasMaxLevel;
        DevelopmentCostAtLevel = developmentCostAtLevel;
        WithinDevelopRangeAt = withinRangeAt; AddCost = addCost;
    }

    internal Type ResearchType { get; }
    internal Func<object, int> Level { get; }
    internal Func<object, int> SelfBonusLevels { get; }
    internal Func<object, bool> IsActive { get; }
    internal Func<object, bool> IsDeveloping { get; }
    internal Func<object, int> MaxLevel { get; }
    internal Func<object, bool> CanDevelop { get; }
    internal Func<object, bool> CanApplyBonusLevel { get; }
    internal Func<object, int> QueuedLevels { get; }
    internal Func<object, int> FreeBonusLevels { get; }
    internal Func<object, object?> DevelopmentCost { get; }
    internal Func<object, bool> HasEnough { get; }
    internal Func<bool> QueueMode { get; }
    internal Action<object> Purchase { get; }
    internal Action<object> Pause { get; }
    internal Action<object> Resume { get; }
    internal Action<object> Cancel { get; }
    internal Action<object> SubmitBonus { get; }
    internal Func<object, bool> HasMaxLevel { get; }
    internal Func<object, int, object?> DevelopmentCostAtLevel { get; }
    internal Func<object, int, bool> WithinDevelopRangeAt { get; }
    internal Func<object, object, object?> AddCost { get; }

    // The four gates ResearchSO.IsWithinDevelopRange evaluates, bound individually so a refusal
    // can name the one that closed rather than only that the aggregate said no.
    internal Func<object, bool> Complete { get; }
    internal Func<object, bool> MeetsLevelRequirements { get; }
    internal Func<object, bool> StillHasLeeway { get; }
    internal Func<object, bool> BelowArtificialMaxLevel { get; }
    internal Func<object, bool> BelowMaxInvestmentLevel { get; }

    /// <summary>
    /// <c>ResearchSO.IsVisible()</c>, which is what <c>UIResearchItem.IsVisible()</c> draws the
    /// tile on: a research it answers false for has no button on the Research grid to press.
    /// </summary>
    internal Func<object, bool> IsVisible { get; }

    internal static bool TryCreate(out ResearchNativeBindings? bindings, out string reason,
        Func<string, Type?>? resolveType = null, Func<string, bool>? includeContract = null) =>
        NativeBindingSet.TryBind("research action", ContractIds, resolveType, includeContract, set =>
        {
            var research = set.Type("research-action.research.type-action", "ResearchSO");
            var cost = set.Type("research-action.cost.type-action", "ResourceCostList");
            var settings = set.Type("research-action.settings.type-action", "SettingsManager");
            return new ResearchNativeBindings(research,
                set.Field<Func<object, int>>("research-action.level-action", research, "level", typeof(int)),
                set.Field<Func<object, int>>(
                    "research-action.self-bonus-action", research, "selfBonusLevels", typeof(int)),
                set.Field<Func<object, bool>>("research-action.active-action", research, "isActive", typeof(bool)),
                set.Field<Func<object, bool>>(
                    "research-action.developing-action", research, "isDeveloping", typeof(bool)),
                set.Field<Func<object, int>>("research-action.max-level-action", research, "maxLevel", typeof(int)),
                set.Call<Func<object, bool>>(
                    "research-action.can-develop-action", research, "CanDevelop", typeof(bool)),
                set.Call<Func<object, bool>>(
                    "research-action.can-bonus-action", research, "CanApplyBonusLevels", typeof(bool)),
                set.Call<Func<object, int>>(
                    "research-action.queued-levels-action", research, "GetQueuedLevels", typeof(int)),
                set.Call<Func<object, int>>(
                    "research-action.free-bonus-action", research, "GetFreeBonusLevelsLeft", typeof(int)),
                set.Call<Func<object, object?>>(
                    "research-action.development-cost-action", research, "GetDevelopmentCost", cost),
                set.Call<Func<object, bool>>("research-action.cost-enough-action", cost, "HasEnough", typeof(bool)),
                set.StaticCall<Func<bool>>(
                    "research-action.queue-mode-action", settings, "IsResearchQueueMode", typeof(bool)),
                set.Call<Action<object>>("research-action.purchase-action", research, "PurchaseLevel", typeof(void)),
                set.Call<Action<object>>("research-action.pause-action", research, "PauseResearch", typeof(void)),
                set.Call<Action<object>>("research-action.resume-action", research, "ResumeResearch", typeof(void)),
                set.Call<Action<object>>("research-action.cancel-action", research, "CancelDevelopment", typeof(void)),
                set.Call<Action<object>>(
                    "research-action.submit-bonus-action", research, "SubmitBonusLevel", typeof(void)),
                set.Call<Func<object, bool>>(
                    "research-action.has-max-level-action", research, "HasMaxLevel", typeof(bool)),
                set.Call<Func<object, int, object?>>("research-action.development-cost-at-level-action",
                    research, "GetDevelopmentCostAtLevel", cost, typeof(int)),
                set.Call<Func<object, int, bool>>("research-action.within-range-at-action",
                    research, "IsWithinDevelopRangeAt", typeof(bool), typeof(int)),
                set.Call<Func<object, object, object?>>("research-action.cost-add-action", cost, "Add", cost, cost),
                set.Call<Func<object, bool>>(
                    "research-action.is-complete-action", research, "IsComplete", typeof(bool)),
                set.Call<Func<object, bool>>(
                    "research-action.meets-requirements-action", research, "MeetsLevelRequirements", typeof(bool)),
                set.Call<Func<object, bool>>(
                    "research-action.still-has-leeway-action", research, "StillHasLeeway", typeof(bool)),
                set.Call<Func<object, bool>>(
                    "research-action.below-artificial-max-action", research, "IsBelowArtificialMaxLevel", typeof(bool)),
                set.Call<Func<object, bool>>(
                    "research-action.below-investment-max-action", research, "IsBelowMaxInvestmentLevel", typeof(bool)),
                set.Call<Func<object, bool>>("research-action.is-visible-action", research, "IsVisible", typeof(bool)));
        }, out bindings, out reason);
}
