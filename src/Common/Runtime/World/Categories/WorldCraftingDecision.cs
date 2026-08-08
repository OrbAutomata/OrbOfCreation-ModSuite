using System;
using System.Collections;
using System.Collections.Generic;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using UnityEngine;

namespace OrbModding.Common.Runtime.World;

internal enum WorldCraftingPipeline
{
    Unknown = 0,
    Direct = 1,
    QueueStack = 2,
    QueueNew = 3,
}

internal readonly struct WorldCraftingDecision
{
    internal WorldCraftingDecision(
        Guid recipeId,
        WorldCraftingPipeline pipeline,
        BigDouble purchaseAmount,
        BigDouble queuedAmount,
        Guid queueId,
        int queueUsed,
        int queueMaximum,
        bool canStart,
        string reasonCode,
        int automationRepetitions = 0,
        int automationUsed = 0,
        int automationMaximum = 0,
        bool canAutomate = false,
        string automationReasonCode = "",
        Guid automationQueueId = default)
    {
        RecipeId = recipeId;
        Pipeline = pipeline;
        PurchaseAmount = purchaseAmount;
        QueuedAmount = queuedAmount;
        QueueId = queueId;
        QueueUsed = queueUsed;
        QueueMaximum = queueMaximum;
        CanStart = canStart;
        ReasonCode = reasonCode ?? string.Empty;
        AutomationRepetitions = automationRepetitions;
        AutomationUsed = automationUsed;
        AutomationMaximum = automationMaximum;
        CanAutomate = canAutomate;
        AutomationReasonCode = automationReasonCode ?? string.Empty;
        AutomationQueueId = automationQueueId;
    }

    internal Guid RecipeId { get; }
    internal WorldCraftingPipeline Pipeline { get; }
    internal BigDouble PurchaseAmount { get; }
    internal BigDouble QueuedAmount { get; }
    /// <summary>The manual queue this recipe routes to.</summary>
    internal Guid QueueId { get; }

    /// <summary>
    /// The automation queue this recipe routes to. Both queues the page owns are published, so a
    /// caller can join either one to its live contents instead of only the manual half.
    /// </summary>
    internal Guid AutomationQueueId { get; }
    internal int QueueUsed { get; }
    internal int QueueMaximum { get; }
    internal bool CanStart { get; }
    internal string ReasonCode { get; }
    /// <summary>
    /// How many times the automation entry repeats, which is what the game's own
    /// <c>CraftingInstance.GetAutomationQuantity()</c> returns:
    /// <c>SetAutomationQuantity(n)</c> stores <c>quantity = 2^(n-1)</c>, so this is the exponent,
    /// not the badge. The badge number lives on the queue entry's <see cref="WorldCraftingQueueEntry.Amount"/>.
    /// </summary>
    internal int AutomationRepetitions { get; }
    internal int AutomationUsed { get; }
    internal int AutomationMaximum { get; }
    internal bool CanAutomate { get; }

    /// <summary>
    /// Why the automation queue will not take this recipe, answered where the native predicates ran.
    /// An undiscovered recipe is refused for being undiscovered, never for a queue that has room.
    /// </summary>
    internal string AutomationReasonCode { get; }
    internal bool CanCancelManual => QueuedAmount > BigDouble.Zero;
    internal bool CanCancelAutomation => AutomationRepetitions > 0;
}

internal readonly struct WorldCraftingDecisionCost
{
    internal WorldCraftingDecisionCost(
        Guid recipeId,
        Guid resourceId,
        BigDouble cost,
        BigDouble amount)
    {
        RecipeId = recipeId;
        ResourceId = resourceId;
        Cost = cost;
        Amount = amount;
    }

    internal Guid RecipeId { get; }
    internal Guid ResourceId { get; }
    internal BigDouble Cost { get; }
    internal BigDouble Amount { get; }
    internal bool Affordable => Amount.CompareTo(Cost) >= 0;
}

internal readonly struct WorldCraftingQueueEntry
{
    internal WorldCraftingQueueEntry(
        Guid queueId,
        int slot,
        Guid recipeId,
        BigDouble amount,
        bool automatic,
        int repetitions)
    {
        QueueId = queueId;
        Slot = slot;
        RecipeId = recipeId;
        Amount = amount;
        Automatic = automatic;
        Repetitions = repetitions;
    }

    internal Guid QueueId { get; }
    internal int Slot { get; }
    internal Guid RecipeId { get; }
    internal BigDouble Amount { get; }
    internal bool Automatic { get; }
    internal int Repetitions { get; }
}

