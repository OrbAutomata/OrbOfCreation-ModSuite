using System;
using System.Linq;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using Xunit;

namespace OrbModding.Tests.Services.AutoConcept.Runtime.ServiceCycle;

public sealed class AutoConceptNativeAdapterTests : IDisposable
{
    private static readonly AutoConceptResourceLimits DefaultLimits =
        AutoConceptResourceLimits.From(new AutoConceptConfiguration());

    private static readonly Guid RecipeId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ReplacementId =
        Guid.Parse("22222222-2222-2222-2222-222222222222");

    public AutoConceptNativeAdapterTests()
    {
        IdScriptableObject.RuntimeLookup.Clear();
    }

    public void Dispose()
    {
        IdScriptableObject.RuntimeLookup.Clear();
    }

    [Theory]
    [Trait("Category", "HeadlessIntegration")]
    [InlineData("ordinary-alchemy")]
    [InlineData("wrong-scholar-type")]
    public void RejectsRecipeListsOutsideTheAuditedConceptDomain(string typeUuid)
    {
        var recipe = new AlchemyRecipeSO("invalid", "Invalid", new[] { new AlchemyTypeSO(typeUuid) });
        InstallNativeLists(recipe);
        using var runtime = new AutoConceptNativeAdapter(new AlchemyGameplayDomainClassifier());

        var initialized = runtime.TryInitialize(out var reason);

        Assert.False(initialized);
        Assert.Contains("without verified Scholar type evidence", reason.Text);
        Assert.False(runtime.IsReady);
    }

    [Fact]
    [Trait("Category", "HeadlessIntegration")]
    public void BatchedDepthRefusesWhenTheLiveMasteryCapCannotHonorTheRequest()
    {
        var resource = new ResourceSO { trueRate = new BigDouble(100.0, 0) };
        var recipe = new AlchemyRecipeSO(
            RecipeId.ToString("D"),
            "Valid concept",
            new[] { new AlchemyTypeSO(AlchemyGameplayDomainClassifier.ReductiveConceptTypeUuid.ToString()) })
        {
            maxUsageSlots = new ValueModifierRecord(new BigDouble(2.0, 0)),
            drainCost = Costs(resource, new BigDouble(10.0, 0)),
        };
        var active = InstallNativeLists(recipe);
        using var runtime = new AutoConceptNativeAdapter(new AlchemyGameplayDomainClassifier());
        var belief = new AutoConceptPlanBelief(0, 0, 2, Guid.Empty, 0);
        var action = new AutoConceptCycleAction(
            AutoConceptActionKind.Add, RecipeId, 20, Guid.Empty, 1, in belief);

        var submission = runtime.Submit(in action, DefaultLimits);

        Assert.Equal(AutoConceptActionResultCodes.AmountUnavailable, submission.Outcome.Code);
        Assert.Contains("at most 2 more", submission.Outcome.Reason, StringComparison.Ordinal);
        Assert.Equal(0, runtime.LastNativeMutationOutcome.NativeCallsAttempted);
        Assert.Equal(0, runtime.LastNativeMutationOutcome.MutationsCommitted);
        Assert.Empty(active.value);
    }

    [Fact]
    [Trait("Category", "HeadlessIntegration")]
    public void ANoOpAssignmentFaultsAndTheNextSubmissionPressesAgain()
    {
        var recipe = new AlchemyRecipeSO(
            RecipeId.ToString("D"),
            "No-op concept",
            new[] { new AlchemyTypeSO(AlchemyGameplayDomainClassifier.ReductiveConceptTypeUuid.ToString()) })
        {
            maxUsageSlots = new ValueModifierRecord(new BigDouble(2.0, 0)),
        };
        var active = InstallNativeLists(recipe);
        active.SuppressAddMutation = true;
        using var runtime = new AutoConceptNativeAdapter(new AlchemyGameplayDomainClassifier());
        var belief = new AutoConceptPlanBelief(0, 0, 2, Guid.Empty, 0);
        var action = new AutoConceptCycleAction(
            AutoConceptActionKind.Add, RecipeId, 1, Guid.Empty, 1, in belief);

        var failed = runtime.Submit(in action, DefaultLimits);

        Assert.Equal(CommonActionResultCodes.AdapterFault, failed.Outcome.Code);
        Assert.Contains("its queued stacks went from 0 to 0.", failed.Outcome.Reason);
        Assert.Equal(1, runtime.LastNativeMutationOutcome.NativeCallsAttempted);
        Assert.Equal(0, runtime.LastNativeMutationOutcome.MutationsCommitted);

        active.SuppressAddMutation = false;
        var retried = runtime.Submit(in action, DefaultLimits);

        Assert.True(retried.Outcome.Verified, retried.Outcome.Reason);
        Assert.Equal(1, runtime.LastNativeMutationOutcome.NativeCallsAttempted);
        Assert.Equal(1, Assert.Single(active.value).queuedQuantity);
    }

