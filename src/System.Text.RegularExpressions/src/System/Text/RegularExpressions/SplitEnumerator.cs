// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Buffers;

namespace System.Text.RegularExpressions
{
    public ref struct SplitEnumerator
    {
        private readonly Regex _regex;
        private readonly int _count;
        private int _index;
        private Match _match;
        private int _inputIndex;
        private int _groupIndex;

        internal SplitEnumerator(Regex regex, ReadOnlySpan<char> input, int count)
        {
            _regex = regex;
            Input = input;
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

        /// <summary>
        /// The original text.
        /// </summary>
        public ReadOnlySpan<char> Input { get; }

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
                Current = Input;
                _index++;
                return true;
            }

            // If last iteration yield the remainder.
            if (_index+1 == _count)
            {
                if (!_regex.RightToLeft)
                {
                    Current = Input.Slice(_inputIndex, Input.Length - _inputIndex);
                }
                else
                {
                    Current = Input.Slice(0, _inputIndex);
                }

                _index++;
                return true;
            }
            
            if (_match == null)
            {
                // First iteration, set the match with RTL considered.
                _match = _regex.Run(false, -1, MemoryOrPinnedSpan<char>.Empty, Input, 0, Input.Length, _regex.RightToLeft ? Input.Length : 0);
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
                        Current = Input.Slice(group.Index, group.Length);

                        _groupIndex++;
                        return true;
                    }

                    _groupIndex++;
                }

                // Evaluate next match and reset capture group index.
                _match = _match.NextMatch(Input);
                _groupIndex = 1;
            }

            // If match evaluation fails, exit.
            if (!_match.Success)
            {
                // If in first iteration, return the input text.
                if (_index == 0)
                {
                    Current = Input;
                }
                // Otherwise return the remainder of the last match index to the end of text (RTL or LTR).
                else if (!_regex.RightToLeft)
                {
                    Current = Input.Slice(_inputIndex, Input.Length - _inputIndex);
                }
                else
                {
                    Current = Input.Slice(0, _inputIndex);
                }

                // Set exit condition.
                _index = -1;
                return true;
            }

            // Match evaulation is successful, yield the text between the last match and the current match
            // and continue iteration.
            if (!_regex.RightToLeft)
            {
                Current = Input.Slice(_inputIndex, _match.Index - _inputIndex);
                _inputIndex = _match.Index + _match.Length;
            }
            else
            {
                Current = Input.Slice(_match.Index + _match.Length, _inputIndex - _match.Index - _match.Length);
                _inputIndex = _match.Index;
            }

            _index++;
            return true;
        }
    }
}
