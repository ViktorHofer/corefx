// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Buffers;
using System.Runtime.InteropServices;

namespace System.Text.RegularExpressions
{
    // Callback class
    public delegate string MatchEvaluator(Match match);

    public partial class Regex
    {
        private const int ReplaceBufferSize = 256;

        /// <summary>
        /// Replaces all occurrences of the pattern with the <paramref name="replacement"/> pattern, starting at
        /// the first character in the input string.
        /// </summary>
        public static string Replace(string input, string pattern, string replacement)
        {
            return Replace(input, pattern, replacement, RegexOptions.None, s_defaultMatchTimeout);
        }

        /// <summary>
        /// Replaces all occurrences of
        /// the <paramref name="pattern "/>with the <paramref name="replacement "/>
        /// pattern, starting at the first character in the input string.
        /// </summary>
        public static string Replace(string input, string pattern, string replacement, RegexOptions options)
        {
            return Replace(input, pattern, replacement, options, s_defaultMatchTimeout);
        }

        public static string Replace(string input, string pattern, string replacement, RegexOptions options, TimeSpan matchTimeout)
        {
            return new Regex(pattern, options, matchTimeout, true).Replace(input, replacement);
        }

        public static bool TryReplace(ReadOnlySpan<char> input, Span<char> destination, out int charsWritten, string pattern, string replacement, RegexOptions options = RegexOptions.None, TimeSpan? matchTimeout = null)
        {
            return new Regex(pattern, options, matchTimeout ?? s_defaultMatchTimeout, true).TryReplace(input, destination, out charsWritten, replacement);
        }

        /// <summary>
        /// Replaces all occurrences of the previously defined pattern with the
        /// <paramref name="replacement"/> pattern, starting at the first character in the
        /// input string.
        /// </summary>
        public string Replace(string input, string replacement)
        {
            if (input == null)
                throw new ArgumentNullException(nameof(input));

            return ReplaceImpl(targetSpan: false, input.AsSpan(), Span<char>.Empty, out _, out _, replacement, -1, UseOptionR() ? input.Length : 0);
        }

        /// <summary>
        /// Replaces all occurrences of the previously defined pattern with the
        /// <paramref name="replacement"/> pattern, starting at the first character in the
        /// input string.
        /// </summary>
        public string Replace(string input, string replacement, int count)
        {
            if (input == null)
                throw new ArgumentNullException(nameof(input));

            return ReplaceImpl(targetSpan: false, input.AsSpan(), Span<char>.Empty, out _, out _, replacement, count, UseOptionR() ? input.Length : 0);
        }

        public bool TryReplace(ReadOnlySpan<char> input, Span<char> destination, out int charsWritten, string replacement, int count = -1)
        {
            ReplaceImpl(targetSpan: true, input, destination, out charsWritten, out bool spanSuccess, replacement, count, UseOptionR() ? input.Length : 0);

            return spanSuccess;
        }

        /// <summary>
        /// Replaces all occurrences of the previously defined pattern with the
        /// <paramref name="replacement"/> pattern, starting at the character position
        /// <paramref name="startat"/>.
        /// </summary>
        public string Replace(string input, string replacement, int count, int startat)
        {
            if (input == null)
                throw new ArgumentNullException(nameof(input));

            return ReplaceImpl(targetSpan: false, input.AsSpan(), Span<char>.Empty, out _, out _, replacement, count, startat);
        }

        /// <summary>
        /// Replaces all occurrences of the <paramref name="pattern"/> with the recent
        /// replacement pattern.
        /// </summary>
        public static string Replace(string input, string pattern, MatchEvaluator evaluator)
        {
            return Replace(input, pattern, evaluator, RegexOptions.None, s_defaultMatchTimeout);
        }

        /// <summary>
        /// Replaces all occurrences of the <paramref name="pattern"/> with the recent
        /// replacement pattern, starting at the first character.
        /// </summary>
        public static string Replace(string input, string pattern, MatchEvaluator evaluator, RegexOptions options)
        {
            return Replace(input, pattern, evaluator, options, s_defaultMatchTimeout);
        }

        public static string Replace(string input, string pattern, MatchEvaluator evaluator, RegexOptions options, TimeSpan matchTimeout)
        {
            return new Regex(pattern, options, matchTimeout, true).Replace(input, evaluator);
        }