    [Fact]
    [Trait("Category", "HeadlessIntegration")]
    public void PositiveDrainFailsClosedWhenNativeResourceIsAtZero()
    {
        var resource = new ResourceSO { quantity = BigDouble.Zero, trueRate = new BigDouble(100.0, 0) };
        var recipe = new AlchemyRecipeSO(
            RecipeId.ToString("D"),
            "Zero resource concept",
            new[] { new AlchemyTypeSO(AlchemyGameplayDomainClassifier.ReflectiveConceptTypeUuid.ToString()) })
        {
            maxUsageSlots = new ValueModifierRecord(new BigDouble(4.0, 0)),
            drainCost = Costs(resource, new BigDouble(1.0, 0)),
        };
        var active = InstallNativeLists(recipe);
        using var runtime = new AutoConceptNativeAdapter(new AlchemyGameplayDomainClassifier());
        var belief = new AutoConceptPlanBelief(0, 0, 4, Guid.Empty, 0);
        var action = new AutoConceptCycleAction(
            AutoConceptActionKind.Add, RecipeId, 4, Guid.Empty, 1, in belief);

        var submission = runtime.Submit(in action, DefaultLimits);

        Assert.Equal(AutoConceptActionResultCodes.AmountUnavailable, submission.Outcome.Code);
        Assert.Contains("at zero", submission.Outcome.Reason);
        Assert.Empty(active.value);
    }

    /// <summary>
    /// A resource sitting at zero is the worker's caution, and the player's press takes the slot.
    /// </summary>
    /// <remarks>
    /// The game does not refuse this press. <c>AlchemyInstanceListVariable.EngageAlchemy</c> gates
    /// on an empty spot or an existing instance, the recipe's free and maximum usage slots, its
    /// usage cost and the multi-buy count; <c>CanAddInstance</c> is that slot test alone. Nothing
    /// on either path reads the drain's zero state — the one caller of
    /// <c>ResourceDrain.AnyResourceAtZero</c> in the assembly is <c>StatusEffect.CheckIfExpired</c>,
    /// which expires a running effect. So refusing the press was the suite's own rule, wearing a
    /// sentence that sounded like the game's.
    /// </remarks>
    [Fact]
    [Trait("Category", "HeadlessIntegration")]
    public void APressIsNotRefusedByAResourceTheGameLetsRunAtZero()
    {
        var resource = new ResourceSO { quantity = BigDouble.Zero, trueRate = new BigDouble(100.0, 0) };
        var recipe = new AlchemyRecipeSO(
            RecipeId.ToString("D"),
            "Zero resource concept",
            new[] { new AlchemyTypeSO(AlchemyGameplayDomainClassifier.ReflectiveConceptTypeUuid.ToString()) })
        {
            maxUsageSlots = new ValueModifierRecord(new BigDouble(4.0, 0)),
            drainCost = Costs(resource, new BigDouble(1.0, 0)),
        };
        var active = InstallNativeLists(recipe);
        using var runtime = new AutoConceptNativeAdapter(new AlchemyGameplayDomainClassifier());
        var belief = new AutoConceptPlanBelief(0, 0, 4, Guid.Empty, 0);
        var action = new AutoConceptCycleAction(
            AutoConceptActionKind.Add, RecipeId, 4, Guid.Empty, 1, in belief);

        var press = runtime.Submit(in action, limits: null);

        Assert.True(press.Outcome.Verified, press.Outcome.Reason);
        Assert.Equal(4, Assert.Single(active.value).queuedQuantity);
    }

