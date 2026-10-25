using System;
using OrbModding.Common.Runtime.Strategy;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;

namespace OrbModConfig;

/// <summary>Composes the Mods read port with deferred runtime activation and startup admission.</summary>
internal sealed class DeferredStrategistBulletinSource : IStrategistBulletinSource
{
    private readonly Func<IStrategistBulletinSource?> _source;
    private readonly Func<string> _blockingReason;

    internal DeferredStrategistBulletinSource(Func<IStrategistBulletinSource?> source, Func<string> blockingReason)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _blockingReason = blockingReason ?? throw new ArgumentNullException(nameof(blockingReason));
    }

    public string UnavailableReason
    {
        get
        {
            var reason = _blockingReason();
            return string.IsNullOrEmpty(reason)
                ? _source()?.UnavailableReason ?? "The automation runtime is not active."
                : reason;
        }
    }

    public StrategistBulletin? ReadStrategistBulletin() =>
        string.IsNullOrEmpty(_blockingReason()) ? _source()?.ReadStrategistBulletin() : null;
}
