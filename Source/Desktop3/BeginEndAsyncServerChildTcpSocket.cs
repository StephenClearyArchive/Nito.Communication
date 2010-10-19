namespace Nito.Communication
{
    using System.Collections.Generic;
    using System;
    using System.ComponentModel;
    using System.Net;
    using System.Net.Sockets;

    using Async;

    public sealed class BeginEndAsyncServerChildTcpSocket : IAsyncTcpConnection
    {
        /// <summary>
        /// The delegate scheduler used to synchronize callbacks.
        /// </summary>
        private readonly IAsyncDelegateScheduler scheduler;

        private readonly Socket socket;

        /// <summary>
        /// The state machine for the socket.
        /// </summary>
        private readonly SocketStateMachine state;

        internal BeginEndAsyncServerChildTcpSocket(IAsyncDelegateScheduler scheduler, Socket socket)
        {
            this.scheduler = scheduler;
            this.socket = socket;
            this.state = new SocketStateMachine();
        }

        public EndPoint LocalEndPoint
        {
            get { return this.socket.LocalEndPoint; }
        }

        public EndPoint RemoteEndPoint
        {
            get { return this.socket.RemoteEndPoint; }
        }

#if !COMPACT
        public bool NoDelay
        {
            get { return this.socket.NoDelay; }
            set { this.socket.NoDelay = value; }
        }
#endif

#if !COMPACT
        public LingerOption LingerState
        {
            get { return this.socket.LingerState; }
            set { this.socket.LingerState = value; }
        }
#endif

        public void ReadAsync(byte[] buffer, int offset, int size)
        {
            this.state.Read();
            this.socket.BeginReceive(
                buffer,
                offset,
                size,
                SocketFlags.None,
                asyncResult =>
                {
                    try
                    {
                        var result = this.socket.EndReceive(asyncResult);
                        this.scheduler.Schedule(() => this.OnReadComplete(null, result));
                    }
                    catch (Exception ex)
                    {
                        this.scheduler.Schedule(() => this.OnReadComplete(ex, 0));
                    }
                },
                null);
        }

#if !COMPACT
        public void ReadAsync(IList<ArraySegment<byte>> buffers)
        {
            this.state.Read();
            this.socket.BeginReceive(
                buffers,
                SocketFlags.None,
                asyncResult =>
                {
                    try
                    {
                        var result = this.socket.EndReceive(asyncResult);
                        this.scheduler.Schedule(() => this.OnReadComplete(null, result));
                    }
                    catch (Exception ex)
                    {
                        this.scheduler.Schedule(() => this.OnReadComplete(ex, 0));
                    }
                },
                null);
        }
#endif

        public void WriteAsync(byte[] buffer, int offset, int size, object state)
        {
            if (this.state.Write(new WriteRequest(buffer, offset, size, state)))
            {
                this.Write(buffer, offset, size, state);
            }
        }

#if !COMPACT
        public void WriteAsync(IList<ArraySegment<byte>> buffers, object state)
        {
            if (this.state.Write(new WriteRequest(buffers, state)))
            {
                this.Write(buffers, state);
            }
        }
#endif

#if !COMPACT
        public void ShutdownAsync()
        {
            this.state.Close();
            this.ReadCompleted = null;
            this.WriteCompleted = null;
            this.socket.BeginDisconnect(false, asyncResult =>
            {
                try
                {
                    this.socket.EndDisconnect(asyncResult);
                    this.scheduler.Schedule(() => this.OnShutdownComplete(null));
                }
                catch (Exception ex)
                {
                    this.scheduler.Schedule(() => this.OnShutdownComplete(ex));
                }
            }, null);
        }
#endif

        public void Dispose()
        {
            this.ReadCompleted = null;
            this.WriteCompleted = null;
#if !COMPACT
            this.ShutdownCompleted = null;
#endif
            this.state.Close();
            this.socket.Close();
        }

        private void OnReadComplete(Exception ex, int result)
        {
            this.state.ReadComplete();
            if (this.ReadCompleted != null)
            {
                this.ReadCompleted(ex == null ? new AsyncResultEventArgs<int>(result) : new AsyncResultEventArgs<int>(ex));
            }
        }

        private void OnWriteComplete(object state, Exception ex)
        {
            this.ContinueWriting();
            if (this.WriteCompleted != null)
            {
                this.WriteCompleted(new AsyncCompletedEventArgs(ex, false, state));
            }
        }

#if !COMPACT
        private void OnShutdownComplete(Exception ex)
        {
            if (this.ShutdownCompleted != null)
            {
                this.ShutdownCompleted(new AsyncCompletedEventArgs(ex, false, null));
            }
        }
#endif

        private void Write(byte[] buffer, int offset, int size, object state)
        {
            this.socket.BeginSend(
                buffer,
                offset,
                size,
                SocketFlags.None,
                asyncResult =>
                {
                    try
                    {
                        var result = this.socket.EndSend(asyncResult);
                        if (result < size)
                        {
                            this.scheduler.Schedule(
                                () =>
                                {
                                    try
                                    {
                                        this.Write(buffer, offset + result, size - result, state);
                                    }
                                    catch (Exception ex)
                                    {
                                        this.OnWriteComplete(state, ex);
                                    }
                                });
                        }
                        else
                        {
                            this.scheduler.Schedule(() => this.OnWriteComplete(state, null));
                        }
                    }
                    catch (Exception ex)
                    {
                        this.scheduler.Schedule(() => this.OnWriteComplete(state, ex));
                    }
                },
                state);
        }

#if !COMPACT
        private void Write(IList<ArraySegment<byte>> buffers, object state)
        {
            this.socket.BeginSend(
                buffers,
                SocketFlags.None,
                asyncResult =>
                {
                    try
                    {
                        var result = this.socket.EndSend(asyncResult);
                        var remainingBuffers = SocketHelpers.RemainingBuffers(buffers, result);
                        if (remainingBuffers.Count != 0)
                        {
                            this.scheduler.Schedule(
                                () =>
                                {
                                    try
                                    {
                                        this.Write(remainingBuffers, state);
                                    }
                                    catch (Exception ex)
                                    {
                                        this.OnWriteComplete(state, ex);
                                    }
                                });
                        }
                        else
                        {
                            this.scheduler.Schedule(() => this.OnWriteComplete(state));
                        }
                    }
                    catch (Exception ex)
                    {
                        this.scheduler.Schedule(() => this.OnWriteComplete(state, ex));
                    }
                },
                state);
        }
#endif

        private void ContinueWriting()
        {
            var nextWrite = this.state.WriteComplete();
            if (nextWrite == null)
            {
                return;
            }

#if !COMPACT
            if (nextWrite.Buffers != null)
            {
                this.Write(nextWrite.Buffers, nextWrite.State);
            }
            else
#endif
            {
                this.Write(nextWrite.Buffer, nextWrite.Offset, nextWrite.Size, nextWrite.State);
            }
        }

        public event Action<AsyncResultEventArgs<int>> ReadCompleted;

        public event Action<AsyncCompletedEventArgs> WriteCompleted;

#if !COMPACT
        public event Action<AsyncCompletedEventArgs> ShutdownCompleted;
#endif
    }
}