        public static bool TryReplace(ReadOnlySpan<char> input, Span<char> destination, out int charsWritten, string pattern, MatchEvaluator evaluator, RegexOptions options = RegexOptions.None, TimeSpan? matchTimeout = null)
        {
            return new Regex(pattern, options, matchTimeout ?? s_defaultMatchTimeout, true).TryReplace(input, destination, out charsWritten, evaluator);
        }

        /// <summary>
        /// Replaces all occurrences of the previously defined pattern with the recent
        /// replacement pattern, starting at the first character position.
        /// </summary>
        public string Replace(string input, MatchEvaluator evaluator)
        {
            if (input == null)
                throw new ArgumentNullException(nameof(input));

            return ReplaceImpl(targetSpan: false, input.AsSpan(), Span<char>.Empty, out _, out _, evaluator, -1, UseOptionR() ? input.Length : 0);
        }

        /// <summary>
        /// Replaces all occurrences of the previously defined pattern with the recent
        /// replacement pattern, starting at the first character position.
        /// </summary>
        public string Replace(string input, MatchEvaluator evaluator, int count)
        {
            if (input == null)
                throw new ArgumentNullException(nameof(input));

            return ReplaceImpl(targetSpan: false, input.AsSpan(), Span<char>.Empty, out _, out _, evaluator, count, UseOptionR() ? input.Length : 0);
        }

        public bool TryReplace(ReadOnlySpan<char> input, Span<char> destination, out int charsWritten, MatchEvaluator evaluator, int count = -1)
        {
            ReplaceImpl(targetSpan: true, input, destination, out charsWritten, out bool spanSuccess, evaluator, count, UseOptionR() ? input.Length : 0);

            return spanSuccess;
        }

        /// <summary>
        /// Replaces all occurrences of the previously defined pattern with the recent
        /// replacement pattern, starting at the character position
        /// <paramref name="startat"/>.
        /// </summary>
        public string Replace(string input, MatchEvaluator evaluator, int count, int startat)
        {
            if (input == null)
                throw new ArgumentNullException(nameof(input));

            return ReplaceImpl(targetSpan: false, input.AsSpan(), Span<char>.Empty, out _, out _, evaluator, count, startat);
        }

        /// <summary>
        /// Replaces <paramref name="count"/> occurrences of the regex in the text with the
        /// replacement pattern.
        ///
        /// Note that the special case of no matches is handled on its own:
        /// with no matches, the input text is returned unchanged.
        /// </summary>
        private string ReplaceImpl(bool targetSpan, ReadOnlySpan<char> input, Span<char> destination, out int charsWritten, out bool spanSuccess, string replacement, int count, int startat)
        {
            if (replacement == null)
                throw new ArgumentNullException(nameof(replacement));
            if (count < -1)
                throw new ArgumentOutOfRangeException(nameof(count), SR.CountTooSmall);
            if (startat < 0 || startat > input.Length)
                throw new ArgumentOutOfRangeException(nameof(startat), SR.BeginIndexNotNegative);

            // If count is zero return the input text.
            if (count == 0)
                return input.CopyInput(targetSpan, destination, out charsWritten, out spanSuccess);

            // Generate the first Match by using the provided ReadOnlySpan and pass an empty input Memory to it to 
            // avoid the Span to Memory conversion costs (pinning/copying).
            Match match = Run(false, -1, MemoryOrPinnedSpan<char>.Empty, input, 0, input.Length, startat);

            // If match fails, return the input text.
            if (!match.Success)
                return input.CopyInput(targetSpan, destination, out charsWritten, out spanSuccess);

            // Gets the weakly cached replacement helper or creates one if there isn't one already.
            RegexReplacement repl = RegexReplacement.GetOrCreate(ReplRef, replacement, caps, capsize, capnames, roptions);
            Span<char> charInitSpan = stackalloc char[ReplaceBufferSize];
            var vsb = new ValueStringBuilder(charInitSpan);

            if (!RightToLeft)
            {
                int prevat = 0;

                do
                {
                    if (match.Index != prevat)
                        vsb.Append(input.Slice(prevat, match.Index - prevat));

                    prevat = match.Index + match.Length;
                    repl.Replace(input, ref vsb, match);
                    if (--count == 0)
                        break;

                    // Generate the next Match by always using the same ReadOnlySpan.
                    match = match.NextMatch(input);
                } while (match.Success);

                if (prevat < input.Length)
                    vsb.Append(input.Slice(prevat, input.Length - prevat));
            }
            else
            {
                // In right to left mode append all the inputs in reversed order to avoid an extra dynamic data structure
                // and to be able to work with Spans. A final reverse of the transformed reversed input string generates
                // the desired output. Similar to Tower of Hanoi.

                int prevat = input.Length;

                do
                {
                    if (match.Index + match.Length != prevat)
                        vsb.AppendReversed(input.Slice(match.Index + match.Length, prevat - match.Index - match.Length));

                    prevat = match.Index;
                    repl.ReplaceRTL(input, ref vsb, match);
                    if (--count == 0)
                        break;

                    // Generate the next Match by always using the same ReadOnlySpan.
                    match = match.NextMatch(input);
                } while (match.Success);

                if (prevat > 0)
                    vsb.AppendReversed(input.Slice(0, prevat));
            }

            // Return the transformed input text either by writing into the provided output Span or by returning a string, 
            // depending on the targetSpan switch. In right to left mode, do a final reverse of the transformed input.
            return vsb.CopyOutput(targetSpan, destination, out charsWritten, out spanSuccess, RightToLeft);
        }

