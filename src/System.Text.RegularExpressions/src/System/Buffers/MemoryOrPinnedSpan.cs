// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace System.Buffers
{
    internal unsafe readonly struct MemoryOrPinnedSpan<T>
    {
        private readonly ReadOnlyMemory<T> _memory;
        private readonly char* _pinnedSpan;

        public MemoryOrPinnedSpan(ReadOnlyMemory<T> memory)
        {
            _memory = memory;
            Length = memory.Length;
            _pinnedSpan = null;
        }

        public MemoryOrPinnedSpan(char* pinnedSpan, int length)
        {
            _pinnedSpan = pinnedSpan;
            Length = length;
        }

        public static MemoryOrPinnedSpan<T> Empty { get; } = new MemoryOrPinnedSpan<T>();

        public int Length { get; }

        public ReadOnlySpan<T> Span => (_pinnedSpan != null) ? new ReadOnlySpan<T>(_pinnedSpan, Length) : _memory.Span;
    }
}
