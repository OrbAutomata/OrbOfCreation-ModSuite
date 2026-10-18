using System;
using System.Collections.Generic;
using OrbModConfig;
using Xunit;

namespace OrbModding.Tests.OrbModConfig;

public sealed class NativeViewAdapterTests : IDisposable
{
    public void Dispose() => NativeViewAdapter.ConfigureFailureLog(null);

    /// <summary>
    /// Opening the Mods tab hides the game's own screens. When one of them throws instead of
    /// hiding, the player is left looking at the wrong screen, and the log says which view threw
    /// and why, once, rather than nothing.
    /// </summary>
    [Fact]
    public void AViewThatThrowsWhenHiddenIsLoggedOnce()
    {
        var logged = new List<string>();
        NativeViewAdapter.ConfigureFailureLog(logged.Add);
        var view = new ThrowingNativeView();

        NativeViewAdapter.SetActive(view, false);
        NativeViewAdapter.SetActive(view, false);

        var line = Assert.Single(logged);
        Assert.Contains("could not hide the game's ThrowingNativeView view", line, StringComparison.Ordinal);
        Assert.Contains("InvalidOperationException: view was torn down", line, StringComparison.Ordinal);
    }

    private sealed class ThrowingNativeView
    {
        public bool IsActive() => true;
        public void SetActive(bool active) => throw new InvalidOperationException("view was torn down");
        public bool IsAvailable() => true;
    }
}
