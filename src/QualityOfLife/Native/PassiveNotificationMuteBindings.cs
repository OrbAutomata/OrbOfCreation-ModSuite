using System;
using OrbModding.Common;

namespace OrbAutomata;

internal sealed class PassiveNotificationMuteBindings
{
    internal Type PassiveType { get; private init; } = null!;
    internal Func<object, Guid> Guid { get; private init; } = null!;
    internal Func<object, bool> Muted { get; private init; } = null!;
    internal Action<object, bool> SetMuted { get; private init; } = null!;
    internal Func<object, bool> Touched { get; private init; } = null!;
    internal Func<object, bool> Hidden { get; private init; } = null!;
    internal Func<object, bool> Silent { get; private init; } = null!;

    internal static bool TryCreate(out PassiveNotificationMuteBindings? bindings, out string reason,
        Func<string, Type?>? resolveType = null, Func<string, bool>? includeContract = null) =>
        NativeBindingSet.TryBind("passive notification mute", QualityOfLifeNativeContracts.ContractIds,
            resolveType, includeContract, set =>
            {
                var passive = set.Type(QualityOfLifeNativeContracts.PassiveType, "PassiveAbilitySO");
                return new PassiveNotificationMuteBindings
                {
                    PassiveType = passive,
                    Guid = set.StableUuid(QualityOfLifeNativeContracts.StableGuid, passive),
                    Muted = set.Field<Func<object, bool>>(QualityOfLifeNativeContracts.Muted, passive, "muted", typeof(bool)),
                    SetMuted = set.Setter<Action<object, bool>>(QualityOfLifeNativeContracts.Muted, passive, "muted", typeof(bool)),
                    Touched = set.Field<Func<object, bool>>(QualityOfLifeNativeContracts.Touched, passive, "touched", typeof(bool)),
                    Hidden = set.Field<Func<object, bool>>(QualityOfLifeNativeContracts.Hidden, passive, "hidden", typeof(bool)),
                    Silent = set.Field<Func<object, bool>>(QualityOfLifeNativeContracts.Silent, passive, "silent", typeof(bool)),
                };
            }, out bindings, out reason);
}
