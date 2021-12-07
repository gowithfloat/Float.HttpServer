using System;
using System.Net;
using System.Threading;

namespace Float.HttpServer
{
    /// <summary>
    /// HttpParser parses requests and to hand off to router.
    /// </summary>
    public class HttpRequestHandler
    {
        readonly HttpRouter router;

        /// <summary>
        /// Initializes a new instance of the <see cref="HttpRequestHandler"/> class.
        /// </summary>
        /// <param name="router">The router.</param>
        public HttpRequestHandler(HttpRouter router)
        {
            this.router = router ?? throw new ArgumentNullException(nameof(router));
        }

        /// <summary>
        /// Gets or sets the cancellation token from the server.
        /// </summary>
        /// <value>
        /// A cancellation token from the server to determine if the request should be stopped.
        /// </value>
        public CancellationToken CancellationToken { get; set; }

        /// <summary>
        /// Handles incoming requests from server and passes them to the router
        /// for processing.
        /// </summary>
        /// <param name="result">The asynchronous result context of the request.</param>
        public void HandleRequest(IAsyncResult result)
        {
            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            if (result.AsyncState is not HttpListener listener)
            {
                return;
            }

            // If the cancellation token has been cancelled it is likely the
            // listener has been disposed (meaning we cannot close the context anyways).
            if (CancellationToken.IsCancellationRequested)
            {
                return;
            }

            // Call EndGetContext to complete the asynchronous operation.
            var context = listener.EndGetContext(result);
            var request = context.Request;
            var response = context.Response;

            try
            {
                router.ProcessResponseMiddleware(request, ref response);
            }
#pragma warning disable CA1031 // Do not catch general exception types
            catch (Exception e)
#pragma warning restore CA1031 // Do not catch general exception types
            {
                System.Diagnostics.Debug.WriteLine(e);

                // If we do not have a 4xx/5xx error here, update the status code.
                // Otherwise assume the handler threw the appropriate error code.
                response.StatusCode = response.StatusCode < (int)HttpStatusCode.BadRequest ?
                    (int)HttpStatusCode.InternalServerError : response.StatusCode;
                return;
            }
            finally
            {
                // always close the stream
                response.OutputStream.Close();
            }
        }
    }
}
