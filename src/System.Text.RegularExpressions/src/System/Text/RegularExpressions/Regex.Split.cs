// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Buffers;
using System.Collections.Generic;

namespace System.Text.RegularExpressions
{
    public partial class Regex
    {
        /// <summary>
        /// Splits the <paramref name="input "/>string at the position defined
        /// by <paramref name="pattern"/>.
        /// </summary>
        public static string[] Split(string input, string pattern)
        {
            return Split(input, pattern, RegexOptions.None, s_defaultMatchTimeout);
        }

        /// <summary>
        /// Splits the <paramref name="input "/>string at the position defined by <paramref name="pattern"/>.
        /// </summary>
        public static string[] Split(string input, string pattern, RegexOptions options)
        {
            return Split(input, pattern, options, s_defaultMatchTimeout);
        }

        public static string[] Split(string input, string pattern, RegexOptions options, TimeSpan matchTimeout)
        {
            return new Regex(pattern, options, matchTimeout, true).Split(input);
        }

        public static SplitEnumerator Split(ReadOnlySpan<char> input, string pattern, RegexOptions options = RegexOptions.None, TimeSpan? matchTimeout = null)
        {
            return new Regex(pattern, options, matchTimeout ?? s_defaultMatchTimeout, true).Split(input);
        }

        /// <summary>
        /// Splits the <paramref name="input"/> string at the position defined by a
        /// previous pattern.
        /// </summary>
        public string[] Split(string input)
        {
            if (input == null)
                throw new ArgumentNullException(nameof(input));

            return SplitImpl(input, 0, UseOptionR() ? input.Length : 0);
        }

        /// <summary>
        /// Splits the <paramref name="input"/> string at the position defined by a
        /// previous pattern.
        /// </summary>
        public string[] Split(string input, int count)
        {
            if (input == null)
                throw new ArgumentNullException(nameof(input));

            return SplitImpl(input, count, UseOptionR() ? input.Length : 0);
        }

        public SplitEnumerator Split(ReadOnlySpan<char> input, int count = 0)
        {
            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count), SR.CountTooSmall);

