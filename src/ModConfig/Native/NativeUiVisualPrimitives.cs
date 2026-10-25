using UnityEngine;

namespace OrbModConfig;

/// <summary>
/// Scene-bound static frame sprites sampled from exact native UI contracts.
/// </summary>
/// <remarks>
/// The sampler does not synthesize substitutes. A caller that cannot obtain this complete snapshot
/// must report the audited surface as unavailable and try again after the next scene rebuild.
/// </remarks>
internal sealed record NativeButtonStateVisualPrimitives(
    Sprite InactiveFrame,
    Sprite ActiveFrame);

internal sealed record NativeFeatureRailVisualPrimitives(
    Sprite FeatureRailBaseFrame,
    Sprite FeatureRailActiveFrame,
    Sprite RuntimeIcon,
    Sprite GeneralIcon,
    Sprite ConceptIcon,
    Sprite AdvancedIcon,
    Sprite WorldIcon,
    Sprite WorkshopIcon);
