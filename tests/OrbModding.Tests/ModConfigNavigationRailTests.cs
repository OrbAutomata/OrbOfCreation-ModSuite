using System;
using System.Linq;
using OrbAutomata;
using OrbModConfig;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Xunit;

namespace OrbModding.Tests;

public sealed class ModConfigNavigationRailTests
{
    [Fact]
    public void LastPageRemainsReachableAndAttentionChangesPreserveRowsAndIndependentScroll()
    {
        using var fixture = new Fixture();
        var viewport = (RectTransform)Child(fixture.Root.transform, "NavigationViewport");
        viewport.rect = new Rect(0f, 0f, 220f, 180f);
        fixture.Rail.RefreshLayout(compact: false);
        var scroll = viewport.GetComponent<ScrollRect>()!;
        var content = scroll.content!;
        Assert.NotNull(viewport.GetComponent<RectMask2D>());
        Assert.Same(viewport, scroll.viewport);
        Assert.False(scroll.horizontal);
        Assert.True(scroll.vertical);
        Assert.Equal(ScrollRect.MovementType.Clamped, scroll.movementType);
        Assert.True(viewport.GetComponent<Image>()!.raycastTarget);
        var last = content.GetChild(content.childCount - 1).gameObject;
        var lastRect = (RectTransform)last.transform;
        Assert.Equal(1f, lastRect.anchorMin.y);
        Assert.Equal(1f, lastRect.anchorMax.y);
        Assert.Equal(ModConfigNativeRailFactory.RowHeight, lastRect.sizeDelta.y);

        last.GetComponent<Button>()!.onClick.Invoke();

        Assert.Equal(fixture.Pages.Length - 1, fixture.Selected);
        Assert.True(fixture.Rail.ScrollOffset > 0f);
        Assert.Equal(37f, fixture.Detail.anchoredPosition.y);
        Assert.Same(fixture.Visuals.FeatureRailActiveFrame, last.GetComponent<Image>()!.sprite);
        var selectedOffset = fixture.Rail.ScrollOffset;
        var first = content.GetChild(0).gameObject;
        var changed = fixture.Pages.ToArray();
        changed[0] = ModConfigTopPage.Runtime(3);
        fixture.Rail.SetPages(changed, fixture.Selected, reveal: false);

        Assert.Same(first, content.GetChild(0).gameObject);
        Assert.Same(last, content.GetChild(content.childCount - 1).gameObject);
        Assert.Equal(selectedOffset, fixture.Rail.ScrollOffset);
        Assert.Equal("Runtime (3)", first.GetComponentInChildren<TextMeshProUGUI>(true)!.text);
    }

    [Fact]
    public void OverflowButtonsReachBothEndsAndResizeClampsTheSameOwnedContent()
    {
        using var fixture = new Fixture();
        var viewport = (RectTransform)Child(fixture.Root.transform, "NavigationViewport");
        viewport.rect = new Rect(0f, 0f, 200f, 150f);
        fixture.Rail.RefreshLayout(compact: true);
        var up = Child(fixture.Root.transform, "NavigationUp").GetComponent<Button>()!;
        var down = Child(fixture.Root.transform, "NavigationDown").GetComponent<Button>()!;
        var content = viewport.GetComponent<ScrollRect>()!.content!;
        Assert.True(down.gameObject.activeSelf);
        Assert.False(up.interactable);
        Assert.False(content.GetChild(0).GetComponentInChildren<TextMeshProUGUI>(true)!.gameObject.activeSelf);
        for (var index = 0; index < fixture.Pages.Length; index++) down.onClick.Invoke();
        Assert.True(up.interactable);
        Assert.False(down.interactable);
        Assert.True(fixture.Rail.ScrollOffset > 0f);
        for (var index = 0; index < fixture.Pages.Length; index++) up.onClick.Invoke();
        Assert.Equal(0f, fixture.Rail.ScrollOffset);
        fixture.Rail.RestoreScroll(10000f);
        viewport.rect = new Rect(0f, 0f, 220f, 2000f);
        fixture.Rail.RefreshLayout(compact: false);
        Assert.Equal(0f, fixture.Rail.ScrollOffset);
        Assert.False(up.gameObject.activeSelf);
        Assert.False(down.gameObject.activeSelf);
        Assert.True(content.GetChild(0).GetComponentInChildren<TextMeshProUGUI>(true)!.gameObject.activeSelf);
    }

    [Fact]
    public void MissingAuditedStaticIconFailsInsteadOfSubstitutingAControl()
    {
        using var fixture = new Fixture();
        var owned = new System.Collections.Generic.List<GameObject>();
        Assert.False(ModConfigNativeRailFactory.TryBuild((RectTransform)fixture.Root.transform,
            new[] { ModConfigTopPage.Plugin("Unsupported", 0, 0, NativeFeatureIcon.Unknown) }, 0, owned,
            fixture.Root.AddComponent<TextMeshProUGUI>(), fixture.Visuals, _ => { }, out var reason));
        Assert.Empty(owned);
        Assert.Contains("Unsupported", reason);
        Assert.Contains("no audited native icon", reason);
    }

    private static Transform Child(Transform parent, string name) =>
        Enumerable.Range(0, parent.childCount).Select(parent.GetChild).Single(child => child.gameObject.name == name);

    private sealed class Fixture : IDisposable
    {
        internal readonly GameObject Root = new("Rail", typeof(RectTransform));
        private readonly GameObject _template = new("Template");
        internal readonly RectTransform Detail = (RectTransform)new GameObject("Detail", typeof(RectTransform)).transform;
        internal readonly NativeFeatureRailVisualPrimitives Visuals;
        internal readonly ModConfigTopPage[] Pages;
        internal readonly ModConfigNavigationRail Rail;
        internal int Selected;

        internal Fixture()
        {
            Detail.anchoredPosition = new Vector2(0f, 37f);
            Visuals = new NativeFeatureRailVisualPrimitives(
                new Sprite(), new Sprite(), new Sprite(), new Sprite(), new Sprite(), new Sprite(), new Sprite(), new Sprite());
            ModConfigUiFactory.UseNativeVisuals(Visuals);
            Pages = new[] { ModConfigTopPage.Runtime(0), ModConfigTopPage.Strategist }
                .Concat(Enumerable.Range(0, 18).Select(index => ModConfigTopPage.Plugin("External " + index, 0, index)))
                .ToArray();
            Rail = new ModConfigNavigationRail((RectTransform)Root.transform,
                _template.AddComponent<TextMeshProUGUI>(), Visuals, Pages, 0, Select);
        }

        private void Select(int index)
        {
            Selected = index;
            Rail.SetPages(Pages, index, reveal: true);
        }

        public void Dispose()
        {
            Rail.Dispose();
            UnityEngine.Object.Destroy(Root);
            UnityEngine.Object.Destroy(_template);
            UnityEngine.Object.Destroy(Detail.gameObject);
        }
    }
}
