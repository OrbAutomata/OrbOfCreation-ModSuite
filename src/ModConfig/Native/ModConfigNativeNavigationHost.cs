using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace OrbModConfig;

internal sealed class ModConfigNavigationObserver : MonoBehaviour
{
    public Action? Changed { get; set; }
    private void OnTransformChildrenChanged() => Changed?.Invoke();
}

/// <summary>
/// Owns all scene-native navigation state: the cloned Mods button, native view
/// activation, close listeners, hierarchy observation, repair detection, and
/// teardown. The settings panel is supplied only for host-liveness checks.
/// </summary>
internal sealed class ModConfigNativeNavigationHost : IDisposable
{
    private readonly GameObject _buttonObject;
    private readonly Button _button;
    private readonly Image? _buttonImage;
    private readonly Color _buttonBaseColor;
    private readonly Sprite? _inactiveSprite;
    private readonly Sprite? _activeSprite;
    private readonly Type _nativeButtonType;
    private readonly Transform _buttonParent;
    private readonly List<object> _nativeViews;
    private readonly List<(Button Button, UnityAction Listener)> _closeListeners = new();
    private ModConfigNavigationObserver? _observer;
    private UnityAction? _modsListener;
    private Action? _nativeTabSelected;
    private object? _previousNativeView;
    private bool _modsActive;
    private bool _disposed;

    internal ModConfigNativeNavigationHost(
        GameObject buttonObject,
        Button button,
        Image? buttonImage,
        Sprite? inactiveSprite,
        Sprite? activeSprite,
        Type nativeButtonType,
        Transform buttonParent,
        Transform panelParent,
        TextMeshProUGUI labelTemplate,
        List<object> nativeViews)
    {
        _buttonObject = buttonObject;
        _button = button;
        _buttonImage = buttonImage;
        _buttonBaseColor = buttonImage?.color ?? Color.white;
        _inactiveSprite = inactiveSprite;
        _activeSprite = activeSprite;
        _nativeButtonType = nativeButtonType;
        _buttonParent = buttonParent;
        PanelParent = panelParent;
        LabelTemplate = labelTemplate;
        _nativeViews = nativeViews;
    }

    public Transform PanelParent { get; }
    public TextMeshProUGUI LabelTemplate { get; }
    public GameObject ButtonObject => _buttonObject;

    public bool IsAlive => ModConfigNativeNavigationPolicy.HostsAlive(
        !_disposed,
        NativeViewAdapter.IsAlive(_buttonObject) &&
            NativeViewAdapter.IsAlive(_buttonObject.transform.parent) &&
            ReferenceEquals(_buttonObject.transform.parent, _buttonParent),
        panelAlive: true,
        NativeViewAdapter.IsAlive(_buttonParent) && NativeViewAdapter.IsAlive(PanelParent));

    public bool HostsPanel(GameObject panel) =>
        NativeViewAdapter.IsAlive(panel) && NativeViewAdapter.IsAlive(panel.transform.parent) &&
        ReferenceEquals(panel.transform.parent, PanelParent);

