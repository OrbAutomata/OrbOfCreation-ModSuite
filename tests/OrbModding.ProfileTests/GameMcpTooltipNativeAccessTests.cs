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
    /// A decorated tooltip is still about the thing inside it. The glyph list hands the screen an
    /// <c>OverwriteTooltip</c> around its own <c>GlyphSO</c>, and because the wrapper forwards
    /// <c>GetName</c> those rows arrived with a name and no id — so a glyph icon was the one
    /// entity-drawing element on the loadout page that had to be read by path.
    /// </summary>
    [Fact]
    public void AWrappedTooltipIsStillAboutTheEntityInsideIt()
    {
        Assert.True(Bind(typeof(global::HoverTooltip), out var access, out var bindingReason),
            bindingReason);
        var glyph = new global::GlyphSO();
        glyph.SetGuid(Guid.Parse("44444444-0000-4000-8000-000000000001"));

        Assert.True(
            access.TryReadEntityId(
                new global::OverwriteTooltip(glyph), out var wrapped, out var wrappedReason),
            wrappedReason);
        Assert.Equal(glyph.GetGuid(), wrapped);

        Assert.True(
            access.TryReadEntityId(
                new global::OverwriteTooltip(new global::OverwriteTooltip(glyph)),
                out var twice,
                out var twiceReason),
            twiceReason);
        Assert.Equal(glyph.GetGuid(), twice);

        Assert.True(
            access.TryReadEntityId(
                new global::OverwriteTooltip(new FakeTooltip("chrome")),
                out var chrome,
                out var chromeReason),
            chromeReason);
        Assert.Equal(Guid.Empty, chrome);
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

    /// <summary>
    /// Round 15 read Time's subtab strip on Magic, Magic's on Time, and Time-screen tooltip nodes
    /// on Scholar. Leaving a screen does not deactivate it — its objects stay alive in the
    /// hierarchy — so "alive" was answering a question it cannot answer. The game's own group says
    /// which screen is being drawn, and both captures ask it.
    /// </summary>
    [Fact]
    public void TheScreenThePlayerLeftIsStillAliveAndIsNoLongerOnScreen()
    {
        var scene = TwoScreens();

        Assert.True(scene.OnTime.gameObject.activeInHierarchy);
        Assert.True(scene.OnMagic.gameObject.activeInHierarchy);
        Assert.True(GameMcpTooltipNativeAccess.OnScreen(scene.OnMagic));
        Assert.False(GameMcpTooltipNativeAccess.OnScreen(scene.OnTime));

        scene.Magic.SetVisibleForTest(false);
        scene.Time.SetVisibleForTest(true);

        Assert.False(GameMcpTooltipNativeAccess.OnScreen(scene.OnMagic));
        Assert.True(GameMcpTooltipNativeAccess.OnScreen(scene.OnTime));
    }

    /// <summary>
    /// A group answers for its whole subtree, however deep, and a panel switched off inside the
    /// screen the player is on is off screen with it.
    /// </summary>
    [Fact]
    public void AGroupSwitchedOffInsideTheCurrentScreenTakesItsSubtreeWithIt()
    {
        var scene = TwoScreens();
        var panel = new UnityEngine.GameObject("CollapsedPanel");
        panel.transform.SetParent(scene.OnMagic.transform, false);
        var panelGroup = panel.AddComponent<global::UIRenderGroup>();
        panelGroup.BindForTest(null, scene.MagicGroup);
        var inside = new UnityEngine.GameObject("PanelButton");
        inside.transform.SetParent(panel.transform, false);
        var hover = inside.AddComponent<global::HoverTooltip>();

        Assert.True(GameMcpTooltipNativeAccess.OnScreen(hover));

        panelGroup.SetEnabled(false);

        Assert.False(GameMcpTooltipNativeAccess.OnScreen(hover));
        Assert.True(GameMcpTooltipNativeAccess.OnScreen(scene.OnMagic));
    }

    /// <summary>
    /// The screen's group is the outer gate: a panel whose own group is perfectly fine is still
    /// off screen once the screen around it is gone.
    /// </summary>
    [Fact]
    public void AHealthyPanelOnADepartedScreenIsOffScreenWithIt()
    {
        var scene = TwoScreens();
        var panel = new UnityEngine.GameObject("TimePanel");
        panel.transform.SetParent(scene.OnTime.transform, false);
        var panelGroup = panel.AddComponent<global::UIRenderGroup>();
        panelGroup.BindForTest(null, scene.TimeGroup);
        var inside = new UnityEngine.GameObject("TimeButton");
        inside.transform.SetParent(panel.transform, false);

        Assert.True(panelGroup.IsManagedViewActive());
        Assert.False(GameMcpTooltipNativeAccess.OnScreen(
            inside.AddComponent<global::HoverTooltip>()));
    }

    private readonly struct StubScene
    {
        internal StubScene(
            global::ManagedView magic,
            global::ManagedView time,
            global::UIRenderGroup magicGroup,
            global::UIRenderGroup timeGroup,
            global::HoverTooltip onMagic,
            global::HoverTooltip onTime)
        {
            Magic = magic;
            Time = time;
            MagicGroup = magicGroup;
            TimeGroup = timeGroup;
            OnMagic = onMagic;
            OnTime = onTime;
        }

        internal global::ManagedView Magic { get; }
        internal global::ManagedView Time { get; }
        internal global::UIRenderGroup MagicGroup { get; }
        internal global::UIRenderGroup TimeGroup { get; }
        internal global::HoverTooltip OnMagic { get; }
        internal global::HoverTooltip OnTime { get; }
    }

    /// <summary>
    /// Two screens under one canvas, both alive, one drawn — the shape the game actually leaves
    /// behind when the player moves from Magic to Time and back.
    /// </summary>
    private static StubScene TwoScreens()
    {
        var canvas = new UnityEngine.GameObject("Canvas");
        var magic = Screen(canvas, "ScreenMagic", visible: true);
        var time = Screen(canvas, "ScreenTime", visible: false);
        return new StubScene(
            magic.View, time.View, magic.Group, time.Group, magic.Strip, time.Strip);
    }

    private static (global::ManagedView View, global::UIRenderGroup Group, global::HoverTooltip Strip)
        Screen(UnityEngine.GameObject canvas, string name, bool visible)
    {
        var root = new UnityEngine.GameObject(name);
        root.transform.SetParent(canvas.transform, false);
        var view = root.AddComponent<global::ManagedView>();
        view.SetVisibleForTest(visible);
        var group = root.AddComponent<global::UIRenderGroup>();
        group.BindForTest(view, null);
        var strip = new UnityEngine.GameObject(name + "/SubviewRadio");
        strip.transform.SetParent(root.transform, false);
        return (view, group, strip.AddComponent<global::HoverTooltip>());
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
