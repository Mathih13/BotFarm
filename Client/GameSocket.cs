using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net.Sockets;
using System.Threading;
using Client.World.Network;

namespace Client
{
    public abstract class GameSocket : IDisposable
    {
        const int DefaultBufferSize = 128;
        protected readonly object _socketLock = new object();
        private volatile bool _asyncOperationInProgress;
        
        public GameSocket()
        {
            SocketArgs = new SocketAsyncEventArgs();
            SocketArgs.Completed += CallSocketCallback;
            _receiveData = new byte[DefaultBufferSize];
            ReceiveDataLength = DefaultBufferSize;
        }

        public IGame Game { get; protected set; }

        protected TcpClient connection { get; set; }

        public AuthenticationCrypto authenticationCrypto = new AuthenticationCrypto();

        public bool IsConnected
        {
            get { return connection.Connected; }
        }

        public bool Disposed
        {
            get;
            private set;
        }

        protected bool Disposing
        {
            get;
            private set;
        }

        #region Asynchronous Reading

        protected byte[] ReceiveData
        {
            get
            {
                return _receiveData;
            }
        }
        private byte[] _receiveData;
        protected int ReceiveDataLength;
        protected void ReserveData(int size, bool reset = false)
        {
            if (reset)
                _receiveData = new byte[DefaultBufferSize];
            if (_receiveData.Length < size)
                Array.Resize(ref _receiveData, size);
            ReceiveDataLength = size;
        }


        protected SocketAsyncEventArgs SocketArgs;
        protected object SocketAsyncState;
        protected EventHandler<SocketAsyncEventArgs> SocketCallback;
        private void CallSocketCallback(object sender, SocketAsyncEventArgs e)
        {
            _asyncOperationInProgress = false;
            if (SocketCallback != null)
                SocketCallback(sender, e);
        }

        protected void ReceiveAsync()
        {
            if (Disposing || Disposed)
                return;
            
            lock (_socketLock)
            {
                if (_asyncOperationInProgress)
                    return;
                    
                var client = connection?.Client;
                if (client == null)
                    return;
                    
                try
                {
                    _asyncOperationInProgress = true;
                    if (!client.ReceiveAsync(SocketArgs))
                    {
                        // Synchronous completion - queue callback to break recursion
                        // This prevents stack overflow with many concurrent sockets
                        ThreadPool.QueueUserWorkItem(_ =>
                        {
                            CallSocketCallback(this, SocketArgs);
                        });
                    }
                }
                catch (ObjectDisposedException)
                {
                    _asyncOperationInProgress = false;
                }
                catch (InvalidOperationException)
                {
                    // SocketAsyncEventArgs already in use - ignore
                    _asyncOperationInProgress = false;
                }
            }
        }

        public abstract void Start();

        #endregion

        public abstract bool Connect();

        public void Dispose()
        {
            Disposing = true;
            if (!Disposed)
                Disconnect();
        }

        public void Disconnect()
        {
            if (connection != null)
            {
                var client = connection.Client;
                if (client != null && connection.Connected && _receiveData != null)
                {
                    try
                    {
                        client.Shutdown(SocketShutdown.Send);
                        client.BeginReceive(_receiveData, 0, _receiveData.Length, SocketFlags.None, SocketShutdownCallback, null);
                    }
                    catch(SocketException)
                    {
                        Disposed = true;
                        connection.Close();
                    }
                    catch(ObjectDisposedException)
                    {
                        Disposed = true;
                    }
                    catch(ArgumentNullException)
                    {
                        // Can happen when called from finalizer
                        Disposed = true;
                        try { connection.Close(); } catch { }
                    }
                }
                else
                {
                    Disposed = true;
                    try { connection.Close(); } catch { }
                }
            }
        }

        void SocketShutdownCallback(IAsyncResult result)
        {
            try
            {
                var client = connection?.Client;
                if (client == null)
                {
                    Disposed = true;
                    return;
                }
                int size = client.EndReceive(result);
                if (size > 0)
                    client.BeginReceive(_receiveData, 0, _receiveData.Length, SocketFlags.None, SocketShutdownCallback, null);
                else
                {
                    Disposed = true;
                    connection?.Close();
                }
            }
            catch (ObjectDisposedException)
            {
                Disposed = true;
            }
            catch (SocketException)
            {
                Disposed = true;
            }
        }

        public abstract void InitHandlers();

        public abstract string LastInOpcodeName
        {
            get;
        }

        public abstract DateTime LastInOpcodeTime
        {
            get;
        }

        public abstract string LastOutOpcodeName
        {
            get;
        }

        public abstract DateTime LastOutOpcodeTime
        {
            get;
        }
    }
}
