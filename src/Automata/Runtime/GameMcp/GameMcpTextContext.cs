#if SERVICE_CYCLE_PROFILE
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Newtonsoft.Json.Linq;


namespace OrbAutomata.GameMcp;

internal static class GameMcpTextContext
{
    internal const string Indent = "  ";

    /// <summary>Past this, a one-line summary stops being easier to read than a block.</summary>
    internal const int InlineBudget = 110;

    /// <summary>
    /// One dialect for every table on the surface. A bare space is two columns or one name with a
    /// space in it and nothing in the row says which, so the page never offers the reader that
    /// choice — the two characters buy back a whole class of misread row.
    /// </summary>
    internal const string Delimiter = " | ";

    /// <summary>
    /// The canned sentences this response has already printed, and where each was printed.
    /// </summary>
    /// <remarks>
    /// A rule sentence is worth saying once per decision, not once per response. Keyed on the
    /// sentence alone, a post-reset answer printed the rule under <c>reset</c> and then rendered
    /// <c>reroll</c> as a bare class with nothing saying why — two different decisions, and the
    /// second lost the only line that explained it. The key is the sentence under the place it is
    /// being said, so siblings each keep theirs while a table's rows, which all render under their
    /// array's one place, still say a shared sentence once.
    /// </remarks>
    internal sealed class Said
    {
        private const char Separator = '\u001f';

        private readonly HashSet<string> _sentences;
        private string _place;

        internal Said()
            : this(new HashSet<string>(StringComparer.Ordinal), string.Empty)
        {
        }

        private Said(HashSet<string> sentences, string place)
        {
            _sentences = sentences;
            _place = place;
        }

        internal string Enter(string name)
        {
            var outer = _place;
            _place = outer + "/" + name;
            return outer;
        }

        internal void Leave(string outer) => _place = outer;

        internal bool Add(string reason) => _sentences.Add(_place + Separator + reason);

        /// <summary>
        /// A copy for lines that may never be printed, mergeable back once they are.
        /// </summary>
        internal Said Fork() => new(new HashSet<string>(_sentences, StringComparer.Ordinal), _place);

        internal void Merge(Said other) => _sentences.UnionWith(other._sentences);
    }

    /// <summary>
    /// The sentence a verdict ends in, or nothing where this response has already said it here.
    /// </summary>
    /// <remarks>
    /// Said once per place per response, whether or not a class rides with it. <paramref
    /// name="said"/> is null wherever a line is being measured rather than written — the budget
    /// tests that decide whether a block fits a cell must weigh the sentence they would print, and
    /// a measurement that recorded it would silence the first real occurrence. An unclassified
    /// verdict is separated by a dash rather than a colon, because the cell it sits in was
    /// introduced by one: <c>inLedger: no: …</c> reads as a stutter.
    /// </remarks>
    internal static string Sentence(Said? said, bool classified, string? reason)
    {
        if (reason is null || reason.Length == 0) return string.Empty;
        if (said is not null && !said.Add(reason)) return string.Empty;
        return (classified ? ": " : " — ") + reason;
    }
}
#endif
