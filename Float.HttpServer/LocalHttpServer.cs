using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Net;
using System.Threading;
using static Float.HttpServer.HttpRouter;

namespace Float.HttpServer
{
    /// <summary>
    /// A simple http server.
    /// </summary>
    public class LocalHttpServer : IDisposable
    {
        readonly AutoResetEvent startEvent = new (false);
        readonly AutoResetEvent stopEvent = new (false);
        readonly HttpListener listener = new ();
        readonly HttpRequestHandler httpRequestHandler;
        readonly HttpRouter router;
        Thread serverThread;
        bool disposed;
        CancellationTokenSource cancellationTokenSource;

        /// <summary>
        /// Initializes a new instance of the <see cref="LocalHttpServer"/> class.
        /// Construct server with given port.
        /// </summary>
        /// <param name="host">Hostname of the server.</param>
        /// <param name="port">Port of the server.</param>
        public LocalHttpServer(string host, ushort port)
        {
            Contract.Requires(!string.IsNullOrEmpty(host), nameof(host));
            Contract.Requires(port >= 49152 && port <= 65535, nameof(port));
            router = new HttpRouter();
            httpRequestHandler = new HttpRequestHandler(router);
            Host = host;
            Port = port;
            AddListenerPrefix();
            SetupServerThread();
        }

        /// <summary>
        /// Gets the host name.
        /// </summary>
        /// <value>
        /// The host name for the http server.
        /// </value>
        public string Host { get; }

        /// <summary>
        /// Gets and sets the port number.
        /// </summary>
        /// <value>
        /// The port number for the http server.
        /// </value>
        public ushort Port
        {
            get;
            private set;
        }

        /// <summary>
        /// Start server.
        /// </summary>
        public void Start()
        {
            // IsAlive:
            // true if this thread has been started and has not terminated normally or aborted; otherwise, false.
            // We should also check the thread state... if it is not one we'd expect
            // we should just restart.
            if (serverThread != null && serverThread.IsAlive &&
                (serverThread.ThreadState == ThreadState.Running ||
                serverThread.ThreadState == ThreadState.WaitSleepJoin))
            {
                return;
            }

            // Ensure the listener is stopped because the new server thread
            // will start it.
            StopListener();
            SetupServerThread();

            serverThread.Start();

            // Wait until the thread starts.
            startEvent.WaitOne();
        }

        /// <summary>
        /// Stop server.
        /// </summary>
        public void Stop()
        {
            try
            {
                // If the cancellation token is empty the server is probably stopped already.
                if (cancellationTokenSource == null || cancellationTokenSource.Token.IsCancellationRequested)
                {
                    return;
                }

                // Tell the server thread to stop and exit.
                cancellationTokenSource.Cancel();

                // Let's wait for the stoppage so we don't over stop.
                stopEvent.WaitOne();
            }
            catch (ObjectDisposedException)
            {
                // We've already cancelled and thrown out the token source.
                // Just leave already 😢.
            }
        }

        /// <summary>
        /// Restarts the default server.
        /// </summary>
        /// <param name="port">The port number to which to reset the server.</param>
        public void Restart(ushort port)
        {
            // Fire off the cancel event.
            Stop();

            // Change the port.
            Port = port;

            // Reset the port for the listener.
            AddListenerPrefix();

            // Start a new thread so we can start again.
            SetupServerThread();
            Start();
        }

        /// <summary>
        /// Checks status of server.
        /// </summary>
        /// <returns><c>True</c> if server is available.</returns>
        public bool IsServerAvailable()
        {
            var isServerThreadAvailable = serverThread != null &&
                serverThread.IsAlive &&
                (serverThread.ThreadState == ThreadState.Running ||
                serverThread.ThreadState == ThreadState.WaitSleepJoin);
            return isServerThreadAvailable && listener != null && listener.IsListening;
        }

        /// <summary>
        /// Register a middleware which triggers on a some methods with a specific path prefix.
        /// </summary>
        /// <param name="methods">The method to which to add the middleware.</param>
        /// <param name="path">The path to which to respond.</param>
        /// <param name="responder">The responder to generate the response.</param>
        public void AddResponser(IEnumerable<HttpMethod> methods, string path, IHttpResponder responder)
        {
            router.AddResponser(methods, path, responder);
        }