    /// <summary>
    /// One drain vector, one resource, two submissions: Auto Concept's own cycle holds the
    /// assignment back, the player's press takes it.
    /// </summary>
    /// <remarks>
    /// The rate reserve is a dial on the worker. The game asks a player
    /// pressing Add for a free slot, a discovered concept and its own price, and nothing else.
    /// </remarks>
    [Fact]
    [Trait("Category", "HeadlessIntegration")]
    public void DialsThatRefuseTheWorkerDoNotRefuseAPress()
    {
        var resource = new ResourceSO
        {
            trueRate = new BigDouble(100.0, 0),
            quantity = new BigDouble(10.0, 0),
        };
        var recipe = new AlchemyRecipeSO(
            RecipeId.ToString("D"),
            "Dialled concept",
            new[] { new AlchemyTypeSO(AlchemyGameplayDomainClassifier.ReflectiveConceptTypeUuid.ToString()) })
        {
            maxUsageSlots = new ValueModifierRecord(new BigDouble(4.0, 0)),
            drainCost = Costs(resource, new BigDouble(50.0, 0)),
        };
        var active = InstallNativeLists(recipe);
        using var runtime = new AutoConceptNativeAdapter(new AlchemyGameplayDomainClassifier());
        var belief = new AutoConceptPlanBelief(0, 0, 4, Guid.Empty, 0);
        var action = new AutoConceptCycleAction(
            AutoConceptActionKind.Add, RecipeId, 1, Guid.Empty, 1, in belief);

        var worker = runtime.Submit(
            in action, new AutoConceptResourceLimits(90.0f));

        Assert.Equal(AutoConceptActionResultCodes.AmountUnavailable, worker.Outcome.Code);
        Assert.Contains("rate reserve", worker.Outcome.Reason, StringComparison.Ordinal);
        Assert.Empty(active.value);

        var press = runtime.Submit(in action, limits: null);

        Assert.True(press.Outcome.Verified, press.Outcome.Reason);
        Assert.Equal(1, Assert.Single(active.value).queuedQuantity);
    }

    /// <summary>
    /// The boundary keeps the same rate reserve the planner keeps: for ordinary rates, rates under
    /// one, a rate already at zero, and a rate the add would take to exactly zero.
    /// </summary>
    [Theory]
    [Trait("Category", "HeadlessIntegration")]
    [InlineData(1.0, 0.5, 0f, true)]
    [InlineData(0.5, 0.5, 10f, false)]
    [InlineData(1.0, 0.25, 50f, true)]
    [InlineData(1.0, 0.75, 50f, false)]
    [InlineData(0.5, 0.5, 0f, true)]
    [InlineData(0.0, 0.5, 0f, false)]
    [InlineData(20.0, 5.0, 50f, true)]
    [InlineData(20.0, 15.0, 50f, false)]
    public void TheBoundaryKeepsThePlannersRateReserve(
        double trueRate,
        double drain,
        float reservePercent,
        bool accepted)
    {
        var resource = new ResourceSO
        {
            trueRate = new BigDouble(trueRate),
            quantity = new BigDouble(10.0, 0),
        };
        var recipe = new AlchemyRecipeSO(
            RecipeId.ToString("D"),
            "Slow concept",
            new[] { new AlchemyTypeSO(AlchemyGameplayDomainClassifier.ReflectiveConceptTypeUuid.ToString()) })
        {
            maxUsageSlots = new ValueModifierRecord(new BigDouble(4.0, 0)),
            drainCost = Costs(resource, new BigDouble(drain)),
        };
        var active = InstallNativeLists(recipe);
        using var runtime = new AutoConceptNativeAdapter(new AlchemyGameplayDomainClassifier());
        var belief = new AutoConceptPlanBelief(0, 0, 4, Guid.Empty, 0);
        var action = new AutoConceptCycleAction(
            AutoConceptActionKind.Add, RecipeId, 1, Guid.Empty, 1, in belief);

        var submission = runtime.Submit(in action, new AutoConceptResourceLimits(reservePercent));

        if (accepted)
        {
            Assert.True(submission.Outcome.Verified, submission.Outcome.Reason);
            Assert.Equal(1, Assert.Single(active.value).queuedQuantity);
        }
        else
        {
            Assert.Equal(AutoConceptActionResultCodes.AmountUnavailable, submission.Outcome.Code);
            Assert.Contains("rate reserve", submission.Outcome.Reason, StringComparison.Ordinal);
            Assert.Empty(active.value);
        }
    }

