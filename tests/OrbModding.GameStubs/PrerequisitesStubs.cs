using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

public class Prerequisites
{
    /// <summary>
    /// Named as the game names it. `available` is a latch, not a question: Check() evaluates the
    /// conditions and leaves it set once they pass. Collection reads the latch; action boundaries
    /// that require current native truth may call Check().
    /// </summary>
    public class Container
    {
        public bool available;
        public bool NativeCheckResult { get; set; }
        public bool? ParameterizedCheckResult { get; set; }
        public int CheckCalls { get; private set; }
        public int ParameterizedCheckCalls { get; private set; }

        /// <summary>
        /// The conditions themselves. The suite reads how many there are — none is what an
        /// unconditional action authors — and never what they are or whether they hold.
        /// </summary>
        public List<object> prerequisites = new List<object>();

        /// <summary>
        /// What the no-argument <c>Check()</c> adds to every threshold it walks.
        /// </summary>
        /// <remarks>
        /// Private, as the game declares it, because the suite reads it by reflection and a public
        /// stub field would let a binding that cannot see the real one pass here. The game authors a
        /// nonzero one exactly once — <c>ResearchSO.Initialize()</c> gives
        /// <c>levelVisibilityPrereq</c> the negated <c>levelVisibilityRange</c> — and reaches it
        /// through this setter.
        /// </remarks>
        private BigDouble adjustValue;

        public Container SetAdjustValue(BigDouble value)
        {
            adjustValue = value;
            return this;
        }

        public BigDouble AdjustValue => adjustValue;

        public Exception? CheckFailure { get; set; }

        public bool Check()
        {
            CheckCalls++;
            if (CheckFailure is { } failure) throw failure;
            if (available) return true;
            if (NativeCheckResult) available = true;
            return available;
        }

        /// <summary>
        /// The per-level overload, which takes the level being bought and neither stamps nor latches.
        /// </summary>
        /// <remarks>
        /// Present so that the two overloads can be told apart, which is the one thing about reaching
        /// this oracle that a portable test can check — picking the parameterless one by name would
        /// turn a diagnostic into a mutation.
        /// <para>
        /// It answers only for the empty container, which is the case the game answers without
        /// consulting anything. It does not evaluate conditions and never will: a stand-in that did
        /// would be a second implementation of the arithmetic under test, and agreeing with it would
        /// mean nothing.
        /// </para>
        /// </remarks>
        public bool Check(Requirements.ConditionInfo conditionInfo)
        {
            ParameterizedCheckCalls++;
            return ParameterizedCheckResult ?? prerequisites.Count == 0;
        }
    }
}
