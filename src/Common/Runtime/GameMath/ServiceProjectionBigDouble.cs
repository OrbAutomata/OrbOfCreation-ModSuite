using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.GameMath;

/// <summary>Preserves an unbounded game quantity as mantissa and exponent, without a lossy double conversion.</summary>
public static class ServiceProjectionBigDouble
{
    public static void Write(ServiceStateProjectionBuilder output, int key, BigDouble value)
    {
        output.Add(new ServiceProjectionKey(key), ServiceProjectionValue.FromFloatingPoint(value.Mantissa));
        output.Add(new ServiceProjectionKey(key + 1), ServiceProjectionValue.FromInteger(value.Exponent));
    }

    public static BigDouble Read(in ServiceStateProjectionSnapshot projection, int key)
    {
        var mantissa = 0d;
        var exponent = 0L;
        for (var index = 0; index < projection.Count; index++)
        {
            var entry = projection.GetEntry(index);
            if (entry.Key.Value == key && entry.Value.Kind == ServiceProjectionValueKind.FloatingPoint)
                mantissa = entry.Value.FloatingPoint;
            if (entry.Key.Value == key + 1 && entry.Value.Kind == ServiceProjectionValueKind.Integer)
                exponent = entry.Value.Integer;
        }
        return BigDouble.Normalize(mantissa, exponent);
    }
}
