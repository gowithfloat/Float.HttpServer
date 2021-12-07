using System;
using System.Collections.Generic;
using System.Net;
using System.Text.RegularExpressions;

namespace Float.HttpServer
{
    /// <summary>
    /// HttpRouter hands requests to route handlers.
    /// </summary>
    public class HttpRouter
    {
        const string DynamicRouteMatchPattern = "(?:/:([A-Za-z]+)/|/:([A-Za-z])+$)";
        readonly IDictionary<HttpMethod, IList<RefFunc<HttpListenerRequest, HttpListenerResponse>>> responseMiddleware = new Dictionary<HttpMethod, IList<RefFunc<HttpListenerRequest, HttpListenerResponse>>>();
        readonly IDictionary<string, IDictionary<HttpMethod, IHttpResponder>> responders = new Dictionary<string, IDictionary<HttpMethod, IHttpResponder>>();
        readonly IDictionary<string, IDictionary<HttpMethod, DynamicRoute>> dynamicResponders = new Dictionary<string, IDictionary<HttpMethod, DynamicRoute>>();
        IHttpResponder defaultResponder;
        IErrorResponder errorResponder;

        /// <summary>
        /// Initializes a new instance of the <see cref="HttpRouter"/> class.
        /// </summary>
        public HttpRouter()
        {
        }

        /// <summary>
        /// Func definition to allow response to be passed by reference.
        /// </summary>
        /// <typeparam name="THttpListenerRequest">A HttpListenerRequest.</typeparam>
        /// <typeparam name="THttpListenerResponse">A HttpListenerResponse.</typeparam>
        /// <param name="request">The request.</param>
        /// <param name="response">The response.</param>
        /// <returns>Boolean <c>true</c> if the middleware should continue to process.</returns>
        public delegate bool RefFunc<in THttpListenerRequest, THttpListenerResponse>(HttpListenerRequest request, ref HttpListenerResponse response);

        /// <summary>
        /// List of possible Http methods for requests.
        /// </summary>
        public enum HttpMethod
        {
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
#pragma warning disable SA1602 // Enumeration items should be documented
            GET,
            PUT,
            ACL,
            HEAD,
            POST,
            COPY,
            LOCK,
            MOVE,
            BIND,
            LINK,
            PATCH,
            TRACE,
            MKCOL,
            MERGE,
            PURGE,
            NOTIFY,
            SEARCH,
            UNLOCK,
            REBIND,
            UNBIND,
            REPORT,
            DELETE,
            UNLINK,
            CONNECT,
            MSEARCH,
            OPTIONS,
            PROPFIND,
            CHECKOUT,
            PROPPATCH,
            SUBSCRIBE,
            MKCALENDAR,
            MKACTIVITY,
            UNSUBSCRIBE,
            SOURCE,
#pragma warning restore SA1602 // Enumeration items should be documented
#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
        }

        /// <summary>
        /// Adds middleware to response.
        /// </summary>
        /// <param name="methods">The methods to which to add the middleware.</param>
        /// <param name="func">The function to change the response.</param>
        public void AddResponseMiddleware(IEnumerable<HttpMethod> methods, RefFunc<HttpListenerRequest, HttpListenerResponse> func)
        {
            if (methods == null)
            {
                return;
            }

            foreach (var method in methods)
            {
                if (responseMiddleware.ContainsKey(method) == false)
                {
                    responseMiddleware[method] = new List<RefFunc<HttpListenerRequest, HttpListenerResponse>>();
                }

                var processesForMethod = responseMiddleware[method];
                processesForMethod.Add(func);
                responseMiddleware[method] = processesForMethod;
            }
        }

        /// <summary>
        /// Register a middleware which triggers on a some methods with a specific path prefix.
        /// </summary>
        /// <param name="methods">The method to which to add the middleware.</param>
        /// <param name="path">The path to which to respond.</param>
        /// <param name="responder">The responder to generate the response.</param>
        public void AddResponser(IEnumerable<HttpMethod> methods, string path, IHttpResponder responder)
        {
            if (methods == null || path == null)
            {
                return;
            }

            foreach (var method in methods)
            {
                if (Regex.IsMatch(path, DynamicRouteMatchPattern, RegexOptions.None))
                {
                    AddDynamicResponser(method, path, responder);
                    continue;
                }

                if (responders.ContainsKey(path) == false)
                {
                    responders[path] = new Dictionary<HttpMethod, IHttpResponder>();
                }

                responders[path].Add(method, responder);
            }
        }

