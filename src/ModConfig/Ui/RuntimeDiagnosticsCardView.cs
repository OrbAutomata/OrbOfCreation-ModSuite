using System;
using TMPro;
using UnityEngine;

namespace OrbModConfig;

internal sealed class RuntimeDiagnosticsCardView : IDisposable
{
    private readonly ModConfigTextCardView _view;
    private RuntimeDiagnosticsCard? _card;
    private long _cardRevision;
    private string _title = string.Empty;
    private string _body = string.Empty;

    public RuntimeDiagnosticsCardView(RectTransform parent, TextMeshProUGUI template) =>
        _view = new ModConfigTextCardView(parent, template);

    public int RenderGeneration => _view.RenderGeneration;

    public float LayoutCard(RuntimeDiagnosticsCard card, float contentWidth, float topOffset,
        int siblingIndex, int renderGeneration)
    {
        if (!ReferenceEquals(_card, card) || _cardRevision != card.Revision)
        {
            _card = card;
            _cardRevision = card.Revision;
            _title = RuntimeDiagnosticsCardText.Title(card);
            _body = RuntimeDiagnosticsCardText.Body(card);
        }
        var color = card.Severity switch
        {
            RuntimeDiagnosticsSeverity.Failure => new Color(0.32f, 0.10f, 0.10f, 0.98f),
            RuntimeDiagnosticsSeverity.Attention => new Color(0.28f, 0.20f, 0.08f, 0.98f),
            RuntimeDiagnosticsSeverity.Waiting => new Color(0.10f, 0.15f, 0.22f, 0.98f),
            _ => ModConfigPalette.Row,
        };
        return _view.Layout(_title, _body, color, contentWidth, topOffset, siblingIndex, renderGeneration);
    }

    public float LayoutStatic(string title, string body, float contentWidth, float topOffset,
        int siblingIndex, int renderGeneration)
    {
        _card = null;
        _cardRevision = 0;
        return _view.Layout(title, body, ModConfigPalette.Row,
            contentWidth, topOffset, siblingIndex, renderGeneration);
    }

    public void Dispose() => _view.Dispose();
}