    [Fact]
    [Trait("Category", "HeadlessIntegration")]
    public void SubmitRevalidatesIdentityQuantityProjectionAndVerifierAtTheBoundary()
    {
        var recipe = new AlchemyRecipeSO(
            RecipeId.ToString("D"),
            "Verified concept",
            new[] { new AlchemyTypeSO(AlchemyGameplayDomainClassifier.ReductiveConceptTypeUuid.ToString()) })
        {
            maxUsageSlots = new ValueModifierRecord(new BigDouble(2.0, 0)),
        };
        var active = InstallNativeLists(recipe);
        using var runtime = new AutoConceptNativeAdapter(new AlchemyGameplayDomainClassifier());
        var belief = new AutoConceptPlanBelief(0, 0, 2, Guid.Empty, 0);
        var action = new AutoConceptCycleAction(
            AutoConceptActionKind.Add, RecipeId, 1, Guid.Empty, 1, in belief);

        var submission = runtime.Submit(in action, DefaultLimits);

        Assert.True(submission.Outcome.Verified, submission.Outcome.Reason);
        Assert.Equal(1, Assert.Single(active.value).queuedQuantity);
    }

    [Fact]
    [Trait("Category", "HeadlessIntegration")]
    public void SubmitRefusesWhenTheLiveQuantityNoLongerMatchesThePlanBelief()
    {
        var recipe = new AlchemyRecipeSO(
            RecipeId.ToString("D"),
            "Changed concept",
            new[] { new AlchemyTypeSO(AlchemyGameplayDomainClassifier.ReductiveConceptTypeUuid.ToString()) })
        {
            maxUsageSlots = new ValueModifierRecord(new BigDouble(2.0, 0)),
        };
        var active = InstallNativeLists(recipe);
        active.value.Add(new AlchemyInstance(recipe) { quantity = 1, queuedQuantity = 1 });
        using var runtime = new AutoConceptNativeAdapter(new AlchemyGameplayDomainClassifier());
        var belief = new AutoConceptPlanBelief(0, 0, 2, Guid.Empty, 0);
        var action = new AutoConceptCycleAction(
            AutoConceptActionKind.Add, RecipeId, 2, Guid.Empty, 1, in belief);

        var submission = runtime.Submit(in action, DefaultLimits);

        Assert.Equal(AutoConceptActionResultCodes.OwnershipChanged, submission.Outcome.Code);
        Assert.Equal(0, runtime.LastNativeMutationOutcome.NativeCallsAttempted);
        Assert.Equal(1, Assert.Single(active.value).queuedQuantity);
    }

    [Fact]
    [Trait("Category", "HeadlessIntegration")]
    public void RotationCanUseAReleasedTypelessSlotAcrossConceptTypes()
    {
        var activeType = new AlchemyTypeSO(
            AlchemyGameplayDomainClassifier.ReductiveConceptTypeUuid.ToString());
        var replacementType = new AlchemyTypeSO(
            AlchemyGameplayDomainClassifier.ReflectiveConceptTypeUuid.ToString());
        var activeRecipe = RecipeWithType(RecipeId, "Active", activeType);
        var replacementRecipe = RecipeWithType(ReplacementId, "Replacement", replacementType);
        var active = InstallNativeLists(activeRecipe, replacementRecipe);
        active.TypelessSlots = 1;
        active.value.Add(new AlchemyInstance(activeRecipe) { quantity = 1, queuedQuantity = 1 });
        using var runtime = new AutoConceptNativeAdapter(new AlchemyGameplayDomainClassifier());
        var belief = new AutoConceptPlanBelief(1, 1, 1, Guid.Empty, 0);
        var action = new AutoConceptCycleAction(
            AutoConceptActionKind.RotateOut,
            RecipeId,
            1,
            ReplacementId,
            1,
            in belief);

        var submission = runtime.Submit(in action, DefaultLimits);

        Assert.True(submission.Outcome.Verified, submission.Outcome.Reason);
        Assert.Empty(active.value);
    }