internal static class WorldCraftingDecisionLookup
{
    internal static bool TryFind(
        PublicationTable<WorldCraftingDecision> decisions,
        Guid recipeId,
        out WorldCraftingDecision decision)
    {
        for (var index = 0; index < decisions.Count; index++)
        {
            if (decisions[index].RecipeId != recipeId) continue;
            decision = decisions[index];
            return true;
        }
        decision = default;
        return false;
    }

    internal static bool TryFindCostRange(
        PublicationTable<WorldCraftingDecisionCost> costs,
        Guid recipeId,
        out int start,
        out int count)
    {
        start = -1;
        count = 0;
        for (var index = 0; index < costs.Count; index++)
        {
            if (costs[index].RecipeId != recipeId)
            {
                if (start >= 0) break;
                continue;
            }
            if (start < 0) start = index;
            count++;
        }
        return start >= 0;
    }

    /// <summary>
    /// The contiguous slot-ordered run one queue owns. Entries are derived sorted by queue then
    /// slot, and each queue is appended exactly once, so one run holds all of a queue's contents.
    /// </summary>
    internal static bool TryFindQueueRange(
        PublicationTable<WorldCraftingQueueEntry> entries,
        Guid queueId,
        out int start,
        out int count)
    {
        start = -1;
        count = 0;
        for (var index = 0; index < entries.Count; index++)
        {
            if (entries[index].QueueId != queueId)
            {
                if (start >= 0) break;
                continue;
            }
            if (start < 0) start = index;
            count++;
        }
        return start >= 0;
    }
}

/// <summary>
/// Captures the authored page-to-queue routing once per lifecycle, then evaluates only the live
/// purchase amount, queue, cost, and affordability facts each world generation.
/// </summary>
internal sealed class WorldCraftingDecisionReader : IWorldCategoryReader
{
    private readonly Type? _pageType;
    private readonly Func<IList?>? _allRecipes;
    private readonly Func<object, Guid>? _identity;
    private readonly Func<object, bool>? _visible;
    private readonly Func<object, bool>? _canBuy;
    private readonly Func<object, BigDouble, bool>? _canBuyAt;
    private readonly Func<object, BigDouble, BigDouble>? _purchaseAmount;
    private readonly Func<object, object?>? _recipeCost;
    private readonly Func<object, BigDouble, BigDouble, object?>? _totalCost;
    private readonly Func<object, object?>? _mainType;
    private readonly Func<object, double>? _timeToComplete;
    private readonly Func<object, object?>? _pageRecipes;
    private readonly Func<object, object?>? _pageQueue;
    private readonly Func<object, object?>? _pageAutomation;
    private readonly Func<object, object?>? _pageMode;
    private readonly Func<object, object?>? _pageMainType;
    private readonly Func<object, IList?>? _recipeListValues;
    private readonly Func<object, IList?>? _queueValues;
    private readonly Func<object, object, BigDouble>? _queueQuantity;
    private readonly Func<object, Guid>? _queueIdentity;
    private readonly Func<object, bool>? _queueHasRoom;
    private readonly Func<object, int>? _queueMaximum;
    private readonly Type? _instanceType;
    private readonly Func<object, Guid>? _instanceRecipe;
    private readonly Func<object, BigDouble>? _instanceQuantity;
    private readonly Func<object, int>? _instanceAutomationQuantity;
    private readonly Func<object, bool>? _instanceIsAuto;
    private readonly Func<object, int>? _modeValue;
    private readonly Func<object, bool>? _costHasEnough;
    private readonly Func<object, BigDouble, object?>? _costMultiply;
    private readonly Func<object, IList?>? _costEntries;
    private readonly Func<object, object?>? _costResource;
    private readonly Func<object, BigDouble>? _costValue;
    private readonly Func<object, Guid>? _resourceIdentity;
    private readonly Func<object, BigDouble>? _resourceAmount;
    private readonly string _unavailable;
    private object[] _pages = Array.Empty<object>();
    private long _publishedEpoch = long.MinValue;

