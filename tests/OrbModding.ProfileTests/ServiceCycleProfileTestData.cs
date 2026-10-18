using System;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;

namespace OrbModding.ProfileTests;

internal static class ServiceCycleProfileTestData
{
    internal static readonly Guid BuildId = new("5c21a5bc-89a1-4ba7-ad8d-046a4358b57f");

    internal static ServiceCycleProfileCalibration Calibration(
        bool traceActive = false,
        bool allocationAvailable = true) =>
        new(10_000_000, 100, 200, BuildId, traceActive, allocationAvailable);

    internal static ServiceCycleProfileRecord Record(
        int stage = 1,
        long startedAt = 300,
        long elapsed = 40,
        long allocatedBytes = 16)
    {
        var operations = new ServiceCycleProfileOperations(stableIdReads: 5_000_000_000, listEntries: 6_000_000_001);
        return ServiceCycleProfileRecord.Sample(
            stage,
            serviceOrdinal: 2,
            lifecycle: 3,
            cycle: 4,
            frame: 5,
            startedAt,
            elapsed,
            allocatedBytes,
            ServiceCycleProfileTemperature.Warm,
            in operations);
    }
}