            return new SplitEnumerator(this, input, count);
        }

        /// <summary>
        /// Splits the <paramref name="input"/> string at the position defined by a previous pattern.
        /// </summary>
        public string[] Split(string input, int count, int startat)
        {
            if (input == null)
                throw new ArgumentNullException(nameof(input));

            return SplitImpl(input, count, startat);
        }

        /// <summary>
        /// Does a split. In the right-to-left case we reorder the
        /// array to be forwards.
        /// </summary>
        private string[] SplitImpl(string input, int count, int startat)
        {
            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count), SR.CountTooSmall);
            if (startat < 0 || startat > input.Length)
                throw new ArgumentOutOfRangeException(nameof(startat), SR.BeginIndexNotNegative);

            if (count == 1)
            {
                return new string[1] { input };
            }

            count -= 1;
            Match match = Match(input, startat);

            if (!match.Success)
            {
                return new string[1] { input };
            }
            else
            {
                var al = new List<string>();

                if (!RightToLeft)
                {
                    int prevat = 0;

                    for (; ; )
                    {
                        al.Add(input.Substring(prevat, match.Index - prevat));

                        prevat = match.Index + match.Length;

                        // add all matched capture groups to the list.
                        for (int i = 1; i < match.Groups.Count; i++)
                        {
                            if (match.IsMatched(i))
                                al.Add(match.Groups[i].ToString());
                        }

                        if (--count == 0)
                            break;

                        match = match.NextMatch();

                        if (!match.Success)
                            break;
                    }

                    al.Add(input.Substring(prevat, input.Length - prevat));
                }
                else
                {
                    int prevat = input.Length;

                    for (; ; )
                    {
                        al.Add(input.Substring(match.Index + match.Length, prevat - match.Index - match.Length));

                        prevat = match.Index;

                        // add all matched capture groups to the list.
                        for (int i = 1; i < match.Groups.Count; i++)
                        {
                            if (match.IsMatched(i))
                                al.Add(match.Groups[i].ToString());
                        }

                        if (--count == 0)
                            break;

                        match = match.NextMatch();

                        if (!match.Success)
                            break;
                    }

                    al.Add(input.Substring(0, prevat));
                    al.Reverse();
                }

                return al.ToArray();
            }
        }

        public ref struct SplitEnumerator
        {
            private readonly Regex _regex;
            private ReadOnlySpan<char> _input;
            private readonly int _count;
            private int _index;
            private Match _match;
            private int _inputIndex;
            private int _groupIndex;

            internal SplitEnumerator(Regex regex, ReadOnlySpan<char> input, int count)
            {
                _regex = regex;
                _input = input;
                _count = count;
                _index = 0;
                _match = null;
                _inputIndex = _regex.RightToLeft ? input.Length : 0;
                _groupIndex = 1;
            }

            /// <summary>
            /// Gets the element at the current position of the enumerator.
            /// </summary>
            public ReadOnlySpan<char> Current { get; private set; }

            public SplitEnumerator GetEnumerator() => this;

            // Count <0 => throw in public API.
            // Count 0 => no boundary, iterate over all matches.
            // Count 1 => return whole string.
            // Count n => return n splits for n matches.
            public bool MoveNext()
            {
                // Exit if count limit is reached (count 0 means no limit) or
                // if the exist condition (index negative) is set.
                if ((_index >= _count && _count != 0) || _index < 0)
                {
                    return false;
                }

                // If count is 1 and we are in the first iteration we just return the input text.
                if (_index == 0 && _count == 1)
                {
                    Current = _input;
                    _index++;
                    return true;
                }

                // If last iteration yield the remainder.
                if (_index + 1 == _count)
                {
                    if (!_regex.RightToLeft)
                    {
                        Current = _input.Slice(_inputIndex, _input.Length - _inputIndex);
                    }
                    else
                    {
                        Current = _input.Slice(0, _inputIndex);
                    }

                    _index++;
                    return true;
                }

                if (_match == null)
                {
                    // First iteration, set the match with RTL considered.
                    _match = _regex.Run(false, -1, default, _input, 0, _input.Length, _regex.RightToLeft ? _input.Length : 0);
                }
                else
                {
                    // Before going to the next match, yield all except the first (which is the match itself) 
                    // capture groups in the current match.
                    while (_groupIndex < _match.Groups.Count)
                    {
                        if (_match.IsMatched(_groupIndex))
                        {
                            // We don't access the match Value property directly as we didn't set it.
                            Group group = _match.Groups[_groupIndex];
                            Current = _input.Slice(group.Index, group.Length);

                            _groupIndex++;
                            return true;
                        }

                        _groupIndex++;
                    }

                    // Evaluate next match and reset capture group index.
                    _match = _match.NextMatch(_input);
                    _groupIndex = 1;
                }

                // If match evaluation fails, exit.
                if (!_match.Success)
                {
                    // If in first iteration, return the input text.
                    if (_index == 0)
                    {
                        Current = _input;
                    }
                    // Otherwise return the remainder of the last match index to the end of text (RTL or LTR).
                    else if (!_regex.RightToLeft)
                    {
                        Current = _input.Slice(_inputIndex, _input.Length - _inputIndex);
                    }
                    else
                    {
                        Current = _input.Slice(0, _inputIndex);
                    }

                    // Set exit condition.
                    _index = -1;
                    return true;
                }

                // Match evaulation is successful, yield the text between the last match and the current match
                // and continue iteration.
                if (!_regex.RightToLeft)
                {
                    Current = _input.Slice(_inputIndex, _match.Index - _inputIndex);
                    _inputIndex = _match.Index + _match.Length;
                }
                else
                {
                    Current = _input.Slice(_match.Index + _match.Length, _inputIndex - _match.Index - _match.Length);
                    _inputIndex = _match.Index;
                }

                _index++;
                return true;
            }
        }
    }
}
