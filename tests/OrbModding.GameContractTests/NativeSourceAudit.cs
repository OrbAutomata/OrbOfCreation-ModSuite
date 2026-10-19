using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace OrbModding.GameContractTests;

internal static class NativeSourceAudit
{
    private static readonly Regex QualifiedTargetPattern = new(
        "\"(?<type>[A-Za-z_][A-Za-z0-9_.+`]*):(?<member>[A-Za-z_][A-Za-z0-9_]*)\"",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex TypeResolverPattern = new(
        "(?:AccessTools\\.TypeByName|ReflectionUtil\\.FindLoadedType)\\s*\\(\\s*\"(?<type>[A-Za-z_][A-Za-z0-9_.+`]*)\"",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// A reflection selector and the literal it names, which need not share a physical line.
    /// </summary>
    /// <remarks>
    /// The gap was <c>[^;\r\n]</c> — same line only — so a call wrapped across lines named a native
    /// target the audit could not see. Twenty-five such calls were live when this was widened, among
    /// them <c>AutoBuyNativeQueueRoomAdapter</c>'s <c>GetRemainingRoom</c>. Stopping at the statement
    /// terminator still keeps one call's literals from bleeding into the next.
    /// </remarks>
    private static readonly Regex LiteralSelectorPattern = new(
        "(?:GetMethod|GetField|GetProperty|FindMethod|FindField|ReadBool|ReadInt)\\s*\\([^;]*?\"(?<target>[A-Za-z_][A-Za-z0-9_.+`]*)\"",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Every name the binder selects a native member by, across both of its surfaces:
    /// <c>NativeAccessorBinder</c>'s static form and <c>WorldMemberBinding</c>'s instance form.
    /// </summary>
    /// <remarks>
    /// One list serves both receivers on purpose. The two lists that preceded it had drifted apart —
    /// the static side knew <c>CollectionField</c>, <c>CollectionElementType</c>, and
    /// <c>StaticList</c> while the instance side knew none of them — so the same selector was
    /// audited or invisible depending only on which form a binder happened to write, and the newest
    /// world binders write the instance form. <see cref="NativeSourceAuditTests.EveryStringTakingBinderMemberIsClassified"/>
    /// holds this list to the binder's own API, so a selector added there cannot arrive unaudited.
    /// </remarks>
    internal const string BinderSelectorNames =
        "BoxedField|CallList|CallObject|CallReferenceGuid|CallWithObjectArgument|Call|"
            + "CollectionCount|CollectionElementType|CollectionField"
            + "|EnumField|Field|GenericBase|ModifierRecord|NestedCollectionCount|NestedEnumField|NestedField|New"
            + "|ReferenceGuid|Reference|Setter|StableUuid|StaticCallObject|StaticCall|StaticDictionaryAccessor"
            + "|StaticField|StaticListAccessor|StaticReference|Through|Type";

    /// <summary>An argument list read to its own closing parenthesis, however deeply nested.</summary>
    /// <remarks>
    /// The gap was <c>[^;]*?\)</c> — first parenthesis wins — so a selector composed out of another
    /// selector ended at the inner call's close and the outer call's own literal fell outside the
    /// match entirely. <c>Call&lt;Guid&gt;(CollectionElementType(spellType, "spellTypes"),
    /// "GetGuid")</c> is the shape and <c>GetGuid</c> is the name that went missing. Counting
    /// parentheses reads the whole call and nothing past it, so one call's literals still cannot
    /// bleed into the next; string bodies are skipped so a parenthesis inside a literal cannot
    /// unbalance the count.
    /// </remarks>
    private const string BalancedArgumentList =
        "\\((?:[^()\"]|\"(?:\\\\.|[^\"\\\\])*\"|(?<open>\\()|(?<-open>\\)))*(?(open)(?!))\\)";

    /// <summary>
    /// World-category binders name their native targets two ways that no reflection API appears in:
    /// as arguments to a binder selector, and as the <c>TypeName</c> and <c>RegistryMember</c>
    /// overrides that say which type and registry the category walks. Both are declarations of a
    /// native contract, so both are audited as one — otherwise the manifest would stop covering the
    /// collector precisely as the collector grew to span every category the game ships.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A selector is recognised by its own name and its argument list rather than by its receiver,
    /// so neither renaming the local a binder binds through nor moving the call between the static
    /// and instance form can quietly drop it out of the audit. No file name appears here.
    /// </para>
    /// <para>
    /// The type argument is optional because most selectors carry none: a binder that returns a raw
    /// <c>int</c>, <c>Guid</c>, <c>IList</c>, or boxed <c>object</c> is called bare. Requiring one
    /// was what hid every instance-form <c>CollectionField</c>, <c>CollectionElementType</c>,
    /// <c>Reference</c>, and <c>Through</c> the newer world binders write — a hundred and six native
    /// member names, in the files that read the most of the game.
    /// </para>
    /// </remarks>
    private static readonly Regex BinderTargetPattern = new(
        "(?:\\.(?:" + BinderSelectorNames + ")(?![A-Za-z0-9_])(?:<(?:[^<>()]|<[^<>()]*>)*>)?\\s*"
            + BalancedArgumentList
            + "|(?:TypeName|RegistryMember)\\s*=>\\s*\"[^\"\\r\\n]*\")",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex BareLiteralPattern = new(
        "\"(?<target>[A-Za-z_][A-Za-z0-9_.+`]*)\"",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    internal static IEnumerable<string> FindLiteralTargets(string source)
    {
        foreach (Match match in QualifiedTargetPattern.Matches(source))
        {
            yield return match.Groups["type"].Value;
            yield return match.Groups["member"].Value;
        }

        foreach (Match match in TypeResolverPattern.Matches(source))
        {
            yield return match.Groups["type"].Value;
        }

        foreach (Match match in LiteralSelectorPattern.Matches(source))
        {
            yield return match.Groups["target"].Value;
        }

        foreach (Match match in BinderTargetPattern.Matches(source))
        {
            // One binder call can name two members — a field and a field inside it — so every literal
            // in the call is a target, not just the first.
            foreach (Match literal in BareLiteralPattern.Matches(match.Value))
            {
                yield return literal.Groups["target"].Value;
            }
        }

        if (source.Contains("[HarmonyPatch]", StringComparison.Ordinal))
        {
            foreach (Match match in Regex.Matches(source, "\"(?<target>[A-Za-z_][A-Za-z0-9_.+`]*)\""))
            {
                yield return match.Groups["target"].Value;
            }
        }
    }

    internal static string NormalizePath(string path) => path.Replace('\\', '/');
}
