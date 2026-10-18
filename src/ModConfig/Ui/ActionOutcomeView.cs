using System;
using System.Collections.Generic;
using OrbModding.Common.Runtime.ServiceCycle.Diagnostics;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Outcomes;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OrbModConfig;

internal sealed class ActionOutcomeView : IDisposable
{
    private const float HeaderHeight = 38f;
    private const float QuietHeight = 46f;
    private const float PlotHeight = 148f;
    private const float LegendRowHeight = 24f;
    private const int LegendColumns = 4;
    private const float DetailHeight = 102f;
    private const float DetailHeaderHeight = 22f;
    private const float DetailRowHeight = 20f;
    private const int DetailColumns = 2;
    private const float TimingHeight = 34f;
    private const float BottomGap = 7f;
    private readonly IServiceActionTimelineSource _outcomes;
    private readonly IServiceCyclePumpTimingSource _timings;
    private readonly TextMeshProUGUI _template;
    private readonly GameObject _root;
    private readonly RectTransform _rect;
    private readonly GameObject _plot;
    private readonly ActionOutcomeTimelineGraphic _graphic;
    private readonly GameObject _selectorRoot;
    private readonly TextMeshProUGUI _axisTop;
    private readonly TextMeshProUGUI _detailHeader;
    private readonly TextMeshProUGUI _detailEmpty;
    private readonly GameObject _detailRoot;
    private readonly TextMeshProUGUI _quiet;
    private readonly TextMeshProUGUI _timing;
    private readonly List<LegendEntry> _legend = new();
    private readonly List<DetailEntry> _details = new();
    private readonly List<GameObject> _bucketSelectors = new();
    private ServiceActionTimelineCellSnapshot[] _timelineBuffer;
    private ServiceCyclePumpTimingSample[] _timingBuffer;
    private long _timelineRevision = -1;
    private long _selectedMinuteKey = long.MinValue;
    private int _selectedBucket = -1;
    private ActionOutcomeSurfacePresentation _presentation;