    public void Connect(Action modsSelected, Action nativeTabSelected, Action? hierarchyChanged)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(ModConfigNativeNavigationHost));
        _modsListener = () => modsSelected();
        _nativeTabSelected = nativeTabSelected;
        _button.onClick.AddListener(_modsListener);
        foreach (var nativeButton in EnumerateNativeButtons()) BindNativeButton(nativeButton);

        _observer = _buttonParent.gameObject.GetComponent<ModConfigNavigationObserver>() ??
                    _buttonParent.gameObject.AddComponent<ModConfigNavigationObserver>();
        _observer.Changed = hierarchyChanged ?? RefreshNavigation;
    }

    public void ActivateMods()
    {
        PruneNativeReferences();
        _previousNativeView = _nativeViews.FirstOrDefault(NativeViewAdapter.IsActive);
        foreach (var nativeView in _nativeViews) NativeViewAdapter.SetActive(nativeView, false);
        _modsActive = true;
        ApplyButtonStyle(active: true);
    }

    public void DeactivateMods(bool restorePreviousNativeView)
    {
        PruneNativeReferences();
        _modsActive = false;
        ApplyButtonStyle(active: false);
        if (restorePreviousNativeView && NativeViewAdapter.IsAlive(_previousNativeView) &&
            !_nativeViews.Any(NativeViewAdapter.IsActive))
        {
            NativeViewAdapter.SetActive(_previousNativeView!, true);
        }

        _previousNativeView = null;
    }

    public NativeNavigationRecovery RecoverAfterPanelFailure(bool restoreRequested)
    {
        PruneNativeReferences();
        var fallback = _nativeViews.FirstOrDefault(NativeViewAdapter.IsAlive);
        var recovery = ModConfigNativeNavigationPolicy.OpenFailureRecovery(
            restoreRequested,
            NativeViewAdapter.IsAlive(_previousNativeView),
            NativeViewAdapter.IsAlive(fallback),
            _nativeViews.Any(NativeViewAdapter.IsActive));
        var target = recovery.RestorePrevious ? _previousNativeView :
            recovery.RestoreFallback ? fallback : null;
        if (recovery.RestorePrevious || recovery.RestoreFallback) RestoreUsableNativeView(target);
        _modsActive = false;
        _previousNativeView = null;
        ApplyButtonStyle(active: false);
        return recovery;
    }

    public void RefreshNavigation()
    {
        if (_disposed) return;
        PruneNativeReferences();
        if (!IsAlive) return;
        EnsureButtonIsLast();
        foreach (var nativeButton in EnumerateNativeButtons()) BindNativeButton(nativeButton);
    }

#if SERVICE_CYCLE_PROFILE
    internal IReadOnlyList<GameMcpNativeTab> CaptureNativeTabsForGameMcp()
    {
        if (_disposed || !IsAlive) return Array.Empty<GameMcpNativeTab>();
        var result = new List<GameMcpNativeTab>();
        foreach (var component in EnumerateNativeButtons())
        {
            var label = component.GetComponentInChildren<TextMeshProUGUI>(includeInactive: true);
            var view = NativeViewAdapter.ReadView(component);
            result.Add(new GameMcpNativeTab(
                result.Count,
                label?.text?.Trim() ?? string.Empty,
                NativeObjectPath.BuildIndexed(component),
                component,
                NativeViewAdapter.IsAlive(view) && NativeViewAdapter.IsActive(view!)));
        }
        result.Add(new GameMcpNativeTab(
            result.Count,
            _button.GetComponentInChildren<TextMeshProUGUI>(includeInactive: true)?.text?.Trim() ??
                "Mods",
            NativeObjectPath.BuildIndexed(_button),
            _button,
            _modsActive));
        return result;
    }

    internal bool IsNativeTabForGameMcp(Component component)
    {
        if (_disposed || !IsAlive || component is null) return false;
        foreach (var candidate in EnumerateNativeButtons())
            if (ReferenceEquals(candidate, component)) return true;
        return ReferenceEquals(component.gameObject, _buttonObject);
    }

    internal int NativeTabCountForGameMcp()
    {
        if (_disposed || !IsAlive) return 0;
        return EnumerateNativeButtons().Count() + 1;
    }

    internal bool TrySelectNativeTabForGameMcp(int requestedIndex, out string reason)
    {
        if (_disposed || !IsAlive)
        {
            reason = "the native navigation host is not alive";
            return false;
        }
        var index = 0;
        foreach (var component in EnumerateNativeButtons())
        {
            if (index++ != requestedIndex) continue;
            var button = component.GetComponent<Button>();
            if (button is null)
            {
                reason = "native tab " + requestedIndex + " has no Button component";
                return false;
            }
            button.onClick.Invoke();
            reason = string.Empty;
            return true;
        }
        if (requestedIndex == index)
        {
            _button.onClick.Invoke();
            reason = string.Empty;
            return true;
        }
        reason =
            "native tab index " + requestedIndex +
            " is outside the current closed-world navigation rail";
        return false;
    }
