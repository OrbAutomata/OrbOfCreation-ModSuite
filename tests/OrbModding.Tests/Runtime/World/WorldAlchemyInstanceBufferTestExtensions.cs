namespace OrbModding.Common.Runtime.World;

internal static class WorldAlchemyInstanceBufferTestExtensions
{
    internal static void Append(this WorldRelationBuffer<RawWorldAlchemyInstance> buffer, in WorldAlchemyInstance row)
    {
        var sample = new RawWorldAlchemyInstance(
            row.RecipeId,
            row.Quantity,
            row.QueuedQuantity,
            row.DrainReadable,
            isDrainApplied: true,
            row.DrainRatio,
            row.DrainRatio);
        buffer.Append(in sample);
    }
}