    internal ActionOutcomeView(
        RectTransform parent,
        TextMeshProUGUI template,
        IServiceActionTimelineSource outcomes,
        IServiceCyclePumpTimingSource timings)
    {
        _template = template ?? throw new ArgumentNullException(nameof(template));
        _outcomes = outcomes ?? throw new ArgumentNullException(nameof(outcomes));
        _timings = timings ?? throw new ArgumentNullException(nameof(timings));
        if (timings.Capacity <= 0)
            throw new ArgumentException("Pump timing source capacity must be positive.", nameof(timings));
        _timelineBuffer = new ServiceActionTimelineCellSnapshot[
            Math.Max(1, outcomes.TimelineCellCapacity)];
        _timingBuffer = new ServiceCyclePumpTimingSample[timings.Capacity];
        _root = ModConfigUiFactory.CreateRectObject(
            "ActionTimelinePanel",
            parent,
            new Vector2(0.01f, 1f),
            new Vector2(0.99f, 1f),
            ModConfigPalette.Row);
        _rect = (RectTransform)_root.transform;
        _rect.pivot = new Vector2(0.5f, 1f);
        ModConfigNativeRailFactory.SkinPanel(
            _root.GetComponent<UnityEngine.UI.Image>()!,
            ModConfigUiFactory.NativeVisuals.FeatureRailBaseFrame,
            ModConfigPalette.Row);
        var title = ModConfigUiFactory.CreateText(
            "Title",
            _root.transform,
            new Vector2(0.02f, 1f),
            new Vector2(0.98f, 1f),
            template,
            ActionOutcomeSurfacePresentation.Title,
            TextAlignmentOptions.TopLeft,
            0.78f);
        ModConfigUiFactory.SetTopAnchoredHeight((RectTransform)title.transform, 8f, 28f);

        _quiet = ModConfigUiFactory.CreateText(
            "QuietWindow",
            _root.transform,
            new Vector2(0.025f, 1f),
            new Vector2(0.975f, 1f),
            template,
            ActionOutcomeSurfacePresentation.QuietWindow,
            TextAlignmentOptions.MidlineLeft,
            0.54f,
            TextOverflowModes.Overflow);
        _quiet.color = new Color(template.color.r, template.color.g, template.color.b, 0.72f);

        _plot = ModConfigUiFactory.CreateRectObject(
            "MinuteBuckets",
            _root.transform,
            new Vector2(0.02f, 1f),
            new Vector2(0.98f, 1f),
            ModConfigPalette.Background);
        var timeline = ModConfigUiFactory.CreateRectObject(
            "CommittedActions",
            _plot.transform,
            new Vector2(0.055f, 0.06f),
            new Vector2(0.995f, 0.80f));
        _graphic = timeline.AddComponent<ActionOutcomeTimelineGraphic>();
        _graphic.raycastTarget = false;

        var axisLabel = ModConfigUiFactory.CreateText(
            "ScaleLabel",
            _plot.transform,
            new Vector2(0.058f, 0.81f),
            new Vector2(0.50f, 0.99f),
            template,
            ActionOutcomeSurfacePresentation.AxisLabel,
            TextAlignmentOptions.MidlineLeft,
            0.43f,
            TextOverflowModes.Overflow);
        axisLabel.color = new Color(template.color.r, template.color.g, template.color.b, 0.68f);
        _axisTop = ModConfigUiFactory.CreateText(
            "ScaleMaximum",
            _plot.transform,
            new Vector2(0.005f, 0.68f),
            new Vector2(0.052f, 0.84f),
            template,
            "0",
            TextAlignmentOptions.MidlineLeft,
            0.42f,
            TextOverflowModes.Overflow);
        _axisTop.color = axisLabel.color;
        var axisZero = ModConfigUiFactory.CreateText(
            "ScaleZero",
            _plot.transform,
            new Vector2(0.005f, 0.03f),
            new Vector2(0.052f, 0.19f),
            template,
            "0",
            TextAlignmentOptions.MidlineLeft,
            0.42f,
            TextOverflowModes.Overflow);
        axisZero.color = axisLabel.color;

        _selectorRoot = ModConfigUiFactory.CreateRectObject(
            "MinuteSelectors",
            _plot.transform,
            new Vector2(0.055f, 0.06f),
            new Vector2(0.995f, 0.80f),
            new Color(0f, 0f, 0f, 0f));
        _selectorRoot.GetComponent<Image>()!.raycastTarget = false;

        _detailRoot = ModConfigUiFactory.CreateRectObject(
            "SelectedMinute",
            _root.transform,
            new Vector2(0.02f, 1f),
            new Vector2(0.98f, 1f),
            new Color(0f, 0f, 0f, 0f));
        _detailRoot.GetComponent<Image>()!.raycastTarget = false;
        _detailHeader = ModConfigUiFactory.CreateText(
            "MinuteLabel",
            _detailRoot.transform,
            new Vector2(0.01f, 1f),
            new Vector2(0.99f, 1f),
            template,
            string.Empty,
            TextAlignmentOptions.TopLeft,
            0.49f,
            TextOverflowModes.Overflow);
        ModConfigUiFactory.SetTopAnchoredHeight(
            (RectTransform)_detailHeader.transform,
            0f,
            DetailHeaderHeight);
        _detailHeader.color = new Color(template.color.r, template.color.g, template.color.b, 0.78f);
        _detailEmpty = ModConfigUiFactory.CreateText(
            "EmptyMinute",
            _detailRoot.transform,
            new Vector2(0.01f, 1f),
            new Vector2(0.99f, 1f),
            template,
            ActionOutcomeSurfacePresentation.EmptyMinute,
            TextAlignmentOptions.TopLeft,
            0.46f,
            TextOverflowModes.Overflow);
        ModConfigUiFactory.SetTopAnchoredHeight(
            (RectTransform)_detailEmpty.transform,
            DetailHeaderHeight,
            DetailRowHeight);
        _detailEmpty.color = new Color(template.color.r, template.color.g, template.color.b, 0.62f);

        _timing = ModConfigUiFactory.CreateText(
            "TimingSummary",
            _root.transform,
            new Vector2(0.025f, 1f),
            new Vector2(0.975f, 1f),
            template,
            ActionOutcomeSurfacePresentation.EmptyTiming,
            TextAlignmentOptions.MidlineLeft,
            0.48f,
            TextOverflowModes.Overflow);
        _timing.color = new Color(template.color.r, template.color.g, template.color.b, 0.72f);
        Refresh();
    }

