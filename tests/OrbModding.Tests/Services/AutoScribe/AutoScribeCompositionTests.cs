using System;
using System.Collections.Generic;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using Xunit;

namespace OrbModding.Tests.Services.AutoScribe;

[Collection(GameActionFaultLogCollection.Name)]
public sealed class AutoScribeCompositionTests : IDisposable
{
    private readonly AutoScribeNativeFixture _native = new();
    private readonly AutoScribeIdentityProfile _profile = AutoScribeIdentityCatalog.Audited;
    private long _epoch = 1;

    public AutoScribeCompositionTests() => AutoScribeNativeFixture.ResetStubs();
    public void Dispose() => AutoScribeNativeFixture.ResetStubs();

    [Fact]
    public void ComposedAutomationUsesTheRegisteredActionAcrossNativeReplacementAndDisposal()
    {
        var first = _native.Build(_profile);
        using var actions = new LifecycleGameActions();
        using var status = Status();
        var composition = Compose(actions, status, static _ => { });
        var config = Configuration();

        var initial = composition.Definition.TryExecute(first.Action, config, default);

        Assert.Equal(ServiceActionDisposition.Committed, initial.Disposition);
        Assert.Same(composition.GameAction, actions.Find<AutoScribeOneShotCraftGameAction>());
        var initialWork = Assert.Single(first.Active.value);
        Assert.Same(first.Recipe, initialWork.reference);
        Assert.Equal(new BigDouble(3, 0), initialWork.Quantity);
        Assert.True(initialWork.Initiated);
        Assert.Equal(1, first.Recipe.PurchaseCalls);
        Assert.Equal(new BigDouble(95, 0), first.Resource.quantity);

        _epoch = 2;
        _native.Registry.Clear();
        var next = _native.Build(_profile);
        var request = new AutoScribeCycleAction(
            next.Action.RecipeId, next.Action.ScrollId, next.Action.Level, _epoch);
        actions.InvalidateLifecycle();
        var replaced = composition.Definition.TryExecute(request, config, default);

        Assert.Equal(ServiceActionDisposition.Committed, replaced.Disposition);
        Assert.Same(next.Recipe, Assert.Single(next.Active.value).reference);
        Assert.Single(first.Active.value);
        Assert.Equal(1, first.Recipe.PurchaseCalls);
        Assert.Equal(1, next.Recipe.PurchaseCalls);

        actions.Dispose();
        var disposed = composition.Definition.TryExecute(request, config, default);

        Assert.Equal(AutoScribeActionResultCodes.ContractUnavailable, disposed.Code);
        Assert.False(composition.GameAction.BindingsAvailable);
        Assert.Equal(1, next.Recipe.PurchaseCalls);
    }

    [Fact]
    public void ComposedAutomationReportsAPaidFailureThroughItsInjectedWarningCallback()
    {
        var native = _native.Build(_profile);
        native.Active.SuppressAdd = true;
        var warnings = new List<string>();
        using var actions = new LifecycleGameActions();
        using var status = Status();
        var composition = Compose(actions, status, warnings.Add);
        var config = Configuration();

        var result = composition.Definition.TryExecute(native.Action, config, default);

        Assert.Equal(AutoScribeActionResultCodes.VerificationFailed, result.Code);
        Assert.Contains("Auto Scribe paid", Assert.Single(warnings));
        Assert.Equal(1, native.Recipe.PurchaseCalls);
        Assert.Empty(native.Active.value);
    }

    private AutoScribeServiceAdapterComposition Compose(
        LifecycleGameActions actions,
        AutomataFeatureStatusReporter status,
        Action<string> warn) =>
        AutoScribeServiceAdapterComposition.Create(new AutoScribeFeatureDependencies(
            _native.Resolver(() => _epoch), _profile, () => _epoch,
            static () => true, static () => true, static () => string.Empty, status, warn), actions);

    private static AutomataFeatureStatusReporter Status() =>
        new(new FeatureStatusRegistry(), new FeatureStatusSnapshot(
            new FeatureStatusKey(PluginIds.SuiteGuid, AutoScribeFeatureContribution.FeatureId),
            "Auto Scribe", true, FeatureStatusState.NotReady,
            new FeatureStatusReason(FeatureStatusReasonCode.GameplayNotReady, "not ready"), 1));

    private static SuiteRuntimeConfiguration Configuration() => new()
    {
        General = new SuiteGeneralConfiguration { Enabled = true },
        AutoItems = new AutoItemsConfiguration { Mode = AutoItemsOperationMode.Active, UseScrolls = true },
        AutoScribe = new AutoScribeConfiguration { Mode = AutoScribeOperationMode.Active },
    };
}
