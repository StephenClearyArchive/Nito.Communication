﻿namespace Nito.Communication
{
    using System;
    using System.Net;
    using System.Net.Sockets;

    using Async;

    public sealed class BeginEndAsyncServerTcpSocket : IAsyncServerTcpSocket
    {
        private readonly IAsyncDelegateScheduler scheduler;
        private readonly Socket socket;

        public BeginEndAsyncServerTcpSocket(IAsyncDelegateScheduler scheduler)
        {
            this.scheduler = scheduler;
            this.socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        }

        public EndPoint LocalEndPoint
        {
            get { return this.socket.LocalEndPoint; }
        }

        public void Bind(EndPoint bindTo, int backlog)
        {
            this.socket.Bind(bindTo);
            this.socket.Listen(backlog);
        }

        public void Dispose()
        {
            this.AcceptCompleted = null;
            this.socket.Close();
        }

        public void AcceptAsync()
        {
            this.socket.BeginAccept(asyncResult =>
            {
                try
                {
                    var socket = this.socket.EndAccept(asyncResult);
                    this.scheduler.Schedule(() => this.OnAcceptComplete(null, new BeginEndAsyncServerChildTcpSocket(this.scheduler, socket)));
                }
                catch (Exception ex)
                {
                    this.scheduler.Schedule(() => this.OnAcceptComplete(ex, null));
                }
            }, null);
        }

        private void OnAcceptComplete(Exception ex, IAsyncTcpConnection result)
        {
            if (this.AcceptCompleted != null)
            {
                this.AcceptCompleted(ex == null ? new AsyncResultEventArgs<IAsyncTcpConnection>(result) : new AsyncResultEventArgs<IAsyncTcpConnection>(ex));
            }
        }

        public event Action<AsyncResultEventArgs<IAsyncTcpConnection>> AcceptCompleted;
    }
}
