﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security;
using System.Threading;
using System.Threading.Tasks;

namespace System.IO.Pipes
{
    internal unsafe sealed class ConnectionCompletionSource : TaskCompletionSource<VoidResult>
    {
        private const int NoResult = 0;
        private const int ResultSuccess = 1;
        private const int ResultError = 2;
        private const int RegisteringCancellation = 4;
        private const int CompletedCallback = 8;

        private readonly NamedPipeServerStream _serverStream;
        private readonly ThreadPoolBoundHandle _threadPoolBinding;

        private CancellationTokenRegistration _cancellationRegistration;
        private CancellationToken _cancellationToken;
        private int _errorCode;
        private NativeOverlapped* _overlapped;
        private int _state;

#if DEBUG
        private bool _cancellationHasBeenRegistered;
#endif

        // Using RunContinuationsAsynchronously for compat reasons (old API used ThreadPool.QueueUserWorkItem for continuations)
        internal ConnectionCompletionSource(NamedPipeServerStream server, CancellationToken cancellationToken)
            : base(TaskCreationOptions.RunContinuationsAsynchronously)
        {
            Debug.Assert(server != null, "server is null");
            Debug.Assert(server._threadPoolBinding != null, "server._threadPoolBinding is null");

            _threadPoolBinding = server._threadPoolBinding;
            _serverStream = server;
            _cancellationToken = cancellationToken;

            _overlapped = _threadPoolBinding.AllocateNativeOverlapped((errorCode, numBytes, pOverlapped) =>
            {
                var completionSource = (ConnectionCompletionSource)ThreadPoolBoundHandle.GetNativeOverlappedState(pOverlapped);
                Debug.Assert(completionSource.Overlapped == pOverlapped);

                completionSource.AsyncCallback(errorCode, numBytes);
            }, this, null);

            _state = NoResult;
        }

        internal NativeOverlapped* Overlapped
        {
            [SecurityCritical]get { return _overlapped; }
        }

        internal void RegisterForCancellation()
        {
#if DEBUG
            Debug.Assert(!_cancellationHasBeenRegistered, "Cannot register for cancellation twice");
            _cancellationHasBeenRegistered = true;
#endif

            // Quick check to make sure that the cancellation token supports cancellation, and that the IO hasn't completed
            if (_cancellationToken.CanBeCanceled && Overlapped != null)
            {
                // Register the cancellation only if the IO hasn't completed
                int state = Interlocked.CompareExchange(ref _state, RegisteringCancellation, NoResult);
                if (state == NoResult)
                {
                    // Register the cancellation
                    _cancellationRegistration = _cancellationToken.Register(thisRef => ((ConnectionCompletionSource)thisRef).Cancel(), this);

                    // Grab the state for case if IO completed while we were setting the registration.
                    state = Interlocked.Exchange(ref _state, NoResult);
                }
                else if (state != CompletedCallback)
                {
                    // IO already completed and we have grabbed result state.
                    // Set NoResult to prevent invocation of CompleteCallback(result state) from AsyncCallback(...)
                    state = Interlocked.Exchange(ref _state, NoResult);
                }

                // If we have the result state of completed IO call CompleteCallback(result).
                // Otherwise IO not completed.
                if (state == ResultSuccess || state == ResultError)
                {
                    CompleteCallback(state);
                }
            }
        }

        internal void ReleaseResources()
        {
            _cancellationRegistration.Dispose();

            // NOTE: The cancellation must *NOT* be running at this point, or it may observe freed memory
            // (this is why we disposed the registration above)
            if (Overlapped != null)
            {
                _threadPoolBinding.FreeNativeOverlapped(Overlapped);
                _overlapped = null;
            }
        }

        internal void SetCompletedSynchronously()
        {
            _serverStream.State = PipeState.Connected;
            TrySetResult(default(VoidResult));
        }

        private void AsyncCallback(uint errorCode, uint numBytes)
        {
            // Special case for when the client has already connected to us.
            if (errorCode == Interop.mincore.Errors.ERROR_PIPE_CONNECTED)
            {
                errorCode = 0;
            }

            _errorCode = (int)errorCode;

            int resultState = errorCode == 0 ? ResultSuccess : ResultError;

            // Store the result so that other threads can observe it
            // and if no other thread is registering cancellation, continue.
            // Otherwise CompleteCallback(resultState) will be invoked by RegisterForCancellation().
            if (Interlocked.Exchange(ref _state, resultState) == NoResult)
            {
                // Now try to prevent invocation of CompleteCallback(resultState) from RegisterForCancellation().
                // Otherwise, thread responsible for registering cancellation stole the result and it will invoke CompleteCallback(resultState).
                if (Interlocked.Exchange(ref _state, CompletedCallback) != NoResult)
                {
                    CompleteCallback(resultState);
                }
            }
        }

        /// <summary>
        /// Cancellation is not guaranteed to succeed.
        /// We ignore all errors here because operation could
        /// succeed just before it was called or someone already
        /// cancelled this operation without using token which should
        /// be manually detected - when operation finishes we should
        /// compare error code to ERROR_OPERATION_ABORTED and if cancellation
        /// token was not used to cancel we will throw.
        /// </summary>
        private void Cancel()
        {
            // Storing to locals to avoid data races
            SafeHandle handle = _threadPoolBinding.Handle;
            NativeOverlapped* overlapped = Overlapped;

            Debug.Assert(overlapped != null && !Task.IsCompleted, "IO should not have completed yet");

            // If the handle is still valid, attempt to cancel the IO
            if (!handle.IsInvalid)
            {
                if (!Interop.mincore.CancelIoEx(handle, overlapped))
                {
                    // This case should not have any consequences although
                    // it will be easier to debug if there exists any special case
                    // we are not aware of.
                    int errorCode = Marshal.GetLastWin32Error();
                    Debug.WriteLine("CancelIoEx finished with error code {0}.", errorCode);
                }
            }
        }

        private void CompleteCallback(int resultState)
        {
            Debug.Assert(resultState == ResultSuccess || resultState == ResultError, "Unexpected result state " + resultState);

            ReleaseResources();

            if (resultState == ResultError)
            {
                if (_errorCode == Interop.mincore.Errors.ERROR_OPERATION_ABORTED)
                {
                    if (_cancellationToken.CanBeCanceled && !_cancellationToken.IsCancellationRequested)
                    {
                        // If this is unexpected abortion
                        TrySetException(__Error.GetOperationAborted());
                    }
                    else
                    {
                        // otherwise set canceled
                        TrySetCanceled(_cancellationToken);
                    }
                }
                else
                {
                    TrySetException(Win32Marshal.GetExceptionForWin32Error(_errorCode));
                }
            }
            else
            {
                SetCompletedSynchronously();
            }
        }
    }

    internal struct VoidResult { }
}
