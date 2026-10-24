using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

public sealed class ValueModifierRecord
{
    /// <summary>
    /// The value the game memoises. [NonSerialized] in the game, so it is zero for every record a save
    /// has not touched — and it is exactly what GetValue() answers whenever the record is clean, which
    /// is why world collection reads it rather than only recomputing.
    /// </summary>
    private BigDouble calculatedValue;

    /// <summary>
    /// Whether the memo is out of date. The game sets it when a modifier is added or removed and
    /// clears it inside Calculate(); nothing else moves it, so a record that never carries a modifier
    /// is never dirty and its memo is permanent.
    /// </summary>
    private bool calculationDirty;

    /// <summary>The number the record is built from. World collection reads this and folds.</summary>
    public double baseValue;

    /// <summary>
    /// The two modifier sets, named and shaped as the game names them on ModifierRecord. World
    /// collection reads both and evaluates Calculate() itself when the record is dirty.
    /// </summary>
    public Dictionary<Guid, ValueModifier> passiveModifiers = new Dictionary<Guid, ValueModifier>();

    public Dictionary<Guid, ValueModifier> activeModifiers = new Dictionary<Guid, ValueModifier>();

    public ValueModifierRecord(BigDouble value)
    {
        baseValue = value.ToDouble();
        calculatedValue = value;
    }

    /// <summary>
    /// Marks the memo out of date, the way the game does when a modifier is added or removed. A test
    /// that puts a modifier into one of the sets without this describes a record the game cannot
    /// produce.
    /// </summary>
    public ValueModifierRecord Dirty()
    {
        calculationDirty = true;
        return this;
    }

    /// <summary>Puts the memo somewhere a recomputation would not, the way a save load does.</summary>
    public ValueModifierRecord WithMemo(BigDouble memo)
    {
        calculatedValue = memo;
        return this;
    }

    /// <summary>Whether the memo is out of date, so a test can state the shape it built.</summary>
    public bool IsCalculationDirty => calculationDirty;

    /// <summary>
    /// The memo, which is what the game answers for a clean record. A dirty one recomputes, and this
    /// deliberately does not: a stand-in that reimplemented Calculate() would be a second copy of the
    /// arithmetic under test, and agreeing with it would prove nothing.
    /// </summary>
    public BigDouble GetValue() => calculatedValue;

    /// <summary>The whole-number reading, which the game defines as <c>GetValue().ToInt()</c>.</summary>
    public int AsInt() => GetValue().ToInt();

    public BigDouble AsPercent() => GetValue().AsPercent();

    public bool HasActiveElements() => activeModifiers.Count > 0;
}
