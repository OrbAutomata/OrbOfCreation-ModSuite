using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

public class Player
{
    private static Player _instance = new Player(initializeOutputVariables: true);
    public IntVariable spellOutputLevel;
    public IntVariable maxSpellOutputLevel;
    public IntVariable reserveLevel;
    public IntVariable maxReserveLevel;
    public IntVariable druidryOutputLevel;
    public IntVariable maxDruidryOutputLevel;
    public IntVariable alchemyOutputLevel;
    public IntVariable maxAlchemyOutputLevel;

    public Player() : this(initializeOutputVariables: true)
    {
    }

    private Player(bool initializeOutputVariables)
    {
        spellOutputLevel = new IntVariable { Value = initializeOutputVariables ? 1 : 0 };
        maxSpellOutputLevel = new IntVariable { Value = initializeOutputVariables ? 100 : 0 };
        reserveLevel = new IntVariable { Value = initializeOutputVariables ? 1 : 0 };
        maxReserveLevel = new IntVariable { Value = initializeOutputVariables ? 100 : 0 };
        druidryOutputLevel = new IntVariable { Value = 1 };
        maxDruidryOutputLevel = new IntVariable { Value = 1 };
        alchemyOutputLevel = new IntVariable { Value = 1 };
        maxAlchemyOutputLevel = new IntVariable { Value = 1 };
    }

    public static Player Current
    {
        get => _instance;
        set => _instance = value;
    }

    public static IntVariable GetSpellOutputLevel() => _instance.spellOutputLevel;
    public static IntVariable GetReserveLevel() => _instance.reserveLevel;

    // The four variables a glyph's critical and echo slots print against. They are serialized
    // fields on the singleton rather than assets in a registry, so the accessor is the only way
    // to learn which variable a slot means.
    public DoubleVariable spellCriticalCastRating = new DoubleVariable();
    public DoubleVariable spellCriticalCastEffect = new DoubleVariable();
    public DoubleVariable spellDoubleCastRating = new DoubleVariable();
    public DoubleVariable spellDoubleCastEffect = new DoubleVariable();

    public static DoubleVariable GetSpellCriticalCastRating() => _instance.spellCriticalCastRating;
    public static DoubleVariable GetSpellCriticalCastEffect() => _instance.spellCriticalCastEffect;
    public static DoubleVariable GetSpellDoubleCastRating() => _instance.spellDoubleCastRating;
    public static DoubleVariable GetSpellDoubleCastEffect() => _instance.spellDoubleCastEffect;

    private static IntVariable bulkDevelopment =
        IntVariable.Register(KnownVariableIds.BulkDevelopment);

    public static IntVariable BulkDevelopment
    {
        get => bulkDevelopment;
        set => bulkDevelopment =
            IntVariable.Replace(bulkDevelopment, value, KnownVariableIds.BulkDevelopment);
    }

    public static IntVariable GetBulkDevelopment() => BulkDevelopment;

    // The three frame-wide terms the resource rate chain needs. Each is a DoubleVariable whose
    // `value` record carries the number, exactly as the game holds them, so a reader that walks
    // variable -> value -> calculatedValue works identically against the stub and the game.
    public static DoubleVariable ResourceOverflow { get; set; } = new DoubleVariable();

    public static DoubleVariable ResourceOverflowLoss { get; set; } = new DoubleVariable();

    public static DoubleVariable ResetTimePassed { get; set; } = new DoubleVariable();

    public static DoubleVariable GetResourceOverflow() => ResourceOverflow;

    public static DoubleVariable GetResourceOverflowLoss() => ResourceOverflowLoss;

    public static DoubleVariable GetResetTimePassed() => ResetTimePassed;

    // The frame-wide structure-cost multiplier, authored at parity like every other percent the
    // game holds.
    public static DoubleVariable StructureCost { get; set; } =
        new DoubleVariable { value = new ValueModifierRecord(new BigDouble(1.0, 2)) };

    public static DoubleVariable GetStructureCost() => StructureCost;

    // The fifth frame-wide term, and the exponent the game raises a resource's quality to when
    // discounting that resource's attribute cost. Same accessor shape as the other four —
    // `static DoubleVariable GetX()` over an instance field on the singleton — so a reader that
    // binds five accessors of one shape binds all five here too. Authored at zero, which is what
    // the game ships it at until the research that raises it: an exponent of zero leaves quality
    // out of attribute prices entirely.
    public static DoubleVariable AttributeQualityBonus { get; set; } = new DoubleVariable();

    public static DoubleVariable GetAttributeQualityBonus() => AttributeQualityBonus;

    public void ManagerStart()
    {
    }
}