        /// <summary>
        /// Replaces all occurrences of the regex in the string with the
        /// replacement evaluator.
        ///
        /// Note that the special case of no matches is handled on its own:
        /// with no matches, the input string is returned unchanged.
        /// The right-to-left case is split out because StringBuilder
        /// doesn't handle right-to-left string building directly very well.
        /// </summary>
        private unsafe string ReplaceImpl(bool targetSpan, ReadOnlySpan<char> input, Span<char> destination, out int charsWritten, out bool spanSuccess, MatchEvaluator evaluator, int count, int startat)
        {
            if (evaluator == null)
                throw new ArgumentNullException(nameof(evaluator));
            if (count < -1)
                throw new ArgumentOutOfRangeException(nameof(count), SR.CountTooSmall);
            if (startat < 0 || startat > input.Length)
                throw new ArgumentOutOfRangeException(nameof(startat), SR.BeginIndexNotNegative);

            // If count is zero return the input text.
            if (count == 0)
                return input.CopyInput(targetSpan, destination, out charsWritten, out spanSuccess);

            // We need to create a Memory<char> as the match evaluator could access the Value
            // which we usually leave empty during Replace and IsMatch calls to reduce costs.
            // By using a custom OwnedMemory and passing just the Span's pointer to it we avoid
            // copying of the input.
            fixed (char* ptr = &MemoryMarshal.GetReference(input))
            {
                var mem = new MemoryOrPinnedSpan<char>(ptr, input.Length);
                Match match = Run(false, -1, mem, input, 0, input.Length, startat);
                
                if (!match.Success)
                    return input.CopyInput(targetSpan, destination, out charsWritten, out spanSuccess);

                Span<char> charInitSpan = stackalloc char[ReplaceBufferSize];
                var vsb = new ValueStringBuilder(charInitSpan);

                if (!RightToLeft)
                {
                    int prevat = 0;

                    do
                    {
                        if (match.Index != prevat)
                            vsb.Append(input.Slice(prevat, match.Index - prevat));

                        prevat = match.Index + match.Length;
                        vsb.Append(evaluator(match));

                        if (--count == 0)
                            break;

                        // Generate the next Match by always using the same ReadOnlySpan.
                        match = match.NextMatch(input);
                    } while (match.Success);

                    if (prevat < input.Length)
                        vsb.Append(input.Slice(prevat, input.Length - prevat));
                }
                else
                {
                    // In right to left mode append all the inputs in reversed order to avoid an extra dynamic data structure
                    // and to be able to work with Spans. A final reverse of the transformed reversed input string generates
                    // the desired output. Similar to Tower of Hanoi.

                    int prevat = input.Length;

                    do
                    {
                        if (match.Index + match.Length != prevat)
                            vsb.AppendReversed(input.Slice(match.Index + match.Length, prevat - match.Index - match.Length));

                        prevat = match.Index;
                        vsb.AppendReversed(evaluator(match));

                        if (--count == 0)
                            break;

                        // Generate the next Match by always using the same ReadOnlySpan.
                        match = match.NextMatch(input);
                    } while (match.Success);

                    if (prevat > 0)
                        vsb.AppendReversed(input.Slice(0, prevat));
                }

                // Return the transformed input text either by writing into the provided output Span or by returning a string, 
                // depending on the targetSpan switch. In right to left mode, do a final reverse of the transformed input.
                return vsb.CopyOutput(targetSpan, destination, out charsWritten, out spanSuccess, RightToLeft);
            }            
        }
    }
}
