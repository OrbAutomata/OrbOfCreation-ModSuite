using System;
using System.Collections.Generic;
using System.Threading;
using OrbAutomata.GameMcp;
using Xunit;

namespace OrbModding.ProfileTests;

public sealed class GameMcpTooltipNativeAccessTests
{
    [Fact]
    public void StartupBindingRequiresTheExactAuditedCollectionShape()
    {
        Assert.False(Bind(typeof(MissingSubTooltips), out _, out var missingReason));
        Assert.Contains("subTooltips", missingReason);

        Assert.False(Bind(typeof(WrongElementType), out _, out var wrongReason));
        Assert.Contains("List<ITooltipable>", wrongReason);
    }

    /// <summary>
    /// The two recipe accessors are bound at startup like every other tooltip member, so a build
    /// that moved either refuses the whole surface rather than quietly shipping panels with no ids
    /// on them.
    /// </summary>
    [Fact]
    public void StartupBindingRequiresBothRecipeAccessors()
    {
        Assert.False(GameMcpTooltipNativeAccess.TryCreate(
            typeof(global::HoverTooltip),
            typeof(MissingSubTooltips),
            typeof(global::PassiveAbility),
            out _,
            out var spellReason));
        Assert.Contains("Spell.get_reference", spellReason);

        Assert.False(GameMcpTooltipNativeAccess.TryCreate(
            typeof(global::HoverTooltip),
            typeof(global::Spell),
            typeof(MissingSubTooltips),
            out _,
            out var passiveReason));
        Assert.Contains("PassiveAbility.get_reference", passiveReason);
    }

    [Fact]
    public void BoundAccessorReadsLiveObjectsWithoutMemberDiscoveryAtExecution()
    {
        Assert.True(Bind(typeof(global::HoverTooltip), out var access, out var bindingReason),
            bindingReason);
        var child = new FakeTooltip("nested");
        var hover = new global::HoverTooltip();
        hover.Setup(
            new FakeTooltip("primary"),
            new List<global::ITooltipable> { child });

        Assert.True(access.TryReadSubTooltips(hover, out var nested, out var readReason),
            readReason);
        Assert.Same(child, Assert.Single(nested));
    }

    [Fact]
    public void BoundAccessorRejectsOffThreadNativeAccess()
    {
        Assert.True(Bind(typeof(global::HoverTooltip), out var access, out var bindingReason),
            bindingReason);
        var hover = new global::HoverTooltip();
        hover.Setup(new FakeTooltip("primary"));

        var accepted = true;
        var refusalReason = string.Empty;
        var thread = new Thread(() =>
        {
            accepted = access.TryReadSubTooltips(hover, out _, out refusalReason);
        });
        thread.Start();
        thread.Join();

        Assert.False(accepted);
        Assert.Contains("off the Unity startup thread", refusalReason);
    }

    /// <summary>
    /// An asset answers with its own id; the two live instances answer with the asset they were
    /// built from; anything else answers with nothing, because there is no entity behind it.
    /// </summary>
    [Fact]
    public void AnElementsIdIsTheEntityItIsAboutOrNothing()
    {
        Assert.True(Bind(typeof(global::HoverTooltip), out var access, out var bindingReason),
            bindingReason);

        var recipe = new global::SpellRecipeSO();
        recipe.SetGuid(Guid.Parse("11111111-0000-4000-8000-000000000001"));
        var passiveAsset = new global::PassiveAbilitySO();
        passiveAsset.SetGuid(Guid.Parse("22222222-0000-4000-8000-000000000001"));
        var asset = new global::TooltipableObject();
        asset.SetGuid(Guid.Parse("33333333-0000-4000-8000-000000000001"));

        Assert.True(access.TryReadEntityId(asset, out var assetId, out var assetReason),
            assetReason);
        Assert.Equal(asset.GetGuid(), assetId);

        Assert.True(
            access.TryReadEntityId(new global::Spell(recipe), out var spellId, out var spellReason),
            spellReason);
        Assert.Equal(recipe.GetGuid(), spellId);

        Assert.True(
            access.TryReadEntityId(
                new global::PassiveAbility(passiveAsset), out var passiveId, out var passiveReason),
            passiveReason);
        Assert.Equal(passiveAsset.GetGuid(), passiveId);

        Assert.True(
            access.TryReadEntityId(new FakeTooltip("control"), out var controlId, out var controlReason),
            controlReason);
        Assert.Equal(Guid.Empty, controlId);
    }