    [Fact]
    [Trait("Category", "HeadlessIntegration")]
    public void RotationDoesNotTradeATypeSpecificSlotForAnotherType()
    {
        var activeType = new AlchemyTypeSO(
            AlchemyGameplayDomainClassifier.ReductiveConceptTypeUuid.ToString());
        var replacementType = new AlchemyTypeSO(
            AlchemyGameplayDomainClassifier.ReflectiveConceptTypeUuid.ToString());
        var activeRecipe = RecipeWithType(RecipeId, "Active", activeType);
        var replacementRecipe = RecipeWithType(ReplacementId, "Replacement", replacementType);
        var active = InstallNativeLists(activeRecipe, replacementRecipe);
        active.TypelessSlots = 0;
        active.TypeSlots[activeType] = 1;
        active.value.Add(new AlchemyInstance(activeRecipe) { quantity = 1, queuedQuantity = 1 });
        using var runtime = new AutoConceptNativeAdapter(new AlchemyGameplayDomainClassifier());
        var belief = new AutoConceptPlanBelief(1, 1, 1, Guid.Empty, 0);
        var action = new AutoConceptCycleAction(
            AutoConceptActionKind.RotateOut,
            RecipeId,
            1,
            ReplacementId,
            1,
            in belief);

        var submission = runtime.Submit(in action, DefaultLimits);

        Assert.Equal(AutoConceptActionResultCodes.SlotUnavailable, submission.Outcome.Code);
        Assert.Equal(1, Assert.Single(active.value).queuedQuantity);
    }

    [Fact]
    [Trait("Category", "HeadlessIntegration")]
    public void RotationDoesNotDegradeWhenTheNativeAdmissionReadThrows()
    {
        var activeType = new AlchemyTypeSO(
            AlchemyGameplayDomainClassifier.ReductiveConceptTypeUuid.ToString());
        var replacementType = new AlchemyTypeSO(
            AlchemyGameplayDomainClassifier.ReflectiveConceptTypeUuid.ToString());
        var activeRecipe = RecipeWithType(RecipeId, "Active", activeType);
        var replacementRecipe = RecipeWithType(ReplacementId, "Replacement", replacementType);
        var active = InstallNativeLists(activeRecipe, replacementRecipe);
        active.TypelessSlots = 1;
        active.ThrowOnCanAdd = true;
        active.value.Add(new AlchemyInstance(activeRecipe) { quantity = 1, queuedQuantity = 1 });
        using var runtime = new AutoConceptNativeAdapter(new AlchemyGameplayDomainClassifier());
        var belief = new AutoConceptPlanBelief(1, 1, 1, Guid.Empty, 0);
        var action = new AutoConceptCycleAction(
            AutoConceptActionKind.RotateOut,
            RecipeId,
            1,
            ReplacementId,
            1,
            in belief);

        var exception = Assert.Throws<InvalidOperationException>(
            () => runtime.Submit(in action, DefaultLimits));

        Assert.Equal("CanAddInstance failed", exception.Message);
        Assert.Equal(1, Assert.Single(active.value).queuedQuantity);
    }

