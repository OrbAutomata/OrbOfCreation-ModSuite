using System.Collections.Generic;
using System.Linq;

namespace Stacked
{
    public class StackedIdEntry<T>
    {
        public T item = default!;
        public int quantity;
    }

    public class AbstractStackedRecord<T, TEntry>
        where TEntry : StackedIdEntry<T>, new()
    {
        protected readonly List<TEntry> entries = new List<TEntry>();

        public void Set(T item, int quantity)
        {
            var entry = entries.FirstOrDefault(candidate => EqualityComparer<T>.Default.Equals(candidate.item, item));
            if (quantity <= 0)
            {
                if (entry is not null) entries.Remove(entry);
                return;
            }
            if (entry is null)
            {
                entry = new TEntry { item = item };
                entries.Add(entry);
            }
            entry.quantity = quantity;
        }

        public int GetQuantity(T item) =>
            entries.FirstOrDefault(candidate => EqualityComparer<T>.Default.Equals(candidate.item, item))?.quantity ?? 0;

        public int GetTotalStacks() => entries.Sum(entry => entry.quantity);

        public void Empty() => entries.Clear();

        public List<TEntry> ToList() => new List<TEntry>(entries);

        /// <summary>One element per distinct item, which is what the game writes back into the list.</summary>
        public List<T> GetItems() => entries.Select(static entry => entry.item).ToList();

        public void ImportItems(List<TEntry> items)
        {
            entries.Clear();
            foreach (var entry in items) Set(entry.item, entry.quantity);
        }

        /// <summary>
        /// One element per distinct item, with the multiplicities discarded — which is what the
        /// game's own <c>GetItemList</c> hands back, and therefore all
        /// <c>Spell.GetAugmentGlyphs()</c> can ever say about a two-of-one-glyph layout.
        /// </summary>
        public List<T> GetItemList() => entries.Select(static entry => entry.item).ToList();

        public List<(T, int)> GetEntries() =>
            entries.Select(static entry => (entry.item, entry.quantity)).ToList();
    }

    public sealed class StackedIdRecord<T> : AbstractStackedRecord<T, StackedIdEntry<T>>
    {
        public StackedIdRecord()
        {
        }

        public StackedIdRecord(StackedIdRecord<T> source)
        {
            foreach (var entry in source.GetEntries()) Set(entry.Item1, entry.Item2);
        }

        public StackedIdRecord(List<T> values)
        {
            foreach (var value in values) Set(value, GetQuantity(value) + 1);
        }
    }
}
