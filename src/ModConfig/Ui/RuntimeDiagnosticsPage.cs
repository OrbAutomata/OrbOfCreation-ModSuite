using System;
using System.Collections.Generic;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Status;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Outcomes;
using OrbModding.Common.Runtime.ServiceCycle.Diagnostics;
using OrbModding.Common.Runtime.Verification;
#if SERVICE_CYCLE_PROFILE
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile.Control;
#endif
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OrbModConfig;

internal sealed class RuntimeDiagnosticsPage : IDisposable
{
    private const string EmptyCardKey = "\0runtime-empty";
    private const string DecisionJournalCardKey = "\0decision-journal";
    private readonly RectTransform _content;
    private readonly ScrollRect _scroll;
    private readonly TextMeshProUGUI _labelTemplate;
    private readonly IDiagnosticsBundleControl _diagnosticsBundle;
    private readonly IDifferentialVerificationControl _differentialVerification;
    private readonly IDecisionJournalStatusSource _decisionJournal;
    private readonly IServiceActionTimelineSource _actionOutcomes;
    private readonly IServiceCyclePumpTimingSource _pumpTiming;
#if SERVICE_CYCLE_PROFILE
    private readonly IPerformanceProfileControl _performanceProfile;
#endif
    private readonly Dictionary<string, RuntimeDiagnosticsCardView> _cards = new(StringComparer.Ordinal);
    private readonly List<string> _staleKeys = new();
    private DiagnosticsBundleControlView? _diagnosticsBundleView;
    private DifferentialVerificationControlView? _differentialVerificationView;
    private ActionOutcomeView? _actionOutcomeView;
    private RuntimeFeatureHealthGridView? _featureHealthView;
#if SERVICE_CYCLE_PROFILE
    private PerformanceProfileControlView? _performanceProfileView;
#endif
    private float _rememberedScrollOffset;
    private int _renderGeneration;
    private long _diagnosticsBundleRevision = -1;
    private long _differentialVerificationRevision = -1;
    private long _decisionJournalRevision = -1;
    private long _actionOutcomeRevision = -1;
#if SERVICE_CYCLE_PROFILE
    private long _performanceProfileRevision = -1;
#endif
    private bool _visible;

    public RuntimeDiagnosticsPage(
        RectTransform content,
        ScrollRect scroll,
        TextMeshProUGUI labelTemplate,
        IDiagnosticsBundleControl diagnosticsBundle,
        IDifferentialVerificationControl differentialVerification,
        IDecisionJournalStatusSource decisionJournal,
        IServiceActionTimelineSource actionOutcomes,
        IServiceCyclePumpTimingSource pumpTiming
#if SERVICE_CYCLE_PROFILE
        , IPerformanceProfileControl performanceProfile
#endif
        )
    {
        _content = content ?? throw new ArgumentNullException(nameof(content));
        _scroll = scroll ?? throw new ArgumentNullException(nameof(scroll));
        _labelTemplate = labelTemplate ?? throw new ArgumentNullException(nameof(labelTemplate));
        _diagnosticsBundle = diagnosticsBundle ?? throw new ArgumentNullException(nameof(diagnosticsBundle));
        _differentialVerification = differentialVerification ??
                                    throw new ArgumentNullException(nameof(differentialVerification));
        _decisionJournal = decisionJournal ?? throw new ArgumentNullException(nameof(decisionJournal));
        _actionOutcomes = actionOutcomes ?? throw new ArgumentNullException(nameof(actionOutcomes));
        _pumpTiming = pumpTiming ?? throw new ArgumentNullException(nameof(pumpTiming));
#if SERVICE_CYCLE_PROFILE
        _performanceProfile = performanceProfile ?? throw new ArgumentNullException(nameof(performanceProfile));
#endif
    }

    public bool ObservabilityChanged => _diagnosticsBundleRevision != _diagnosticsBundle.Revision ||
        _differentialVerificationRevision != _differentialVerification.Revision ||
        _decisionJournalRevision != _decisionJournal.Revision ||
        _actionOutcomeRevision != _actionOutcomes.TimelineRevision
#if SERVICE_CYCLE_PROFILE
        || _performanceProfileRevision != _performanceProfile.Revision
#endif
        ;

