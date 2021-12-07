using System.Collections.Generic;
using System.Net;

namespace Float.HttpServer
{
    /// <summary>
    /// Interface which genererates Http responses.
    /// </summary>
    public interface IHttpResponder
    {
        /// <summary>
        /// Allows the request to be altered for handlers down the line.
        /// </summary>
        /// <param name="httpRequest">A reference to the request.</param>
        /// <param name="httpResponse">A reference to the response.</param>
        /// <param name="parameters">Optional parameters associated with the route.</param>
        void GenerateResponse(in HttpListenerRequest httpRequest, ref HttpListenerResponse httpResponse, IDictionary<string, string> parameters);
    }
}