    internal float Layout(float contentWidth, float topOffset, int siblingIndex)
    {
        _ = contentWidth;
        Refresh();
        var legendRows = _presentation.ShowsTimeline
            ? (_presentation.Legend.Length + LegendColumns - 1) / LegendColumns
            : 0;
        var activityHeight = _presentation.ShowsTimeline
            ? PlotHeight + legendRows * LegendRowHeight + DetailHeight
            : QuietHeight;
        var height = HeaderHeight + activityHeight + TimingHeight + BottomGap;
        _rect.anchoredPosition = new Vector2(0f, -topOffset);
        _rect.sizeDelta = new Vector2(0f, height);
        _root.transform.SetSiblingIndex(siblingIndex);
        ModConfigUiFactory.SetTopAnchoredHeight(
            (RectTransform)_quiet.transform,
            HeaderHeight,
            QuietHeight);
        ModConfigUiFactory.SetTopAnchoredHeight(
            (RectTransform)_plot.transform,
            HeaderHeight,
            PlotHeight - 6f);
        LayoutLegend(HeaderHeight + PlotHeight - 2f);
        ModConfigUiFactory.SetTopAnchoredHeight(
            (RectTransform)_detailRoot.transform,
            HeaderHeight + PlotHeight + legendRows * LegendRowHeight,
            DetailHeight);
        ModConfigUiFactory.SetTopAnchoredHeight(
            (RectTransform)_timing.transform,
            height - TimingHeight - BottomGap,
            TimingHeight);
        return height + 7f;
    }

    internal void Refresh()
    {
        EnsureCapacity();
        if (_timelineRevision == _outcomes.TimelineRevision) return;
        var timeline = _outcomes.CopyTimelineTo(_timelineBuffer);
        if (!timeline.IsComplete)
        {
            _timelineBuffer = new ServiceActionTimelineCellSnapshot[timeline.AvailableCount];
            timeline = _outcomes.CopyTimelineTo(_timelineBuffer);
            if (!timeline.IsComplete)
                throw new InvalidOperationException("The action timeline changed while it was being copied.");
        }
        var timings = _timings.CopyTo(_timingBuffer);
        _timelineRevision = timeline.Revision;
        _presentation = ActionOutcomeSurfacePresentation.Build(
            _timelineBuffer.AsSpan(0, timeline.WrittenCount),
            timeline.ServiceCount,
            timeline.BucketCount,
            _timingBuffer.AsSpan(0, timings.WrittenCount));
        _plot.SetActive(_presentation.ShowsTimeline);
        _detailRoot.SetActive(_presentation.ShowsTimeline);
        _quiet.gameObject.SetActive(!_presentation.ShowsTimeline);
        _quiet.text = _presentation.QuietMessage;
        _axisTop.text = _presentation.MaximumCommitted.ToString();
        _graphic.SetTimeline(_presentation.Buckets, _presentation.MaximumCommitted);
        EnsureBucketSelectors(_presentation.Buckets.Length);
        while (_legend.Count < _presentation.Legend.Length)
            _legend.Add(new LegendEntry(_rect, _template));
        for (var index = 0; index < _legend.Count; index++)
        {
            var active = _presentation.ShowsTimeline && index < _presentation.Legend.Length;
            _legend[index].SetActive(active);
            if (active) _legend[index].Render(_presentation.Legend[index]);
        }
        ResolveSelectedBucket();
        RenderSelectedBucket();
        _timing.text = _presentation.TimingSummary;
    }

    public void Dispose()
    {
        foreach (var entry in _legend) entry.Dispose();
        _legend.Clear();
        foreach (var entry in _details) entry.Dispose();
        _details.Clear();
        foreach (var selector in _bucketSelectors)
            selector.GetComponent<Button>()?.onClick.RemoveAllListeners();
        _bucketSelectors.Clear();
        UnityEngine.Object.Destroy(_root);
    }