        /// <summary>
        /// Adjusts response based on middleware.
        /// </summary>
        /// <param name="request">The request.</param>
        /// <param name="response">The response to which is adjusted.</param>
        /// <returns>The adjusted response.</returns>
        public HttpListenerResponse ProcessResponseMiddleware(HttpListenerRequest request, ref HttpListenerResponse response)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (response == null)
            {
                throw new ArgumentNullException(nameof(response));
            }

            var method = (HttpMethod)Enum.Parse(typeof(HttpMethod), request.HttpMethod);
            if (responseMiddleware.ContainsKey(method))
            {
                var actions = responseMiddleware[method];
                var i = 0;
                var shouldContinueProcessing = true;
                while (i < actions.Count && shouldContinueProcessing)
                {
                    shouldContinueProcessing = actions[i](request, ref response);
                    if (!shouldContinueProcessing)
                    {
                        return response;
                    }

                    i++;
                }
            }

            var (responder, parameters) = FindHttpResponder(request);

            try
            {
                if (responder == null)
                {
                    throw new Exception("Route not found");
                }

                responder.GenerateResponse(request, ref response, parameters);
            }
            catch (Exception e)
            {
                if (errorResponder != null)
                {
                    errorResponder.GenerateErrorResponse(request, ref response, e);
                    return response;
                }

                throw;
            }

            return response;
        }

        /// <summary>
        /// Sets the default route responder.
        /// </summary>
        /// <param name="responder">The default route responder.</param>
        public void SetDefaultResponder(IHttpResponder responder)
        {
            defaultResponder = responder;
        }

        /// <summary>
        /// Sets the default error responder.
        /// </summary>
        /// <param name="responder">The error route responder.</param>
        public void SetErrorResponder(IErrorResponder responder)
        {
            errorResponder = responder;
        }

        /// <summary>
        /// Finds the responder to which to generate the response based on a request.
        /// </summary>
        /// <param name="request">The request for which to find the responder.</param>
        /// <returns>An HttpResponder which can generate a request.</returns>
        (IHttpResponder, IDictionary<string, string>) FindHttpResponder(HttpListenerRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            var path = request.Url?.LocalPath ?? string.Empty;

            if (string.IsNullOrEmpty(path))
            {
                return (null, null);
            }

            var method = (HttpMethod)Enum.Parse(typeof(HttpMethod), request.HttpMethod);

            if (responders.ContainsKey(path))
            {
                return (responders[path][method], null);
            }

            // Alright that didn't work so let's parse all of the
            // responders with a dynamic parameter.
            foreach (var dynamicResponder in dynamicResponders)
            {
                var key = dynamicResponder.Key;
                var matches = Regex.Matches(path, key, RegexOptions.None);

                if (matches.Count != 0 && dynamicResponder.Value.ContainsKey(method))
                {
                    // We have a dynamic route! Make sure to inject the parameters for the responder.
                    var parameterNames = dynamicResponder.Value[method].ParameterNames;
                    var parameters = new Dictionary<string, string>();

                    for (int i = 0; i < parameterNames.Count; i++)
                    {
                        parameters.Add(parameterNames[i], matches[i].Groups[1].Value);
                    }

                    var responder = dynamicResponder.Value[method].Responder;
                    return (responder, parameters);
                }
            }

            return (defaultResponder, null);
        }

        void AddDynamicResponser(HttpMethod method, string path, IHttpResponder responder)
        {
            var parameters = new List<string>();
            var replacementPath = path.Split('/');

            for (var i = 0; i < replacementPath.Length; i++)
            {
                var partial = $"/{replacementPath[i]}/";
                var match = Regex.Match(partial, DynamicRouteMatchPattern, RegexOptions.None);

                if (match.Success)
                {
                    parameters.Add(match.Groups[1].Value);
                    replacementPath[i] = "([^/]+)";
                }
            }

            var regexPath = "^";
            regexPath += string.Join("/", replacementPath);
            regexPath += "$";

            if (dynamicResponders.ContainsKey(regexPath) == false)
            {
                var dynamicResponder = new Dictionary<HttpMethod, DynamicRoute>();
                dynamicResponders[regexPath] = dynamicResponder;
            }

            var route = new DynamicRoute(responder, parameters);
            dynamicResponders[regexPath].Add(method, route);
        }

        class DynamicRoute
        {
            public DynamicRoute(IHttpResponder responder, IList<string> parameterNames)
            {
                Responder = responder;
                ParameterNames = parameterNames;
            }

            /// <summary>
            /// Gets the responder for the dynamic route.
            /// </summary>
            public IHttpResponder Responder { get; }

            /// <summary>
            /// Gets the parameter keys for the route.
            /// </summary>
            public IList<string> ParameterNames { get; }
        }
    }
}