    [Fact]
    [Trait("Category", "HeadlessIntegration")]
    public void RotationRevalidatesThatTheReplacementIsStillUnlocked()
    {
        var type = new AlchemyTypeSO(
            AlchemyGameplayDomainClassifier.ReductiveConceptTypeUuid.ToString());
        var activeRecipe = RecipeWithType(RecipeId, "Active", type);
        var replacementRecipe = RecipeWithType(ReplacementId, "Locked", type);
        replacementRecipe.discovered = false;
        var active = InstallNativeLists(activeRecipe, replacementRecipe);
        active.TypelessSlots = 1;
        active.value.Add(new AlchemyInstance(activeRecipe) { quantity = 1, queuedQuantity = 1 });
        using var runtime = new AutoConceptNativeAdapter(new AlchemyGameplayDomainClassifier());
        var belief = new AutoConceptPlanBelief(1, 1, 1, Guid.Empty, 0);
        var action = new AutoConceptCycleAction(
            AutoConceptActionKind.RotateOut,
            RecipeId,
            1,
            ReplacementId,
            1,
            in belief);

        var submission = runtime.Submit(in action, DefaultLimits);

        Assert.Equal(AutoConceptActionResultCodes.SlotUnavailable, submission.Outcome.Code);
        Assert.Contains("unlock", submission.Outcome.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, Assert.Single(active.value).queuedQuantity);
    }

    [Fact]
    [Trait("Category", "HeadlessIntegration")]
    public void AConceptThatIsNoLongerDiscoveredRefusesAsUndiscoveredNotAsAChangedIdentity()
    {
        var type = new AlchemyTypeSO(
            AlchemyGameplayDomainClassifier.ReductiveConceptTypeUuid.ToString());
        var recipe = RecipeWithType(RecipeId, "Locked", type);
        recipe.discovered = false;
        var active = InstallNativeLists(recipe);
        using var runtime = new AutoConceptNativeAdapter(new AlchemyGameplayDomainClassifier());
        var belief = new AutoConceptPlanBelief(0, 0, 1, Guid.Empty, 0);
        var action = new AutoConceptCycleAction(
            AutoConceptActionKind.Add, RecipeId, 1, Guid.Empty, 1, in belief);

        var submission = runtime.Submit(in action, DefaultLimits);

        Assert.Equal("not_discovered", submission.Outcome.Code.Name);
        Assert.Equal(0, runtime.LastNativeMutationOutcome.NativeCallsAttempted);
        Assert.Empty(active.value);
    }

    [Fact]
    [Trait("Category", "HeadlessIntegration")]
    public void AThrownCandidateReadReachesTheCallerInsteadOfReadingAsAChangedIdentity()
    {
        var type = new AlchemyTypeSO(
            AlchemyGameplayDomainClassifier.ReductiveConceptTypeUuid.ToString());
        var recipe = RecipeWithType(RecipeId, "Unreadable", type);
        recipe.DiscoveredReadFailure = new InvalidOperationException("injected discovery read failure");
        var active = InstallNativeLists(recipe);
        using var runtime = new AutoConceptNativeAdapter(new AlchemyGameplayDomainClassifier());
        var belief = new AutoConceptPlanBelief(0, 0, 1, Guid.Empty, 0);
        var action = new AutoConceptCycleAction(
            AutoConceptActionKind.Add, RecipeId, 1, Guid.Empty, 1, in belief);

        var exception = Assert.ThrowsAny<Exception>(() => runtime.Submit(in action, DefaultLimits));

        Assert.Contains("injected discovery read failure", exception.GetBaseException().Message);
        Assert.Empty(active.value);
    }

    [Fact]
    [Trait("Category", "HeadlessIntegration")]
    public void Every_missing_member_disables_the_complete_auto_concept_binding_set()
    {
        foreach (var missing in AutoConceptNativeBindings.ContractIds)
        {
            IdScriptableObject.RuntimeLookup.Clear();
            var type = new AlchemyTypeSO(AlchemyGameplayDomainClassifier.ReductiveConceptTypeUuid.ToString());
            var active = InstallNativeLists(RecipeWithType(RecipeId, "Concept", type));
            using var runtime = new AutoConceptNativeAdapter(
                new AlchemyGameplayDomainClassifier(),
                registryResolver: null,
                resolveType: null,
                includeContract: id => id != missing);
            var belief = new AutoConceptPlanBelief(0, 0, 1, Guid.Empty, 0);
            var action = new AutoConceptCycleAction(
                AutoConceptActionKind.Add, RecipeId, 1, Guid.Empty, 1, in belief);

            Assert.False(runtime.TryInitialize(out var reason));
            Assert.Contains(missing, reason.Text, StringComparison.Ordinal);
            var submission = runtime.Submit(in action, DefaultLimits);

            Assert.Equal(CommonActionResultCodes.AdapterFault, submission.Outcome.Code);
            Assert.False(submission.Outcome.ToResult().HasNativeEvidence);
            Assert.Contains(missing, submission.Outcome.Reason, StringComparison.Ordinal);
            Assert.False(runtime.IsReady);
            Assert.Empty(active.value);
        }
    }

