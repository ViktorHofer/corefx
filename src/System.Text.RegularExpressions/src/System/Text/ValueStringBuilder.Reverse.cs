// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace System.Text
{
    internal ref partial struct ValueStringBuilder
    {
        public void AppendReversed(ReadOnlySpan<char> value)
        {
            Span<char> span = AppendSpan(value.Length);
            value.CopyTo(span);
            span.Reverse();
        }

        public void Reverse(int start = 0, int length = 0)
        {
            _chars.Slice(start, length > 0 ? length : _pos).Reverse();
        }
    }
}
