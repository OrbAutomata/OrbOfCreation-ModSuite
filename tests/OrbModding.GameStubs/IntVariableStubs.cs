using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

public sealed class IntVariable : NumberVariable
{
    public IntVariable() => value = new ValueModifierRecord(new BigDouble(1));
    // The game keeps every global integer in one static registry, and world collection walks it
    // rather than naming the hundred-odd accessors that read out of it.
    public static List<IntVariable> All = new List<IntVariable>();

    // The two flags that decide the value is a count of seconds, and how finely the game draws it.

    public override Guid GetGuid() => GetGuidFailure is { } failure ? throw failure : uuid;

    // The game's AsInt() reads out of `value`; storing the answer separately let a fixture set a
    // count the collector could never see. Setting either keeps both in step.
    public int Value
    {
        get => this.value.AsInt();
        set
        {
            this.value = new ValueModifierRecord(new BigDouble(value, 0));
        }
    }


    public int SetCalls { get; private set; }

    public int? ThrowBeforeWriteFor { get; set; }

    public int? ThrowAfterWriteFor { get; set; }

    /// <summary>What the setter throws for <see cref="ThrowBeforeWriteFor"/> and <see cref="ThrowAfterWriteFor"/>.</summary>
    public Func<string, Exception> SetFailure { get; set; } = message => new InvalidOperationException(message);

    public Exception? ReadFailure { get; set; }

    /// <summary>Limits <see cref="ReadFailure"/> to reads taken while the variable holds this value.</summary>
    public int? ReadFailureWhileValueIs { get; set; }

    public int AsInt() =>
        ReadFailure is { } failure && (ReadFailureWhileValueIs is null || ReadFailureWhileValueIs == Value)
            ? throw failure
            : Value;

    public void SetValue(int value)
    {
        SetCalls++;
        if (ThrowBeforeWriteFor == value)
        {
            throw SetFailure($"setter rejected {value} before write");
        }

        Value = value;
        if (ThrowAfterWriteFor == value)
        {
            throw SetFailure($"setter rejected {value} after write");
        }
    }

    internal static IntVariable Register(Guid id)
    {
        var variable = new IntVariable { uuid = id };
        All.Add(variable);
        return variable;
    }

    /// <summary>
    /// Swaps a well-known global, keeping the registry to one entry per identity. Tests replace
    /// these wholesale between cases; the game never does, and a registry that grew one entry per
    /// reset would hand world collection a different answer on every pass.
    /// </summary>
    internal static IntVariable Replace(IntVariable current, IntVariable replacement, Guid id)
    {
        All.Remove(current);
        replacement.uuid = id;
        All.Add(replacement);
        return replacement;
    }
    public int Maximum { get => Value; set => Value = value; }
    public Exception? GetGuidFailure { get; set; }
}
