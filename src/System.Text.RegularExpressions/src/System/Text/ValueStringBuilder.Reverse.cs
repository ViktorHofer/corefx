// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace System.Text
{
    internal ref partial struct ValueStringBuilder
    {
        public unsafe void AppendReversed(ReadOnlySpan<char> value)
        {
            int pos = _pos;
            if (pos > _chars.Length - value.Length)
            {
                Grow(value.Length);
            }

            Span<char> slice = _chars.Slice(_pos, value.Length);
            value.CopyTo(slice);
            slice.Reverse();
            _pos += value.Length;
        }

        public void Reverse(int start = 0, int length = 0)
        {
            _chars.Slice(start, length > 0 ? length : _pos).Reverse();
        }
    }
}