    private void EnsureCapacity()
    {
        if (_timelineBuffer.Length < _outcomes.TimelineCellCapacity)
            _timelineBuffer = new ServiceActionTimelineCellSnapshot[_outcomes.TimelineCellCapacity];
        if (_timingBuffer.Length < _timings.Capacity)
            _timingBuffer = new ServiceCyclePumpTimingSample[_timings.Capacity];
    }

    private void LayoutLegend(float top)
    {
        for (var index = 0; index < _presentation.Legend.Length; index++)
        {
            var column = index % LegendColumns;
            var row = index / LegendColumns;
            _legend[index].Layout(column, row, top);
        }
    }

    private void EnsureBucketSelectors(int count)
    {
        if (_bucketSelectors.Count == count) return;
        foreach (var selector in _bucketSelectors) UnityEngine.Object.Destroy(selector);
        _bucketSelectors.Clear();
        if (count <= 0) return;
        for (var index = 0; index < count; index++)
        {
            var left = index / (float)count;
            var right = (index + 1f) / count;
            var selector = ModConfigUiFactory.CreateRectObject(
                "Minute." + index,
                _selectorRoot.transform,
                new Vector2(left, 0f),
                new Vector2(right, 1f),
                new Color(0f, 0f, 0f, 0f));
            selector.GetComponent<Image>()!.raycastTarget = true;
            var button = selector.AddComponent<Button>();
            var captured = index;
            button.onClick.AddListener(() => SelectBucket(captured));
            _bucketSelectors.Add(selector);
        }
    }

    private void ResolveSelectedBucket()
    {
        _selectedBucket = -1;
        for (var index = 0; index < _presentation.Buckets.Length; index++)
        {
            if (_presentation.Buckets[index].MinuteKey != _selectedMinuteKey) continue;
            _selectedBucket = index;
            break;
        }
        if (_selectedBucket >= 0) return;
        for (var index = _presentation.Buckets.Length - 1; index >= 0; index--)
        {
            var bucket = _presentation.Buckets[index];
            if (bucket.Details.Length == 0 && bucket.Committed == 0 && !bucket.HasFault) continue;
            _selectedBucket = index;
            _selectedMinuteKey = bucket.MinuteKey;
            return;
        }
    }

    private void SelectBucket(int bucketIndex)
    {
        if (bucketIndex < 0 || bucketIndex >= _presentation.Buckets.Length) return;
        _selectedBucket = bucketIndex;
        _selectedMinuteKey = _presentation.Buckets[bucketIndex].MinuteKey;
        RenderSelectedBucket();
    }

    private void RenderSelectedBucket()
    {
        _graphic.SetSelectedBucket(_selectedBucket);
        if (_selectedBucket < 0 || _selectedBucket >= _presentation.Buckets.Length)
        {
            _detailHeader.text = string.Empty;
            _detailEmpty.gameObject.SetActive(false);
            SetDetails(Array.Empty<ActionOutcomeServiceDetailPresentation>());
            return;
        }
        var minutesAgo = _presentation.Buckets.Length - 1 - _selectedBucket;
        _detailHeader.text = minutesAgo switch
        {
            0 => "This minute",
            1 => "1 minute ago",
            _ => minutesAgo + " minutes ago",
        };
        var details = _presentation.Buckets[_selectedBucket].Details;
        _detailEmpty.gameObject.SetActive(details.Length == 0);
        SetDetails(details);
    }

    private void SetDetails(ActionOutcomeServiceDetailPresentation[] details)
    {
        while (_details.Count < details.Length)
            _details.Add(new DetailEntry((RectTransform)_detailRoot.transform, _template));
        for (var index = 0; index < _details.Count; index++)
        {
            var active = index < details.Length;
            _details[index].SetActive(active);
            if (!active) continue;
            _details[index].Render(details[index]);
            _details[index].Layout(index % DetailColumns, index / DetailColumns);
        }
    }

