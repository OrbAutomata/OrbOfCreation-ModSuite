using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

/// <summary>
/// The staged glyph lists, stackable exactly as the shipped assembly declares them: the value list
/// carries one element per distinct glyph and the stack beside it carries how many of each.
/// </summary>
public sealed class GlyphListVariable : StackableListVariable<GlyphSO>
{
}
