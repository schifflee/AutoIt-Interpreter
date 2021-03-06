﻿// #define SANITY_CHECK

using System;
using System.Collections.Generic;
using System.Linq;

namespace Piglet.Lexer.Construction
{
    internal class CharSet
    {
        private IList<CharRange> ranges = new List<CharRange>();

        public IEnumerable<CharRange> Ranges => ranges;

        public CharSet()
        {
        }

        public CharSet(IEnumerable<CharRange> ranges) => this.ranges = ranges.ToList();

        public CharSet(bool combine, params char[] ranges)
        {
            if (ranges.Length % 2 != 0)
                throw new ArgumentException("Number of chars in ranges must be an even number");

            for (int i = 0; i < ranges.Length; i += 2)
                AddRange(ranges[i], ranges[i + 1], combine);
        }

        public void Add(char c) => AddRange(c, c, true);

        public void AddRange(char from, char to, bool combine = true)
        {
            if (from > to)
            {
                char pivot = to;

                to = from;
                from = pivot;
            }

            if (combine)
            {
                // See if there is an old range that contains the new from as the to
                // in that case merge the ranges
                CharRange range = ranges.SingleOrDefault(f => f.To == from);

                if (range != null)
                {
                    range.To = to;

                    return;
                }

                // To the same thing the other direction
                range = ranges.SingleOrDefault(f => f.From == to);

                if (range != null)
                {
                    range.From = from;

                    return;
                }
            }

            // Ranges are not mergeable. Add the range straight up
            ranges.Add(new CharRange { From = from, To = to });
        }

        public bool Any() => ranges.Any();

        public override string ToString() => Any() ? string.Join(", ", ranges.Select(f => f.ToString()).ToArray()) : "ε";

        public void UnionWith(CharSet set)
        {
            foreach (CharRange range in set.ranges)
                if (!ranges.Contains(range))
                    if (ranges.Any(f => f.From == range.From || f.To == range.To))
#if DEBUG && SANITY_CHECK
                        throw new Exception("Do not want");
#else
                        ;
#endif
                    else
                        ranges.Add(range);
        }

        public CharSet Except(CharSet except)
        {
            CharSet cs = new CharSet();

            foreach (CharRange range in ranges)
                foreach (CharRange clippedRange in ClipRange(range, except.ranges))
                    cs.AddRange(clippedRange.From, clippedRange.To);

            return cs;
        }

        private IEnumerable<CharRange> ClipRange(CharRange range, IList<CharRange> excludedCharRanges)
        {
            char from = range.From;
            char to = range.To;

            foreach (CharRange excludedRange in excludedCharRanges)
            {
                // If the range is fully excluded by the excluded range, yield nothing
                if (excludedRange.From <= from && excludedRange.To >= to)
                    yield break;

                // Check if the excluded range is wholly contained within the range
                if (excludedRange.From > from && excludedRange.To < to )
                {
                    // Split this range and return
                    foreach (CharRange charRange in ClipRange(new CharRange {From = @from, To = (char)(excludedRange.From - 1)}, excludedCharRanges))
                        yield return charRange;

                    // Second split
                    foreach (CharRange charRange in ClipRange(new CharRange { From = (char)(excludedRange.To + 1), To = to }, excludedCharRanges))
                        yield return charRange;

                    yield break;
                }

                // Trim the edges of the range
                if (to >= excludedRange.From && to <= excludedRange.To)
                    to = (char)(excludedRange.From - 1);

                if (from >= excludedRange.From && from <= excludedRange.To)
                    from = (char)(excludedRange.To + 1);
            }

            // If the range has been clipped away to nothing, then quit
            if (to < from)
                yield break;

            // Return the possibly clipped range
            yield return new CharRange { From = from, To = to};
        }

        public CharSet Union(CharSet charRange)
        {
            CharSet c = new CharSet();

            foreach (CharRange range in ranges)
                c.AddRange(range.From, range.To);

            foreach (CharRange range in charRange.ranges)
                c.AddRange(range.From, range.To);

            return c;
        }

        public bool ContainsChar(char input) => ranges.Any(charRange => charRange.From <= input && charRange.To >= input);
    }
}