    internal WorldCraftingDecisionReader(Func<string, Type?> resolve)
    {
        _pageType = resolve("UICraftingPage");
        var recipeType = resolve("CraftingRecipeSO");
        var recipeListType = resolve("CraftingRecipeListVariable");
        var queueType = resolve("CraftingInstanceListVariable");
        _instanceType = resolve("CraftingInstance");
        var recipeMainType = resolve("CraftingRecipeTypeSO");
        var intVariableType = resolve("IntVariable");
        var resourceType = resolve("ResourceSO");
        if (_pageType is null || recipeType is null || recipeListType is null ||
            queueType is null || _instanceType is null || recipeMainType is null || intVariableType is null ||
            resourceType is null)
        {
            _unavailable = "one or more crafting decision types were unavailable";
            return;
        }

        var recipe = new WorldMemberBinding(recipeType, "CraftingRecipeSO");
        _allRecipes = NativeAccessorBinder.StaticListAccessor(recipeType, "All");
        _identity = recipe.Call<Guid>("GetGuid");
        _visible = recipe.Call<bool>("IsVisible");
        _canBuy = recipe.Call<bool>("CanBuy");
        _canBuyAt = recipe.Call<BigDouble, bool>("CanBuyAt");
        _purchaseAmount = recipe.Call<BigDouble, BigDouble>("GetPurchaseQuantity");
        var totalCostMethod = recipeType.GetMethod("GetTotalCost");
        var costType = totalCostMethod?.ReturnType;
        _recipeCost = recipe.Reference("recipeCost", costType);
        _totalCost = recipe.CallObject<BigDouble, BigDouble>("GetTotalCost", costType);
        _mainType = recipe.CallObject("GetMainType", recipeMainType);
        _timeToComplete = recipe.Field<double>("timeToComplete");

        var page = new WorldMemberBinding(_pageType, "UICraftingPage");
        _pageRecipes = page.Reference("availableRecipes", recipeListType);
        _pageQueue = page.Reference("craftingQueueInstances", queueType);
        _pageAutomation = page.Reference("craftingAutomationInstances", queueType);
        _pageMode = page.Reference("craftMode", intVariableType);
        _pageMainType = page.Reference("mainCraftType", recipeMainType);
        var recipeList = new WorldMemberBinding(recipeListType, "CraftingRecipeListVariable");
        _recipeListValues = recipeList.CollectionField("value");
        var queue = new WorldMemberBinding(queueType, "CraftingInstanceListVariable");
        _queueValues = queue.CollectionField("value");
        _queueIdentity = queue.Call<Guid>("GetGuid");
        _queueQuantity = queue.CallWithObjectArgument<BigDouble>("GetQuantity", recipeType);
        _queueHasRoom = queue.Call<bool>("HasEmptySpot");
        _queueMaximum = queue.Call<int>("GetMax");
        var instance = new WorldMemberBinding(_instanceType, "CraftingInstance");
        _instanceRecipe = instance.Call<Guid>("GetGuidReference");
        _instanceQuantity = instance.Call<BigDouble>("GetQuantity");
        _instanceAutomationQuantity = instance.Call<int>("GetAutomationQuantity");
        _instanceIsAuto = instance.Call<bool>("IsAuto");
        var mode = new WorldMemberBinding(intVariableType, "IntVariable");
        _modeValue = mode.Call<int>("AsInt");

        var cost = new WorldMemberBinding(costType!, "ResourceCostList");
        _costHasEnough = cost.Call<bool>("HasEnough");
        _costMultiply = cost.CallObject<BigDouble>("Multiply", costType);
        var entriesMethod = costType?.GetMethod("GetEntries");
        var entriesType = entriesMethod?.ReturnType;
        var tupleType = entriesType is { IsGenericType: true }
            ? entriesType.GetGenericArguments()[0]
            : null;
        _costEntries = cost.CallList("GetEntries", tupleType);
        var tuple = new WorldMemberBinding(tupleType!, "ResourceTuple");
        _costResource = tuple.Reference("resource", resourceType);
        _costValue = tuple.Call<BigDouble>("GetValue");
        var resource = new WorldMemberBinding(resourceType, "ResourceSO");
        _resourceIdentity = resource.Call<Guid>("GetGuid");
        _resourceAmount = resource.Call<BigDouble>("GetQuantity");

        _unavailable = JoinFailures(
            _allRecipes is null ? "CraftingRecipeSO.All was unavailable" : string.Empty,
            recipe.Failure,
            page.Failure,
            recipeList.Failure,
            queue.Failure,
            instance.Failure,
            mode.Failure,
            cost.Failure,
            tuple.Failure,
            resource.Failure);
    }

