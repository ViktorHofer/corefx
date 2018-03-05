// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;

namespace System.Buffers
{
    internal sealed class CharOwnedMemory : OwnedMemory<char>
    {
        private readonly int _length;
        private IntPtr _ptr;
        private int _retainedCount;
        private bool _disposed;

        public CharOwnedMemory(IntPtr ptr, int length)
        {
            _length = length;
            _ptr = ptr;
        }

        ~CharOwnedMemory()
        {
            Debug.WriteLine($"{nameof(CharOwnedMemory)} being finalized");
            Dispose(false);
        }

        public override bool IsDisposed
        {
            get
            {
                lock (this)
                {
                    return _disposed && _retainedCount == 0;
                }
            }
        }

        public override int Length => _length;

        protected override bool IsRetained
        {
            get
            {
                lock (this)
                {
                    return _retainedCount > 0;
                }
            }
        }

        public override unsafe Span<char> Span => new Span<char>((void*)_ptr, _length);

        public override unsafe MemoryHandle Pin(int byteOffset = 0)
        {
            if (byteOffset < 0 || byteOffset > _length)
                throw new ArgumentOutOfRangeException(nameof(byteOffset));

            void* pointer = (void*)((char*)_ptr + byteOffset * sizeof(char));

            return new MemoryHandle(this, pointer);
        }

        public override bool Release()
        {
            lock (this)
            {
                if (_retainedCount > 0)
                {
                    _retainedCount--;
                    if (_retainedCount == 0)
                    {
                        if (_disposed)
                        {
                            _ptr = IntPtr.Zero;
                        }
                        return true;
                    }
                }
            }
            return false;
        }

        public override void Retain()
        {
            lock (this)
            {
                if (_retainedCount == 0 && _disposed)
                {
                    throw new Exception();
                }
                _retainedCount++;
            }
        }

        protected override void Dispose(bool disposing)
        {
            lock (this)
            {
                _disposed = true;
                if (_retainedCount == 0)
                {
                    _ptr = IntPtr.Zero;
                }
            }
        }

        protected override bool TryGetArray(out ArraySegment<char> arraySegment)
        {
            arraySegment = default;
            return false;
        }
    }
}
