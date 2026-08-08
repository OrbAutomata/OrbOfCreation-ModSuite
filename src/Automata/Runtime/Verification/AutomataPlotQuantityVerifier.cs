using System;
using System.Reflection;
using OrbModding.Common.Runtime.GameMath;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata;

/// <summary>
/// Compares the two plot-node quantity ports against the game's own answers, for real nodes in a
/// live session.
/// </summary>
/// <remarks>
/// <para>
/// <c>GetRemainingQuantity()</c> is the number every harvest admission decision turns on, and its
/// two usage terms are asymmetric in a way that is easy to transcribe backwards: <em>main</em> comes
/// off the idle count directly, while <em>any</em> is absorbed by whatever is busy first. Reversing
/// them produces a number that is right whenever nothing is growing, which is most of the time a
/// stand-in would be looking.
/// </para>
/// <para>
/// <b>Scope.</b> The four inputs are read from the game and supplied to the port, so what is under
/// test is the expression rather than the collector's reading of its terms — the same division
/// <see cref="AutomataCostVerifier"/> draws, and for the same reason: two failures that need
/// different fixes should not share one verdict.
/// </para>
/// <para>
/// Inputs are read before either native answer is asked for. <c>AsInt()</c> settles a dirty modifier
/// record, so reading it first is what makes both sides see one number; asking the game first would
/// leave the port comparing against a value that changed underneath it.
/// </para>
/// </remarks>
internal sealed class AutomataPlotQuantityVerifier
{
    private readonly PlotQuantityContract? _contract;

    internal AutomataPlotQuantityVerifier(Type plotNodeType) =>
        _contract = PlotQuantityContract.TryResolve(plotNodeType);

    internal bool IsAvailable => _contract is not null;

    internal bool TryVerify(object node, DifferentialRun run, out string failure)
    {
        if (node is null) throw new ArgumentNullException(nameof(node));
        if (run is null) throw new ArgumentNullException(nameof(run));

        if (_contract is null)
        {
            failure = "The PlotNodeSO quantity contract is unavailable on this build.";
            return false;
        }

        try
        {
            var entityId = _contract.ReadGuid(node);
            var idle = _contract.ReadIdleQuantity(node);
            var total = _contract.ReadTotalQuantity(node);
            var usageMain = _contract.ReadUsageMain(node);
            var usageAny = _contract.ReadUsageAny(node);

            run.Compare(
                entityId,
                "GetRemainingQuantity",
                new BigDouble(GameWorldStateDeriver.RemainingQuantity(idle, total, usageMain, usageAny)),
                new BigDouble(_contract.InvokeRemainingQuantity(node)));
            run.Compare(
                entityId,
                "GetRemainingTotalQuantity",
                new BigDouble(GameWorldStateDeriver.RemainingTotalQuantity(total, usageMain, usageAny)),
                new BigDouble(_contract.InvokeRemainingTotalQuantity(node)));

            failure = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            failure = $"Reading a plot node's quantities threw: {ex.GetBaseException().Message}";
            return false;
        }
    }

    /// <summary>
    /// The reflected members needed to read a node's quantity inputs and ask the game for its own
    /// answer. A missing member makes the whole verifier unavailable rather than partial.
    /// </summary>
    private sealed class PlotQuantityContract
    {
        private const BindingFlags Instance =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        private readonly MethodInfo _getGuid;
        private readonly MethodInfo _getQuantity;
        private readonly object _idlePhase;
        private readonly MethodInfo _getTotalQuantity;
        private readonly MethodInfo _getRemainingQuantity;
        private readonly MethodInfo _getRemainingTotalQuantity;
        private readonly FieldInfo _usageMain;
        private readonly FieldInfo _usageAny;
        private readonly MethodInfo _asInt;