    public string Category => "crafting decisions";
    public bool IsAvailable => _unavailable.Length == 0;

    public WorldCategoryReport Collect(HashSet<Guid> claimed, GameWorldCycleFrame frame)
    {
        frame.CraftingDecisions.Reset();
        frame.CraftingDecisionCosts.Reset();
        frame.CraftingQueueEntries.Reset();
        if (!IsAvailable) return WorldCategoryReport.Missing(Category, _unavailable);
        if (!TryPinPages(frame.CollectedAtEpoch, out var stabilityReason))
            return WorldCategoryReport.Missing(Category, stabilityReason);

        try
        {
            AppendQueueEntries(frame);
        }
        catch (Exception exception)
        {
            frame.CraftingQueueEntries.Reset();
            return WorldCategoryReport.Missing(
                Category,
                "reading the visible crafting queues failed: " +
                exception.GetBaseException().Message);
        }

        var recipes = _allRecipes!() ??
            throw new InvalidOperationException("CraftingRecipeSO.All was null.");
        var sampled = 0;
        var skipped = 0;
        var firstFailure = string.Empty;
        for (var index = 0; index < recipes.Count; index++)
        {
            var recipe = recipes[index];
            try
            {
                if (recipe is null) throw new InvalidOperationException("recipe was null");
                AppendDecision(recipe, frame);
                sampled++;
            }
            catch (Exception exception)
            {
                skipped++;
                if (firstFailure.Length == 0)
                    firstFailure = "crafting decision row " + index + " failed: " +
                        exception.GetBaseException().Message;
            }
        }
        return new WorldCategoryReport(
            Category,
            WorldCategoryOutcome.Collected,
            sampled,
            skipped,
            firstFailure);
    }