    /// <summary>
    /// A live instance whose reference is gone publishes no id rather than a guessed one: the row
    /// keeps its name and path, which is exactly what it had before an id was reachable at all.
    /// </summary>
    [Fact]
    public void AnInstanceWithNoReferenceLeftPublishesNoId()
    {
        Assert.True(Bind(typeof(global::HoverTooltip), out var access, out var bindingReason),
            bindingReason);

        Assert.True(access.TryReadEntityId(new global::Spell(), out var spellId, out var spellReason),
            spellReason);
        Assert.Equal(Guid.Empty, spellId);

        Assert.True(
            access.TryReadEntityId(new global::PassiveAbility(null), out var passiveId, out var passiveReason),
            passiveReason);
        Assert.Equal(Guid.Empty, passiveId);
    }

    [Fact]
    public void TheIdentityReadIsRejectedOffTheUnityThread()
    {
        Assert.True(Bind(typeof(global::HoverTooltip), out var access, out var bindingReason),
            bindingReason);
        var asset = new global::TooltipableObject();

        var accepted = true;
        var refusalReason = string.Empty;
        var thread = new Thread(() =>
        {
            accepted = access.TryReadEntityId(asset, out _, out refusalReason);
        });
        thread.Start();
        thread.Join();

        Assert.False(accepted);
        Assert.Contains("off the Unity startup thread", refusalReason);
    }

    [Fact]
    public void AClosedModalStaysInstantiatedAndItsControlsAreNotOnScreen()
    {
        var modalRoot = new UnityEngine.GameObject("Modal(Clone)");
        var modal = modalRoot.AddComponent<global::UIModal>();
        var button = new UnityEngine.GameObject("SaveButton");
        button.transform.SetParent(modalRoot.transform, false);
        var hover = button.AddComponent<global::HoverTooltip>();

        Assert.True(button.activeInHierarchy);
        Assert.False(GameMcpTooltipNativeAccess.OnScreen(hover));

        modal.OpenForTest();
        Assert.True(GameMcpTooltipNativeAccess.OnScreen(hover));
    }

    [Fact]
    public void PersistentChromeOutsideAnyModalIsOnScreen()
    {
        var bar = new UnityEngine.GameObject("CastingBar");
        var icon = new UnityEngine.GameObject("SpellButton");
        icon.transform.SetParent(bar.transform, false);

        Assert.True(GameMcpTooltipNativeAccess.OnScreen(
            icon.AddComponent<global::HoverTooltip>()));
    }

    private static bool Bind(
        Type hoverTooltipType,
        out GameMcpTooltipNativeAccess access,
        out string reason) =>
        GameMcpTooltipNativeAccess.TryCreate(
            hoverTooltipType,
            typeof(global::Spell),
            typeof(global::PassiveAbility),
            out access,
            out reason);

    private sealed class MissingSubTooltips
    {
    }

    private sealed class WrongElementType
    {
#pragma warning disable CS0414
        private readonly List<string> subTooltips = new();
#pragma warning restore CS0414
    }

    private sealed class FakeTooltip : global::ITooltipable
    {
        internal FakeTooltip(string name) => Name = name;
        private string Name { get; }
        public string GetName() => Name;
        public string GetDisplayType() => "Fixture";
        public UnityEngine.Sprite GetIcon() => new();
        public UnityEngine.Color GetColor() => UnityEngine.Color.white;
        public bool IsColoredIcon() => false;
        public bool HasAltTooltips() => false;
        public string GetDescription() => Name;
        public List<global::TooltipNode> GetTooltipNodes() => new();
        public List<global::TooltipNode> GetAltTooltipNodes() => new();
    }
}