#endif

    public void Dispose()
    {
        if (_disposed) return;
        PruneNativeReferences();
        if (_modsActive) DeactivateMods(restorePreviousNativeView: true);
        _disposed = true;
        if (_modsListener is not null) TryRemoveListener(_button, _modsListener);
        ModConfigNativeNavigationPolicy.DetachAll(
            _closeListeners,
            binding => TryRemoveListener(binding.Button, binding.Listener));
        _nativeViews.Clear();
        if (_observer != null)
        {
            _observer.Changed = null;
            UnityEngine.Object.Destroy(_observer);
            _observer = null;
        }

        if (_buttonObject != null) UnityEngine.Object.Destroy(_buttonObject);
    }

    private IEnumerable<Component> EnumerateNativeButtons()
    {
        for (var index = 0; index < _buttonParent.childCount; index++)
        {
            var child = _buttonParent.GetChild(index);
            if (ReferenceEquals(child.gameObject, _buttonObject)) continue;
            if (child.gameObject.GetComponent(_nativeButtonType) is Component component) yield return component;
        }
    }

    private void BindNativeButton(Component component)
    {
        if (!NativeViewAdapter.IsAlive(component)) return;
        var view = NativeViewAdapter.ReadView(component);
        if (NativeViewAdapter.IsAlive(view) && !_nativeViews.Any(existing => ReferenceEquals(existing, view)))
            _nativeViews.Add(view!);
        var button = component.GetComponent<Button>();
        if (button is null || _closeListeners.Any(binding => ReferenceEquals(binding.Button, button))) return;
        UnityAction listener = OnNativeTabSelected;
        button.onClick.AddListener(listener);
        _closeListeners.Add((button, listener));
    }

    private void OnNativeTabSelected()
    {
        PruneNativeReferences();
        EnsureButtonIsLast();
        _nativeTabSelected?.Invoke();
    }

    private void EnsureButtonIsLast()
    {
        if (!IsAlive) return;
        if (_buttonObject.transform.GetSiblingIndex() != _buttonParent.childCount - 1)
            _buttonObject.transform.SetSiblingIndex(_buttonParent.childCount - 1);
    }

    private void PruneNativeReferences()
    {
        ModConfigNativeNavigationPolicy.PruneDead(_nativeViews, NativeViewAdapter.IsAlive);
        ModConfigNativeNavigationPolicy.PruneDead(
            _closeListeners,
            binding => NativeViewAdapter.IsAlive(binding.Button) &&
                       NativeViewAdapter.IsAlive(binding.Button.gameObject),
            binding => TryRemoveListener(binding.Button, binding.Listener));
        if (!NativeViewAdapter.IsAlive(_previousNativeView)) _previousNativeView = null;
    }

    private void RestoreUsableNativeView(object? preferred)
    {
        if (NativeViewAdapter.IsAlive(preferred))
        {
            NativeViewAdapter.SetActive(preferred!, true);
            if (NativeViewAdapter.IsActive(preferred!)) return;
        }

        foreach (var nativeView in _nativeViews)
        {
            if (!NativeViewAdapter.IsAlive(nativeView) || ReferenceEquals(nativeView, preferred)) continue;
            NativeViewAdapter.SetActive(nativeView, true);
            if (NativeViewAdapter.IsActive(nativeView)) return;
        }
    }

    private void ApplyButtonStyle(bool active)
    {
        if (_buttonImage is null) return;
        var sprite = active ? _activeSprite : _inactiveSprite;
        if (sprite is not null)
        {
            _buttonImage.sprite = sprite;
            _buttonImage.color = _buttonBaseColor;
            return;
        }

        _buttonImage.color = active
            ? Color.Lerp(_buttonBaseColor, Color.white, 0.25f)
            : _buttonBaseColor;
    }

    private static void TryRemoveListener(Button? button, UnityAction listener)
    {
        try { if (button != null) button.onClick.RemoveListener(listener); } catch { }
    }
}

#if SERVICE_CYCLE_PROFILE
internal readonly struct GameMcpNativeTab
{
    internal GameMcpNativeTab(
        int index,
        string label,
        string path,
        Component component,
        bool active)
    {
        Index = index;
        Label = label ?? string.Empty;
        Path = path ?? string.Empty;
        Component = component ?? throw new ArgumentNullException(nameof(component));
        Active = active;
    }

    internal int Index { get; }
    internal string Label { get; }
    internal string Path { get; }
    internal Component Component { get; }
    internal bool Active { get; }
}
#endif