    private void AppendDecision(object recipe, GameWorldCycleFrame frame)
    {
        var recipeId = _identity!(recipe);
        if (recipeId == Guid.Empty) throw new InvalidOperationException("recipe UUID was empty");
        object? matchedPage = null;
        for (var index = 0; index < _pages.Length; index++)
        {
            var values = _recipeListValues!(_pageRecipes!(_pages[index])!) ??
                throw new InvalidOperationException("page recipe list was null");
            var contains = false;
            for (var valueIndex = 0; valueIndex < values.Count; valueIndex++)
                if (ReferenceEquals(values[valueIndex], recipe))
                {
                    contains = true;
                    break;
                }
            if (!contains) continue;
            if (matchedPage is not null)
                throw new InvalidOperationException("recipe appears on more than one crafting page");
            matchedPage = _pages[index];
        }

        var visible = _visible!(recipe);
        if (matchedPage is null)
        {
            var amount = _purchaseAmount!(recipe, BigDouble.One);
            var timeToComplete = _timeToComplete!(recipe);
            var canStart = visible && amount > BigDouble.Zero &&
                timeToComplete <= 0d && _canBuy!(recipe);
            var reason = !visible
                ? "hidden_or_undiscovered"
                : amount <= BigDouble.Zero
                    ? "invalid_purchase_amount"
                    : timeToComplete > 0d
                        ? "crafting_page_not_loaded"
                        : canStart ? "ready" : "native_purchase_refused";
            if (amount > BigDouble.Zero)
            {
                var baseCost = _recipeCost!(recipe) ??
                    throw new InvalidOperationException("recipeCost was null");
                var totalCost = _costMultiply!(baseCost, amount);
                if (totalCost is not null) AppendCosts(recipeId, totalCost, frame);
            }
            frame.CraftingDecisions.Append(new WorldCraftingDecision(
                recipeId,
                timeToComplete > 0d
                    ? WorldCraftingPipeline.Unknown
                    : WorldCraftingPipeline.Direct,
                amount,
                BigDouble.Zero,
                Guid.Empty,
                0,
                0,
                canStart,
                reason));
            return;
        }

        var queue = _pageQueue!(matchedPage) ??
            throw new InvalidOperationException("page queue was null");
        var pageMainType = _pageMainType!(matchedPage) ??
            throw new InvalidOperationException("page main type was null");
        if (!ReferenceEquals(pageMainType, _mainType!(recipe)))
            throw new InvalidOperationException("page and recipe main types differ");
        var mode = _modeValue!(_pageMode!(matchedPage) ??
            throw new InvalidOperationException("page craft mode was null"));
        if (mode is not 0 and not 1)
            throw new InvalidOperationException("page craft mode was outside 0..1");
        var previous = _queueQuantity!(queue, recipe);
        var amountToBuy = _purchaseAmount!(recipe, previous);
        var valuesInQueue = _queueValues!(queue) ??
            throw new InvalidOperationException("page queue value was null");
        var automation = _pageAutomation!(matchedPage) ??
            throw new InvalidOperationException("page automation queue was null");
        var valuesInAutomation = _queueValues!(automation) ??
            throw new InvalidOperationException("page automation value was null");
        var automationRepetitions = AutomationRepetitions(valuesInAutomation, recipeId);
        var automationHasExisting = automationRepetitions > 0;
        var automationHasRoom = automationHasExisting || _queueHasRoom!(automation);
        var canAutomate = visible && automationHasRoom;
        var automationReasonCode = !visible
            ? "hidden_or_undiscovered"
            : automationHasRoom
                ? "ready"
                : "automation_full";
        var hasExisting = HasRecipe(valuesInQueue, recipeId);
        var hasSpace = hasExisting && mode == 0 || _queueHasRoom!(queue);
        var targetAmount = previous +
            (amountToBuy < BigDouble.One ? BigDouble.One : amountToBuy);
        var nativeAllows = amountToBuy > BigDouble.Zero && _canBuyAt!(recipe, targetAmount);
        var costAllows = false;
        if (amountToBuy > BigDouble.Zero)
        {
            var total = _totalCost!(recipe, previous, amountToBuy) ??
                throw new InvalidOperationException("GetTotalCost returned null");
            costAllows = _costHasEnough!(total);
            AppendCosts(recipeId, total, frame);
        }
        var canQueue = visible && nativeAllows && costAllows && hasSpace;
        var reasonCode = !visible
            ? "hidden_or_undiscovered"
            : amountToBuy <= BigDouble.Zero
                ? "invalid_purchase_amount"
                : !hasSpace
                    ? "queue_full"
                    : !nativeAllows || !costAllows
                        ? "native_purchase_refused"
                        : "ready";
        frame.CraftingDecisions.Append(new WorldCraftingDecision(
            recipeId,
            hasExisting && mode == 0
                ? WorldCraftingPipeline.QueueStack
                : WorldCraftingPipeline.QueueNew,
            amountToBuy,
            previous,
            _queueIdentity!(queue),
            CountNonNull(valuesInQueue),
            _queueMaximum!(queue),
            canQueue,
            reasonCode,
            automationRepetitions,
            CountNonNull(valuesInAutomation),
            _queueMaximum!(automation),
            canAutomate,
            automationReasonCode,
            _queueIdentity!(automation)));
    }

    private void AppendQueueEntries(GameWorldCycleFrame frame)
    {
        var queues = new Dictionary<Guid, object>();
        for (var pageIndex = 0; pageIndex < _pages.Length; pageIndex++)
        {
            AppendQueue(
                _pageQueue!(_pages[pageIndex]) ??
                    throw new InvalidOperationException("page manual queue was null"),
                expectedAutomatic: false,
                queues,
                frame);
            AppendQueue(
                _pageAutomation!(_pages[pageIndex]) ??
                    throw new InvalidOperationException("page automation queue was null"),
                expectedAutomatic: true,
                queues,
                frame);
        }
    }