        private PlotQuantityContract(
            MethodInfo getGuid,
            MethodInfo getQuantity,
            object idlePhase,
            MethodInfo getTotalQuantity,
            MethodInfo getRemainingQuantity,
            MethodInfo getRemainingTotalQuantity,
            FieldInfo usageMain,
            FieldInfo usageAny,
            MethodInfo asInt)
        {
            _getGuid = getGuid;
            _getQuantity = getQuantity;
            _idlePhase = idlePhase;
            _getTotalQuantity = getTotalQuantity;
            _getRemainingQuantity = getRemainingQuantity;
            _getRemainingTotalQuantity = getRemainingTotalQuantity;
            _usageMain = usageMain;
            _usageAny = usageAny;
            _asInt = asInt;
        }

        internal static PlotQuantityContract? TryResolve(Type? plotNodeType)
        {
            if (plotNodeType is null) return null;

            var getGuid = FindNoArg(plotNodeType, "GetGuid");
            var getTotalQuantity = FindNoArg(plotNodeType, "GetTotalQuantity");
            var getRemainingQuantity = FindNoArg(plotNodeType, "GetRemainingQuantity");
            var getRemainingTotalQuantity = FindNoArg(plotNodeType, "GetRemainingTotalQuantity");
            if (getGuid is null || getTotalQuantity is null ||
                getRemainingQuantity is null || getRemainingTotalQuantity is null)
            {
                return null;
            }

            // The phase argument is taken from the accessor's own signature rather than from a type
            // name: the game nests the enum inside PlotNodeSO and nothing guarantees a second build
            // nests it the same way. The member is named, so an enum that dropped Idle fails closed.
            var getQuantity = FindOneEnumArg(plotNodeType, "GetQuantity");
            var phaseType = getQuantity?.GetParameters()[0].ParameterType;
            if (getQuantity is null || phaseType is null || !Enum.IsDefined(phaseType, "Idle"))
                return null;

            var usageMain = plotNodeType.GetField("actionQuantityUsageMain", Instance);
            var usageAny = plotNodeType.GetField("actionQuantityUsageAny", Instance);
            if (usageMain is null || usageAny is null) return null;

            var asInt = FindNoArg(usageMain.FieldType, "AsInt");
            if (asInt is null || asInt.ReturnType != typeof(int)) return null;

            return new PlotQuantityContract(
                getGuid,
                getQuantity,
                Enum.Parse(phaseType, "Idle"),
                getTotalQuantity,
                getRemainingQuantity,
                getRemainingTotalQuantity,
                usageMain,
                usageAny,
                asInt);
        }

        internal Guid ReadGuid(object node) =>
            _getGuid.Invoke(node, null) is Guid guid ? guid : Guid.Empty;

        internal int ReadIdleQuantity(object node) =>
            Convert.ToInt32(_getQuantity.Invoke(node, new[] { _idlePhase }));

        internal int ReadTotalQuantity(object node) =>
            Convert.ToInt32(_getTotalQuantity.Invoke(node, null));

        internal int ReadUsageMain(object node) => ReadUsage(_usageMain, node);

        internal int ReadUsageAny(object node) => ReadUsage(_usageAny, node);

        internal int InvokeRemainingQuantity(object node) =>
            Convert.ToInt32(_getRemainingQuantity.Invoke(node, null));

        internal int InvokeRemainingTotalQuantity(object node) =>
            Convert.ToInt32(_getRemainingTotalQuantity.Invoke(node, null));

        private int ReadUsage(FieldInfo field, object node)
        {
            var record = field.GetValue(node);
            return record is null ? 0 : Convert.ToInt32(_asInt.Invoke(record, null));
        }

        private static MethodInfo? FindNoArg(Type type, string name) =>
            type.GetMethod(name, Instance, null, Type.EmptyTypes, null);

        private static MethodInfo? FindOneEnumArg(Type type, string name)
        {
            foreach (var candidate in type.GetMethods(Instance))
            {
                if (candidate.Name != name || candidate.ReturnType != typeof(int)) continue;

                var parameters = candidate.GetParameters();
                if (parameters.Length == 1 && parameters[0].ParameterType.IsEnum) return candidate;
            }

            return null;
        }
    }
}