    /// <summary>
    /// The projection multiplies the drain through the game's <c>Multiply(BigDouble)</c>, not
    /// whichever one-parameter <c>Multiply</c> on the runtime list accepts the argument.
    /// </summary>
    [Fact]
    [Trait("Category", "HeadlessIntegration")]
    public void The_drain_projection_multiplies_through_the_declared_overload()
    {
        var resource = new ResourceSO { trueRate = new BigDouble(1.0), quantity = new BigDouble(10.0, 0) };
        var active = InstallDrainingConcept(new DecoyCostList { costs = { new ResourceTuple(resource, new BigDouble(0.75)) } });
        using var runtime = new AutoConceptNativeAdapter(new AlchemyGameplayDomainClassifier());

        var submission = runtime.Submit(in AddOne, new AutoConceptResourceLimits(50f));

        Assert.Equal(AutoConceptActionResultCodes.AmountUnavailable, submission.Outcome.Code);
        Assert.Contains("rate reserve", submission.Outcome.Reason, StringComparison.Ordinal);
        Assert.Empty(active.value);
    }

    /// <summary>
    /// The quality conversion is the game's <c>ResourceSO.GetTrueSpend(BigDouble)</c>, not a
    /// look-alike the runtime resource type declares.
    /// </summary>
    [Fact]
    [Trait("Category", "HeadlessIntegration")]
    public void The_quality_conversion_calls_the_declared_true_spend()
    {
        var resource = new DecoySpendResource { trueRate = new BigDouble(1.0), quantity = new BigDouble(10.0, 0) };
        var active = InstallDrainingConcept(Costs(resource, new BigDouble(0.75)));
        using var runtime = new AutoConceptNativeAdapter(new AlchemyGameplayDomainClassifier());

        var submission = runtime.Submit(in AddOne, new AutoConceptResourceLimits(50f));

        Assert.Equal(AutoConceptActionResultCodes.AmountUnavailable, submission.Outcome.Code);
        Assert.Contains("rate reserve", submission.Outcome.Reason, StringComparison.Ordinal);
        Assert.Empty(active.value);
    }

    /// <summary>
    /// The mastery limit is <c>GetMaxUsageSlots()</c> alone. A throw from it reaches the caller as
    /// the game threw it; it never falls back to reading the <c>maxUsageSlots</c> record as a number.
    /// </summary>
    [Fact]
    [Trait("Category", "HeadlessIntegration")]
    public void A_thrown_mastery_limit_read_reaches_the_caller_without_a_field_fallback()
    {
        var type = new AlchemyTypeSO(AlchemyGameplayDomainClassifier.ReductiveConceptTypeUuid.ToString());
        var recipe = RecipeWithType(RecipeId, "Unreadable limit", type);
        recipe.MaxUsageReadFailure = new InvalidOperationException("injected mastery limit read failure");
        var active = InstallNativeLists(recipe);
        using var runtime = new AutoConceptNativeAdapter(new AlchemyGameplayDomainClassifier());

        var exception = Assert.Throws<InvalidOperationException>(() => runtime.Submit(in AddOne, DefaultLimits));

        Assert.Equal("injected mastery limit read failure", exception.Message);
        Assert.Empty(active.value);
    }