    private void AppendQueue(
        object queue,
        bool expectedAutomatic,
        Dictionary<Guid, object> queues,
        GameWorldCycleFrame frame)
    {
        var queueId = _queueIdentity!(queue);
        if (queueId == Guid.Empty)
            throw new InvalidOperationException("a crafting queue UUID was empty");
        if (queues.TryGetValue(queueId, out var existing))
        {
            if (!ReferenceEquals(existing, queue))
                throw new InvalidOperationException(
                    "two crafting queues shared identity " + queueId.ToString("D"));
            return;
        }
        queues.Add(queueId, queue);
        var values = _queueValues!(queue) ??
            throw new InvalidOperationException("crafting queue value was null");
        for (var slot = 0; slot < values.Count; slot++)
        {
            var value = values[slot];
            if (value is null) continue;
            if (value.GetType() != _instanceType)
                throw new InvalidOperationException(
                    "crafting queue slot " + slot + " had the wrong native type");
            var automatic = _instanceIsAuto!(value);
            if (automatic != expectedAutomatic)
                throw new InvalidOperationException(
                    "crafting queue slot " + slot +
                    " contradicted its manual or automatic list");
            var recipeId = _instanceRecipe!(value);
            if (recipeId == Guid.Empty)
                throw new InvalidOperationException(
                    "crafting queue slot " + slot + " had no recipe identity");
            frame.CraftingQueueEntries.Append(new WorldCraftingQueueEntry(
                queueId,
                slot,
                recipeId,
                _instanceQuantity!(value),
                automatic,
                automatic ? _instanceAutomationQuantity!(value) : 0));
        }
    }

    private void AppendCosts(Guid recipeId, object cost, GameWorldCycleFrame frame)
    {
        var entries = _costEntries!(cost) ??
            throw new InvalidOperationException("ResourceCostList.GetEntries returned null");
        for (var index = 0; index < entries.Count; index++)
        {
            var tuple = entries[index] ??
                throw new InvalidOperationException("cost entry was null");
            var resource = _costResource!(tuple) ??
                throw new InvalidOperationException("cost resource was null");
            var resourceId = _resourceIdentity!(resource);
            if (resourceId == Guid.Empty)
                throw new InvalidOperationException("cost resource UUID was empty");
            frame.CraftingDecisionCosts.Append(new WorldCraftingDecisionCost(
                recipeId,
                resourceId,
                _costValue!(tuple),
                _resourceAmount!(resource)));
        }
    }

    private bool TryPinPages(long epoch, out string reason)
    {
        if (_publishedEpoch == epoch)
        {
            reason = string.Empty;
            return true;
        }
        var first = ScanPages();
        var second = ScanPages();
        if (!SameReferences(first, second))
        {
            reason = "crafting page authoring changed during the lifecycle capture";
            return false;
        }
        _pages = second;
        _publishedEpoch = epoch;
        reason = string.Empty;
        return true;
    }

    private object[] ScanPages()
    {
        var found = Resources.FindObjectsOfTypeAll(_pageType!);
        var current = new List<object>(found.Length);
        for (var index = 0; index < found.Length; index++)
            if (found[index] is { } page && page.GetType() == _pageType) current.Add(page);
        return current.ToArray();
    }

    private bool HasRecipe(IList instances, Guid recipeId)
    {
        for (var index = 0; index < instances.Count; index++)
        {
            var instance = instances[index];
            if (instance is null) continue;
            if (instance.GetType() == _instanceType && _instanceRecipe!(instance) == recipeId)
                return true;
        }
        return false;
    }

    private int AutomationRepetitions(IList instances, Guid recipeId)
    {
        var found = false;
        var repetitions = 0;
        for (var index = 0; index < instances.Count; index++)
        {
            var instance = instances[index];
            if (instance is null || instance.GetType() != _instanceType) continue;
            if (_instanceRecipe!(instance) != recipeId) continue;
            if (!_instanceIsAuto!(instance))
                throw new InvalidOperationException(
                    "automation queue contained a non-automatic crafting instance");
            if (found)
                throw new InvalidOperationException(
                    "automation queue contained duplicate instances for one recipe");
            found = true;
            repetitions = _instanceAutomationQuantity!(instance);
        }
        return repetitions;
    }

    private static bool SameReferences(object[] first, object[] second)
    {
        if (first.Length != second.Length) return false;
        for (var index = 0; index < first.Length; index++)
        {
            var found = false;
            for (var other = 0; other < second.Length; other++)
                if (ReferenceEquals(first[index], second[other]))
                {
                    found = true;
                    break;
                }
            if (!found) return false;
        }
        return true;
    }

    private static int CountNonNull(IList values)
    {
        var count = 0;
        for (var index = 0; index < values.Count; index++)
            if (values[index] is not null) count++;
        return count;
    }

    private static string JoinFailures(params string[] values)
    {
        var result = string.Empty;
        for (var index = 0; index < values.Length; index++)
        {
            if (values[index].Length == 0) continue;
            result = result.Length == 0 ? values[index] : result + "; " + values[index];
        }
        return result;
    }
}
