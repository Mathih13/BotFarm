using System;
using System.Collections.Concurrent;
using System.Threading;

namespace BotFarm
{
    /// <summary>
    /// Thread-safe pool of RemoteAccess connections to enable parallel account creation.
    /// Without pooling, a single RemoteAccess connection becomes a bottleneck that serializes
    /// all parallel test runs due to ~2+ seconds per SendCommand call.
    /// </summary>
    class RemoteAccessPool : IDisposable
    {
        private readonly ConcurrentBag<RemoteAccess> available = new ConcurrentBag<RemoteAccess>();
        private readonly SemaphoreSlim semaphore;
        private readonly string hostname;
        private readonly int port;
        private readonly string username;
        private readonly string password;
        private readonly int maxSize;
        private int currentSize;
        private bool disposed;

        /// <summary>
        /// Create a new RemoteAccess connection pool
        /// </summary>
        /// <param name="hostname">Server hostname</param>
        /// <param name="port">Remote access port</param>
        /// <param name="username">Admin username</param>
        /// <param name="password">Admin password</param>
        /// <param name="maxSize">Maximum number of connections in the pool (default: 4)</param>
        public RemoteAccessPool(string hostname, int port, string username, string password, int maxSize = 4)
        {
            this.hostname = hostname;
            this.port = port;
            this.username = username;
            this.password = password;
            this.maxSize = maxSize;
            this.semaphore = new SemaphoreSlim(maxSize, maxSize);
            this.currentSize = 0;
        }

        /// <summary>
        /// Get a connection from the pool. Blocks if all connections are in use.
        /// Creates new connections lazily up to maxSize.
        /// </summary>
        /// <returns>A RemoteAccess connection to use</returns>
        public RemoteAccess GetConnection()
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(RemoteAccessPool));

            // Wait for a slot to become available
            semaphore.Wait();

            // Try to get an existing connection from the bag
            if (available.TryTake(out RemoteAccess connection))
            {
                return connection;
            }

            // Create a new connection if we haven't hit the max
            lock (available)
            {
                int newSize = Interlocked.Increment(ref currentSize);
                if (newSize <= maxSize)
                {
                    var newConnection = new RemoteAccess(hostname, port, username, password);
                    if (!newConnection.Connect())
                    {
                        Console.WriteLine($"RemoteAccessPool: Failed to create connection #{newSize}");
                        // Still return it - SendCommand will try to reconnect
                    }
                    else
                    {
                        Console.WriteLine($"RemoteAccessPool: Created connection #{newSize}");
                    }
                    return newConnection;
                }

                // Over limit - decrement and throw
                Interlocked.Decrement(ref currentSize);
                throw new InvalidOperationException("Pool exhausted - this should not happen");
            }
        }

        /// <summary>
        /// Return a connection to the pool for reuse
        /// </summary>
        /// <param name="connection">The connection to return</param>
        public void ReturnConnection(RemoteAccess connection)
        {
            if (connection == null)
                return;

            if (disposed)
            {
                // Pool is disposed, dispose the connection instead of returning it
                connection.Dispose();
                return;
            }

            available.Add(connection);
            semaphore.Release();
        }

        /// <summary>
        /// Dispose all connections in the pool
        /// </summary>
        public void Dispose()
        {
            if (disposed)
                return;

            disposed = true;

            // Dispose all connections in the bag
            while (available.TryTake(out RemoteAccess connection))
            {
                try
                {
                    connection.Dispose();
                }
                catch
                {
                    // Ignore disposal errors
                }
            }

            semaphore.Dispose();
        }
    }
}
