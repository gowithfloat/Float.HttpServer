using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace Float.HttpServer.Tests
{
    public class HttpRouterTests
    {
        readonly HttpClient client = new HttpClient();

        [Fact]
        public async Task TestAddResponseMiddleware()
        {
            var port = PortSelector.SelectForAddress("127.0.0.1", 55555);
            var server = new LocalHttpServer("127.0.0.1", port);
            server.Get("/1234", new TestResponder());
            server.Start();
            var response = await GetResponse($"http://{server.Host}:{server.Port}/1234");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            server.Use(
            new List<HttpRouter.HttpMethod> { HttpRouter.HttpMethod.GET },
            (HttpListenerRequest request, ref HttpListenerResponse response) =>
            {
                response.StatusCode = (int)HttpStatusCode.NotFound;
                return true;
            });
            response = await GetResponse($"http://{server.Host}:{server.Port}/1234");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            server.Use(
            new List<HttpRouter.HttpMethod> { HttpRouter.HttpMethod.GET },
            (HttpListenerRequest request, ref HttpListenerResponse response) =>
            {
                response.StatusCode = (int)HttpStatusCode.NotFound;
                return false;
            });

            response = await GetResponse($"http://{server.Host}:{server.Port}/1234");
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            server.Dispose();
        }

        [Fact]
        public async Task TestAddResponser()
        {
            var port = PortSelector.SelectForAddress("127.0.0.1", 55555);
            var server = new LocalHttpServer("127.0.0.1", port);
            server.Get("/1234", new TestResponder());
            server.Start();

            var response = await GetResponse($"http://{server.Host}:{server.Port}/1234");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            response = await GetResponse($"http://{server.Host}:{server.Port}/1234/5678");
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

            server.Get("/1234/:id", new TestResponder());
            response = await GetResponse($"http://{server.Host}:{server.Port}/1234/5678");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            server.Dispose();
        }

        [Fact]
        public async Task TestSetDefaultResponder()
        {
            var port = PortSelector.SelectForAddress("127.0.0.1", 55555);
            var server = new LocalHttpServer("127.0.0.1", port);
            server.SetDefaultResponder(new TestResponder());
            server.Start();

            var response = await GetResponse($"http://{server.Host}:{server.Port}/1234");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            response = await GetResponse($"http://{server.Host}:{server.Port}/12345678");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            server.Dispose();
        }

        [Fact]
        public async Task TestSetErrorResponder()
        {
            var port = PortSelector.SelectForAddress("127.0.0.1", 56000);
            var server = new LocalHttpServer("127.0.0.1", port);
            // Check error responder.
            server.SetErrorResponder(new ErrorResponder404());
            server.Start();
            var response = await GetResponse($"http://{server.Host}:{server.Port}/1234");
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            server.Dispose();
        }

        private async Task<HttpResponseMessage> GetResponse(string url)
        {
            var request = new HttpRequestMessage
            {
                RequestUri = new Uri(url)
            };

            return await client.SendAsync(request);
        }

        class TestResponder : IHttpResponder
        {
            public void GenerateResponse(in HttpListenerRequest httpRequest, ref HttpListenerResponse httpResponse, IDictionary<string, string> parameters)
            {
                httpResponse.StatusCode = (int)HttpStatusCode.OK;
            }
        }
    }
}
