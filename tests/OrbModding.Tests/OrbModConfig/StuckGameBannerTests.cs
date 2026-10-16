using System.Linq;
using OrbModConfig;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Xunit;

namespace OrbModding.Tests.OrbModConfig;

public sealed class StuckGameBannerTests
{
    [Fact]
    public void TheBannerSpansTheRootCanvasPassesClicksThroughAndUpdatesInPlace()
    {
        var root = new GameObject("canvas", typeof(RectTransform)).transform;
        var panelParent = new GameObject("panels", typeof(RectTransform)).transform;
        panelParent.SetParent(root, false);
        var banner = new StuckGameBanner(
            panelParent,
            new GameObject("label", typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>()!);

        banner.Show("first");
        banner.Show("second");

        var strip = Enumerable.Range(0, root.childCount)
            .Select(root.GetChild)
            .Single(child => child.gameObject.name == StuckGameBanner.ObjectName);
        Assert.False(strip.GetComponent<Image>()!.raycastTarget);
        Assert.Equal("second", strip.GetChild(0).GetComponent<TextMeshProUGUI>()!.text);
        Assert.Equal(root.childCount - 1, strip.GetSiblingIndex());

        banner.Show(null);

        Assert.Null(banner.Shown);
    }
}
