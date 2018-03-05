// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace System.Buffers
{
    internal unsafe readonly struct MemoryOrPinnedSpan<T>
    {
        private readonly ReadOnlyMemory<T> _memory;
        private readonly char* _ptr;
        private readonly int _length;

        public MemoryOrPinnedSpan(ReadOnlyMemory<T> memory)
        {
            _memory = memory;
            _length = memory.Length;
            _ptr = null;
        }

        public MemoryOrPinnedSpan(char* pinnedSpan, int length)
        {
            _ptr = pinnedSpan;
            _length = length;
            _memory = null;
        }

        public int Length => _length;

        public ReadOnlySpan<T> Span => (_ptr != null) ? new ReadOnlySpan<T>(_ptr, Length) : _memory.Span;
    }
}
