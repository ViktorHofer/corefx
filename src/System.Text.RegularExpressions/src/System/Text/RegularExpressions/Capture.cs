// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

// Capture is just a location/length pair that indicates the
// location of a regular expression match. A single regexp
// search may return multiple Capture within each capturing
// RegexGroup.

using System.Buffers;

namespace System.Text.RegularExpressions
{
    /// <summary>
    /// Represents the results from a single subexpression capture. The object represents
    /// the input slice for a single successful capture.
    /// </summary>
    public class Capture
    {
        private string _value;

        /// <summary>
        /// Creates a Capture with an input string, a start index of the capture
        /// and the length of the captured string.
        /// </summary>
        /// <param name="text">The input string provided by the user wrapped in a MemoryOrPinnedSpan.</param>
        internal Capture(in MemoryOrPinnedSpan<char> text, int index, int length)
        {
            Text = text;
            Index = index;
            Length = length;
        }

        /// <summary>
        /// Returns the position in the original string where the first character of
        /// captured substring was found.
        /// </summary>
        public int Index { get; private protected set; }

        /// <summary>
        /// Returns the length of the captured substring.
        /// </summary>
        public int Length { get; private protected set; }

        /// <summary>
        /// Returns the original text.
        /// </summary>
        internal MemoryOrPinnedSpan<char> Text { get; private protected set; }

        /*
         * The backing string won't be allocated until the proprety is accessed the first time.
         */
        /// <summary>
        /// Returns the value of this Regex Capture.
        /// </summary>
        public string Value => _value ?? (_value = ValueSpan.ToString());

        /// <summary>
        /// Returns the value of this Regex Capture.
        /// </summary>
        public ReadOnlySpan<char> ValueSpan => Text.Span.Slice(Index, Length);

        /// <summary>
        /// Returns the substring that was matched.
        /// </summary>
        public override string ToString() => Value;

        /// <summary>
        /// The substring to the left of the capture.
        /// </summary>
        internal ReadOnlySpan<char> GetLeftSubstring(ReadOnlySpan<char> input) => input.Slice(0, Index);

        /// <summary>
        /// The substring to the right of the capture.
        /// </summary>
        internal ReadOnlySpan<char> GetRightSubstring(ReadOnlySpan<char> input) =>
            input.Slice(Index + Length, input.Length - Index - Length);
    }
}