    private sealed class LegendEntry : IDisposable
    {
        private readonly GameObject _root;
        private readonly RectTransform _rect;
        private readonly UnityEngine.UI.Image _swatch;
        private readonly TextMeshProUGUI _label;

        internal LegendEntry(RectTransform parent, TextMeshProUGUI template)
        {
            _root = ModConfigUiFactory.CreateRectObject(
                "ServiceLegend",
                parent,
                Vector2.zero,
                Vector2.zero);
            _rect = (RectTransform)_root.transform;
            _rect.pivot = new Vector2(0.5f, 1f);
            var swatch = ModConfigUiFactory.CreateRectObject(
                "Color",
                _root.transform,
                new Vector2(0.02f, 0.28f),
                new Vector2(0.08f, 0.72f),
                Color.white);
            _swatch = swatch.GetComponent<UnityEngine.UI.Image>()!;
            _label = ModConfigUiFactory.CreateText(
                "Service",
                _root.transform,
                new Vector2(0.105f, 0f),
                new Vector2(0.99f, 1f),
                template,
                string.Empty,
                TextAlignmentOptions.MidlineLeft,
                0.46f,
                TextOverflowModes.Ellipsis);
        }

        internal void SetActive(bool active) => _root.SetActive(active);

        internal void Render(ActionOutcomeLegendPresentation legend)
        {
            _swatch.color = ActionOutcomeTimelineGraphic.ColorFor(legend.Color);
            _label.text = legend.DisplayName;
        }

        internal void Layout(int column, int row, float top)
        {
            var left = 0.02f + column * 0.245f;
            _rect.anchorMin = new Vector2(left, 1f);
            _rect.anchorMax = new Vector2(left + 0.235f, 1f);
            _rect.anchoredPosition = new Vector2(0f, -top - row * LegendRowHeight);
            _rect.sizeDelta = new Vector2(0f, LegendRowHeight);
        }

        public void Dispose() => UnityEngine.Object.Destroy(_root);
    }

    private sealed class DetailEntry : IDisposable
    {
        private readonly GameObject _root;
        private readonly RectTransform _rect;
        private readonly Image _swatch;
        private readonly TextMeshProUGUI _label;

        internal DetailEntry(RectTransform parent, TextMeshProUGUI template)
        {
            _root = ModConfigUiFactory.CreateRectObject(
                "ServiceOutcome",
                parent,
                Vector2.zero,
                Vector2.zero,
                new Color(0f, 0f, 0f, 0f));
            _root.GetComponent<Image>()!.raycastTarget = false;
            _rect = (RectTransform)_root.transform;
            _rect.pivot = new Vector2(0.5f, 1f);
            var swatch = ModConfigUiFactory.CreateRectObject(
                "Color",
                _root.transform,
                new Vector2(0.01f, 0.28f),
                new Vector2(0.035f, 0.72f),
                Color.white);
            _swatch = swatch.GetComponent<Image>()!;
            _swatch.raycastTarget = false;
            _label = ModConfigUiFactory.CreateText(
                "Outcome",
                _root.transform,
                new Vector2(0.05f, 0f),
                new Vector2(0.99f, 1f),
                template,
                string.Empty,
                TextAlignmentOptions.MidlineLeft,
                0.43f,
                TextOverflowModes.Ellipsis);
        }

        internal void SetActive(bool active) => _root.SetActive(active);

        internal void Render(ActionOutcomeServiceDetailPresentation detail)
        {
            _swatch.color = ActionOutcomeTimelineGraphic.ColorFor(detail.Color);
            _label.text = detail.Summary;
        }

        internal void Layout(int column, int row)
        {
            var left = 0.01f + column * 0.495f;
            _rect.anchorMin = new Vector2(left, 1f);
            _rect.anchorMax = new Vector2(left + 0.485f, 1f);
            _rect.anchoredPosition = new Vector2(
                0f,
                -DetailHeaderHeight - row * DetailRowHeight);
            _rect.sizeDelta = new Vector2(0f, DetailRowHeight);
        }

        public void Dispose() => UnityEngine.Object.Destroy(_root);
    }
}
