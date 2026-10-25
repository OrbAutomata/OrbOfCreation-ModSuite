using System;
using System.Reflection;
using Xunit;

namespace OrbModding.Tests;

/// <summary>
/// Lifecycle observation is the suite's, not Mentor's: the collected epoch every world reader
/// compares against only moves because these five hooks fire. These pin the set and its shape. What
/// cannot be pinned headlessly is the call site itself — composition needs a bound configuration, a
/// Harmony instance and a live Chainloader — so the guarantee that the set is installed above
/// Mentor's first early return lives in ComposeAutomata and in the comment on Mentor's composition,
/// not in an assertion here.
/// </summary>
public sealed class PluginLifecycleObservationHookTests
{
    [Fact]
    public void LifecycleObservationHooksNameEveryTransitionTheSuiteWatches()
    {
        Assert.Equal(
            new[]
            {
                ("SaveStateManager:ImplementLoadedJson", "BeforeSaveLoad", false),
                ("SaveStateManager:ImplementLoadedJson", "AfterSaveLoaded", true),
                ("GameManager:InitGame", "AfterGameInitialized", true),
                ("GameManager:ResetGameState", "BeforeRuntimeReset", false),
                ("PersistentResetManager:PersistentResetLogic", "BeforePersistentReset", false),
            },
            global::OrbModding.Plugin.LifecycleObservationHooks);
    }

    [Fact]
    public void EveryLifecycleObservationHandlerIsAPatchTheSuiteCanActuallyInstall()
    {
        foreach (var hook in global::OrbModding.Plugin.LifecycleObservationHooks)
        {
            var handler = typeof(global::OrbModding.Plugin).GetMethod(
                hook.Handler,
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.True(handler is not null, $"{hook.Handler} is not a static handler on the plugin");
            Assert.All(
                handler!.GetParameters(),
                parameter =>
                {
                    Assert.Equal(typeof(object), parameter.ParameterType);
                    Assert.StartsWith("__", parameter.Name ?? string.Empty, StringComparison.Ordinal);
                });
        }
    }
}