        /// <summary>
        /// Register a middleware which triggers on a `GET` with a specific path prefix.
        /// </summary>
        /// <param name="path">The path to which to respond.</param>
        /// <param name="responder">The responder to generate the response.</param>
        public void Get(string path, IHttpResponder responder)
        {
            router.AddResponser(new List<HttpMethod> { HttpMethod.GET }, path, responder);
        }

        /// <summary>
        /// Register a middleware which triggers on a `POST` with a specific path prefix.
        /// </summary>
        /// <param name="path">The path to which to respond.</param>
        /// <param name="responder">The responder to generate the response.</param>
        public void Post(string path, IHttpResponder responder)
        {
            router.AddResponser(new List<HttpMethod> { HttpMethod.POST }, path, responder);
        }

        /// <summary>
        /// Adds middleware to response.
        /// </summary>
        /// <param name="methods">The methods to which to add the middleware.</param>
        /// <param name="func">The function to change the response.</param>
        public void Use(IEnumerable<HttpMethod> methods, RefFunc<HttpListenerRequest, HttpListenerResponse> func)
        {
            router.AddResponseMiddleware(methods, func);
        }

        /// <summary>
        /// Sets the default route responder.
        /// </summary>
        /// <param name="responder">The default route responder.</param>
        public void SetDefaultResponder(IHttpResponder responder)
        {
            router.SetDefaultResponder(responder);
        }

        /// <summary>
        /// Sets the default error responder.
        /// </summary>
        /// <param name="responder">The error route responder.</param>
        public void SetErrorResponder(IErrorResponder responder)
        {
            router.SetErrorResponder(responder);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Release all managed and unmanaged resources.
        /// </summary>
        /// <param name="disposing">If we called disposed.</param>
        protected virtual void Dispose(bool disposing)
        {
            // Check to see if Dispose has already been called.
            if (disposed)
            {
                return;
            }

            if (disposing)
            {
                Stop();
                listener.Close();
                cancellationTokenSource.Dispose();
                serverThread = null;
                startEvent.Dispose();
                stopEvent.Dispose();
            }

            disposed = true;
        }

        void Listen()
        {
            cancellationTokenSource = new CancellationTokenSource();
            cancellationTokenSource.Token.Register(() =>
            {
                System.Diagnostics.Debug.WriteLine("Server is stopping");
                StopListener();
            });

            StartListener();
            startEvent.Set();

            // Try here in case the token or listener is disposed.
            try
            {
                while (cancellationTokenSource != null && !cancellationTokenSource.Token.IsCancellationRequested)
                {
                    httpRequestHandler.CancellationToken = cancellationTokenSource.Token;
                    if (cancellationTokenSource.Token.IsCancellationRequested)
                    {
                        break;
                    }

                    var result = listener.BeginGetContext(httpRequestHandler.HandleRequest, listener);
                    result.AsyncWaitHandle.WaitOne();
                }
            }
            catch (ObjectDisposedException)
            {
                // We've already cancelled and thrown out the token source.
                // Just leave already 😢.
                return;
            }
#pragma warning disable CA1031 // Do not catch general exception types
            catch (Exception e)
#pragma warning restore CA1031 // Do not catch general exception types
            {
                System.Diagnostics.Debug.WriteLine(e);
            }
            finally
            {
                if (!disposed)
                {
                    cancellationTokenSource.Dispose();
                }

                stopEvent.Set();
            }
        }

        void SetupServerThread()
        {
            serverThread = new Thread(Listen);
        }

        void AddListenerPrefix()
        {
            listener.Prefixes.Clear();
            listener.Prefixes.Add($"http://{Host}:{Port}/");
        }

        void StartListener()
        {
            try
            {
                listener.Start();
            }
#pragma warning disable CA1031 // Do not catch general exception types
            catch (Exception e)
#pragma warning restore CA1031 // Do not catch general exception types
            {
                System.Diagnostics.Debug.WriteLine("Listener start - " + e);
                return;
            }
        }

        void StopListener()
        {
            if (listener != null && listener.IsListening)
            {
                try
                {
                    listener.Stop();
                }
                catch
                {
                }
            }
        }
    }
}
