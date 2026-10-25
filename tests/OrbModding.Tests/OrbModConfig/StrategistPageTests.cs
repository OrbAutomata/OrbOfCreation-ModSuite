using System;
using System.Collections.Generic;
using System.Linq;
using OrbAutomata;
using OrbModConfig;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Common.Runtime.Strategy;
using OrbModding.Tests.Strategist;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Xunit;

namespace OrbModding.Tests.OrbModConfig;

public sealed class StrategistPageTests
{
    private const string RootId = "14b35ebc-f284-4d53-bd3f-f57a885cf2b1";
    private const string GoalId = "5a9c3e71-8b2d-4f60-a1c4-d7e8f9012345";
    private const string ResourceId = "3f1c0a52-7d4e-4b8a-9c21-5e6f70819203";

    private static StrategistBulletin Derive(string resource, string gain, string extra = "")
    {
        var (facts, window) = StrategistFactsParser.Parse(string.Join("\n",
            $"facts world=4 played=5400 reset=1800 challenges=1 root={RootId}",
            "open name=Spellcraft",
            $"resource {ResourceId} {resource} name=Mana",
            $"node {RootId} upgrade gate name=Restoration",
            $"node {GoalId} upgrade offered gate name=Keystone",
            $"price {GoalId} {ResourceId} amount=100",
            $"edge {RootId} {GoalId}",
            "window required=60", gain, extra));
        return StrategistDerivation.Derive(facts, window);
    }

    [Theory]
    [InlineData("held=10 cap=50", 600, 60, "Increase storage capacity", "capacity 50; raise it to at least 100")]
    [InlineData("held=10 cap=500", 0, 60, "Find a source of income", "no income measured in this window")]
    [InlineData("held=10 cap=500", 100, 10, "Wait for income measurement", "income is still being measured")]
    [InlineData("held=10 cap=500 points", 100, 60, "Gain allocation points", "allocation points required; no time estimate")]
    [InlineData("held=10 cap=500", 600, 60, "Save resources", "estimated wait")]
    [InlineData("held=100 cap=500", 100, 10, "Ready in this snapshot", "cost covered")]
    public void CardsRenderShippingJudgmentsAndExactMissingAmounts(
        string resource, int gained, int span, string action, string reason)
    {
        var bulletin = Derive(resource, $"gain {ResourceId} gained={gained} span={span}");
        var cards = StrategistPresentation.Build(bulletin, string.Empty);
        var active = Assert.Single(cards, card => card.Title == "Active goal");

        Assert.Contains(action + " — Keystone", active.Body);
        Assert.Contains(reason, active.Body);
        Assert.Contains(resource.StartsWith("held=100", StringComparison.Ordinal) ? "covered" : "missing 90", active.Body);
        Assert.Contains("Relevant to captured Restoration prerequisites", active.Body);
        Assert.Contains("Referenced by prerequisite rules: Restoration", active.Body);
        Assert.DoesNotContain(GoalId, string.Join("\n", cards.Select(card => card.Body)));
        Assert.Contains("about once a minute", cards[0].Body);
        Assert.Contains("assume other spending stops", cards[0].Body);
    }

    [Fact]
    public void ExistingPublicationRendersItsExactUnavailableReasonAsSnapshotContext()
    {
        var cards = StrategistPresentation.Build(
            Derive("held=10 cap=500", $"gain {ResourceId} gained=600 span=60"),
            "Strategist evaluation failed: invalid income sample.");
        Assert.Contains("Snapshot retained: Strategist evaluation failed: invalid income sample.", cards[0].Body);
        Assert.Contains("Captured at", cards[0].Body);
    }

    [Fact]
    public void RealPageConsumesPublisherAndClearsGoalsAcrossLifecycleAndStartupAdmission()
    {
        using var registry = new ServiceCycleRegistry(1);
        registry.Seal();
        Assert.True(registry.RequestLifecycle(new LifecycleGeneration(1)));
        var blocker = string.Empty;
        var source = new DeferredStrategistBulletinSource(() => registry.Strategy, () => blocker);
        var root = new GameObject("StrategistTest", typeof(RectTransform));
        var viewport = new GameObject("Viewport", typeof(RectTransform));
        viewport.transform.SetParent(root.transform, false);
        ((RectTransform)viewport.transform).rect = new Rect(0f, 0f, 800f, 200f);
        var content = new GameObject("Content", typeof(RectTransform));
        content.transform.SetParent(viewport.transform, false);
        var contentRect = (RectTransform)content.transform;
        contentRect.rect = new Rect(0f, 0f, 800f, 0f);
        var scroll = viewport.AddComponent<ScrollRect>();
        scroll.viewport = (RectTransform)viewport.transform;
        scroll.content = contentRect;
        var template = new GameObject("Template").AddComponent<TextMeshProUGUI>();
        var prototype = new GameObject("NativePrototype", typeof(RectTransform));
        ModConfigUiFactory.UseNativeVisuals(new NativeFeatureRailVisualPrimitives(new Sprite(), new Sprite(), new Sprite(), new Sprite(),
            new Sprite(), new Sprite(), new Sprite(), new Sprite()));
        using var page = new StrategistPage(contentRect, scroll, template, source);
        page.Show();
        Assert.Contains("Waiting for the first world snapshot", Text(content.transform));

        var bulletin = Derive("held=10 cap=500", $"gain {ResourceId} gained=600 span=60");
        StrategistService.Publish(registry.StrategistBulletinPublication, new StrategistCycleAction(bulletin));
        page.Refresh();
        Assert.Contains("Save resources — Keystone", Text(content.transform));
        contentRect.anchoredPosition = new Vector2(0f, 60f);
        page.Refresh();
        Assert.Equal(60f, page.ScrollOffset);
        page.Hide();
        page.Show();
        Assert.Equal(60f, page.ScrollOffset);
        var wideContentHeight = contentRect.sizeDelta.y;
        contentRect.rect = new Rect(0f, 0f, 500f, 0f);
        page.Refresh();
        Assert.Equal(60f, page.ScrollOffset);
        Assert.True(contentRect.sizeDelta.y > wideContentHeight);

        Assert.True(registry.RequestLifecycle(new LifecycleGeneration(2)));
        page.Refresh();
        UnityEngine.Object.FlushDestroyQueue();
        Assert.Contains("Waiting for the first world snapshot", Text(content.transform));
        Assert.DoesNotContain("Keystone", Text(content.transform));
        Assert.Equal(1, content.transform.childCount);
        Assert.Equal(0f, page.ScrollOffset);

        blocker = "Installed game assemblies are quarantined pending an exact-build acknowledgement.";
        page.Refresh();
        UnityEngine.Object.FlushDestroyQueue();
        Assert.Contains(blocker, Text(content.transform));
        Assert.DoesNotContain("Waiting for the first world snapshot", Text(content.transform));
        Assert.Null(source.ReadStrategistBulletin());
        UnityEngine.Object.Destroy(root);
        UnityEngine.Object.Destroy(template.gameObject);
        UnityEngine.Object.Destroy(prototype);
    }

    private static string Text(Transform root) => string.Join("\n", Labels(root).Select(label => label.text));
    private static IEnumerable<TextMeshProUGUI> Labels(Transform root)
    {
        var label = root.gameObject.GetComponent<TextMeshProUGUI>();
        if (label is not null) yield return label;
        for (var index = 0; index < root.childCount; index++)
            foreach (var childLabel in Labels(root.GetChild(index))) yield return childLabel;
    }
}