    /// <summary>
    /// The projection reads the rate through the bound <c>ResourceSO.GetTrueRate()</c>, not a method
    /// looked up by name on the runtime resource type at call time.
    /// </summary>
    [Fact]
    [Trait("Category", "HeadlessIntegration")]
    public void The_drain_projection_reads_the_bound_true_rate()
    {
        var resource = new DecoyRateResource { trueRate = new BigDouble(1.0), quantity = new BigDouble(10.0, 0) };
        var active = InstallDrainingConcept(Costs(resource, new BigDouble(0.75)));
        using var runtime = new AutoConceptNativeAdapter(new AlchemyGameplayDomainClassifier());

        var submission = runtime.Submit(in AddOne, new AutoConceptResourceLimits(50f));

        Assert.Equal(AutoConceptActionResultCodes.AmountUnavailable, submission.Outcome.Code);
        Assert.Contains("rate reserve", submission.Outcome.Reason, StringComparison.Ordinal);
        Assert.Empty(active.value);
    }

    /// <summary>
    /// An unexpected throw from the game inside the projection escapes loudly; it never becomes a
    /// "rate is unavailable" refusal the worker would read as an ordinary hold.
    /// </summary>
    [Fact]
    [Trait("Category", "HeadlessIntegration")]
    public void An_unexpected_throw_inside_the_drain_projection_escapes()
    {
        var resource = new ResourceSO
        {
            trueRate = new BigDouble(1.0),
            quantity = new BigDouble(10.0, 0),
            TrueRateReadFailure = new NullReferenceException("injected rate read failure"),
        };
        var active = InstallDrainingConcept(Costs(resource, new BigDouble(0.75)));
        using var runtime = new AutoConceptNativeAdapter(new AlchemyGameplayDomainClassifier());

        var exception = Assert.Throws<NullReferenceException>(
            () => runtime.Submit(in AddOne, new AutoConceptResourceLimits(50f)));

        Assert.Equal("injected rate read failure", exception.Message);
        Assert.Empty(active.value);
    }

    private static readonly AutoConceptCycleAction AddOne = new(
        AutoConceptActionKind.Add, RecipeId, 1, Guid.Empty, 1, new AutoConceptPlanBelief(0, 0, 4, Guid.Empty, 0));

    private static AlchemyInstanceListVariable InstallDrainingConcept(ResourceCostList drainCost) =>
        InstallNativeLists(new AlchemyRecipeSO(
            RecipeId.ToString("D"),
            "Draining concept",
            new[] { new AlchemyTypeSO(AlchemyGameplayDomainClassifier.ReflectiveConceptTypeUuid.ToString()) })
        {
            maxUsageSlots = new ValueModifierRecord(new BigDouble(4.0, 0)),
            drainCost = drainCost,
        });

    private sealed class DecoyCostList : ResourceCostList
    {
        public ResourceCostList Multiply(object factor) => new();
    }

    private sealed class DecoySpendResource : ResourceSO
    {
        public BigDouble GetTrueSpend(object amount) => BigDouble.Zero;
    }

    private sealed class DecoyRateResource : ResourceSO
    {
        public new BigDouble GetTrueRate() => new BigDouble(1.0, 6);
    }

    private static ResourceCostList Costs(ResourceSO resource, BigDouble value) =>
        new() { costs = { new ResourceTuple(resource, value) } };

    private static AlchemyRecipeSO RecipeWithType(
        Guid id,
        string name,
        AlchemyTypeSO type) =>
        new(id.ToString("D"), name, new[] { type })
        {
            coreType = type,
            maxUsageSlots = new ValueModifierRecord(new BigDouble(1.0, 0)),
        };

    private static AlchemyInstanceListVariable InstallNativeLists(params AlchemyRecipeSO[] recipes)
    {
        var active = new AlchemyInstanceListVariable();
        active.SetGuid(new Guid(AutoConceptNativeAdapter.ActiveConceptsUuid));
        var recipeList = new AlchemyRecipeListVariable { value = recipes.ToList() };
        recipeList.SetGuid(AlchemyGameplayDomainClassifier.ConceptRecipesUuid);
        IdScriptableObject.RuntimeLookup[new Guid(AutoConceptNativeAdapter.ActiveConceptsUuid)] = active;
        IdScriptableObject.RuntimeLookup[AlchemyGameplayDomainClassifier.ConceptRecipesUuid] = recipeList;
        return active;
    }
}