    public void Render(RuntimeDiagnosticsDashboard dashboard, bool resetScroll)
    {
        if (dashboard is null) throw new ArgumentNullException(nameof(dashboard));
        var requestedOffset = resetScroll
            ? 0f
            : _visible
                ? Math.Max(0f, _content.anchoredPosition.y)
                : _rememberedScrollOffset;
        _visible = true;
        _renderGeneration = checked(_renderGeneration + 1);

        var top = 4f;
        var siblingIndex = 0;
        _featureHealthView ??= new RuntimeFeatureHealthGridView(_content, _labelTemplate);
        top += _featureHealthView.Layout(
            RuntimeFeatureHealthProjection.Build(dashboard),
            top,
            siblingIndex++);
        _diagnosticsBundleView ??= new DiagnosticsBundleControlView(
            _content,
            _labelTemplate,
            _diagnosticsBundle);
        top += _diagnosticsBundleView.Layout(_content.rect.width, top, siblingIndex++);
        _diagnosticsBundleRevision = _diagnosticsBundle.Revision;
        _differentialVerificationView ??= new DifferentialVerificationControlView(
            _content,
            _labelTemplate,
            _differentialVerification);
        top += _differentialVerificationView.Layout(_content.rect.width, top, siblingIndex++);
        _differentialVerificationRevision = _differentialVerification.Revision;
#if SERVICE_CYCLE_PROFILE
        _performanceProfileView ??= new PerformanceProfileControlView(
            _content,
            _labelTemplate,
            _performanceProfile);
        top += _performanceProfileView.Layout(_content.rect.width, top, siblingIndex++);
        _performanceProfileRevision = _performanceProfile.Revision;
#endif
        _actionOutcomeView ??= new ActionOutcomeView(
            _content,
            _labelTemplate,
            _actionOutcomes,
            _pumpTiming);
        top += _actionOutcomeView.Layout(_content.rect.width, top, siblingIndex++);
        _actionOutcomeRevision = _actionOutcomes.TimelineRevision;
        var journalView = GetOrCreate(DecisionJournalCardKey);
        top += journalView.LayoutStatic(
            "Decision journal",
            DecisionJournalPresenter.Build(_decisionJournal.Status),
            _content.rect.width,
            top,
            siblingIndex++,
            _renderGeneration);
        _decisionJournalRevision = _decisionJournal.Revision;
        if (dashboard.Cards.Count == 0)
        {
            var view = GetOrCreate(EmptyCardKey);
            top += view.LayoutStatic(
                "Runtime diagnostics",
                "No loaded plugin or runtime service is currently reporting diagnostics.",
                _content.rect.width,
                top,
                siblingIndex,
                _renderGeneration);
        }
        else
        {
            foreach (var card in dashboard.Cards)
            {
                var view = GetOrCreate(card.PluginGuid);
                top += view.LayoutCard(
                    card,
                    _content.rect.width,
                    top,
                    siblingIndex++,
                    _renderGeneration);
            }
        }
        RemoveStaleCards();

        var contentHeight = Math.Max(1f, top);
        _content.sizeDelta = new Vector2(0f, contentHeight);
        var viewportHeight = _content.parent is RectTransform viewport ? viewport.rect.height : 0f;
        var restoredOffset = ModSettingsLayout.ClampScrollOffset(
            requestedOffset,
            contentHeight,
            viewportHeight);
        _content.anchoredPosition = new Vector2(_content.anchoredPosition.x, restoredOffset);
        _scroll.verticalNormalizedPosition = ModSettingsLayout.CalculateVerticalNormalizedPosition(
            restoredOffset,
            contentHeight,
            viewportHeight);
    }

    public void Hide()
    {
        if (!_visible) return;
        _rememberedScrollOffset = Math.Max(0f, _content.anchoredPosition.y);
        _visible = false;
        Clear();
    }

    public void RefreshActivity() => _actionOutcomeView?.Refresh();

    public void Clear()
    {
        _diagnosticsBundleView?.Dispose();
        _diagnosticsBundleView = null;
        _differentialVerificationView?.Dispose();
        _differentialVerificationView = null;
        _actionOutcomeView?.Dispose();
        _actionOutcomeView = null;
        _featureHealthView?.Dispose();
        _featureHealthView = null;
#if SERVICE_CYCLE_PROFILE
        _performanceProfileView?.Dispose();
        _performanceProfileView = null;
#endif
        foreach (var view in _cards.Values) view.Dispose();
        _cards.Clear();
        _staleKeys.Clear();
    }

    public void Dispose()
    {
        _visible = false;
        Clear();
    }

    private RuntimeDiagnosticsCardView GetOrCreate(string key)
    {
        if (_cards.TryGetValue(key, out var view)) return view;
        view = new RuntimeDiagnosticsCardView(_content, _labelTemplate);
        _cards.Add(key, view);
        return view;
    }

    private void RemoveStaleCards()
    {
        _staleKeys.Clear();
        foreach (var pair in _cards)
        {
            if (pair.Value.RenderGeneration != _renderGeneration) _staleKeys.Add(pair.Key);
        }
        foreach (var key in _staleKeys)
        {
            _cards[key].Dispose();
            _cards.Remove(key);
        }
        _staleKeys.Clear();
    }
}
