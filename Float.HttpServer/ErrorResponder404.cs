using System;
using System.Net;
using System.Text;

namespace Float.HttpServer
{
    /// <summary>
    /// Generates a 404 response with hidden error message.
    /// </summary>
    public class ErrorResponder404 : IErrorResponder
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ErrorResponder404"/> class.
        /// </summary>
        public ErrorResponder404()
        {
        }

        /// <inheritdoc/>
        public virtual void GenerateErrorResponse(in HttpListenerRequest httpRequest, ref HttpListenerResponse httpResponse, Exception e)
        {
            if (httpResponse == null || e == null || httpResponse.OutputStream == null)
            {
                return;
            }

            httpResponse.StatusCode = (int)HttpStatusCode.NotFound;
            var errorPage = $"<html><body><div>Error: 404</div><div style=\"display:none;\">{e.Message}</div></body></html>";
            var errorContent = Encoding.UTF8.GetBytes(errorPage);
            httpResponse.ContentType = "text/html";
            httpResponse.ContentLength64 = errorContent.Length;
            httpResponse.OutputStream.Write(errorContent, 0, errorContent.Length);
        }
    }
}
