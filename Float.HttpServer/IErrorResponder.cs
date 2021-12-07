using System;
using System.Net;

namespace Float.HttpServer
{
    /// <summary>
    /// Interface which genererates Http responses.
    /// </summary>
    public interface IErrorResponder
    {
        /// <summary>
        /// Allows the request to be altered for handlers down the line.
        /// </summary>
        /// <param name="httpRequest">A reference to the request.</param>
        /// <param name="httpResponse">A reference to the response.</param>
        /// <param name="e">The exception.</param>
        void GenerateErrorResponse(in HttpListenerRequest httpRequest, ref HttpListenerResponse httpResponse, Exception e);
    }
}
