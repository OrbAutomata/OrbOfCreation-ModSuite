using System;

namespace OrbModding.Common.Runtime.ServiceCycle.Contracts;

/// <summary>A Guid carried in a state projection as two integer entries, at <c>key</c> and <c>key + 1</c>.</summary>
public static class ServiceProjectionGuid
{
    public static void Write(ServiceStateProjectionBuilder output, int key, Guid value)
    {
        Span<byte> bytes = stackalloc byte[16];
        value.TryWriteBytes(bytes);
        output.Add(new ServiceProjectionKey(key), ServiceProjectionValue.FromInteger(BitConverter.ToInt64(bytes)));
        output.Add(new ServiceProjectionKey(key + 1), ServiceProjectionValue.FromInteger(BitConverter.ToInt64(bytes.Slice(8))));
    }

    public static Guid Read(in ServiceStateProjectionSnapshot projection, int key)
    {
        Span<byte> bytes = stackalloc byte[16];
        BitConverter.TryWriteBytes(bytes, Integer(in projection, key));
        BitConverter.TryWriteBytes(bytes.Slice(8), Integer(in projection, key + 1));
        return new Guid(bytes);
    }

    private static long Integer(in ServiceStateProjectionSnapshot projection, int key)
    {
        for (var index = 0; index < projection.Count; index++)
        {
            var entry = projection.GetEntry(index);
            if (entry.Key.Value == key && entry.Value.Kind == ServiceProjectionValueKind.Integer)
                return entry.Value.Integer;
        }
        return 0;
    }
}
