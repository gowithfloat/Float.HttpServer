using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace Float.HttpServer.Tests
{
    public class LocalHttpServerTests
    {
        [Fact]
        public void TestLifecycle()
        {
            var port = PortSelector.SelectForAddress("127.0.0.1");
            var server = new LocalHttpServer("127.0.0.1", port);
            Assert.False(server.IsServerAvailable());
            server.Start();
            Assert.True(server.IsServerAvailable());
            server.Restart(PortSelector.SelectForAddress("127.0.0.1"));
            Assert.True(server.IsServerAvailable());
            server.Stop();
            Assert.False(server.IsServerAvailable());
        }

        [Fact]
        public async Task TestRequest()
        {
            var port = PortSelector.SelectForAddress("127.0.0.1");
            var server = new LocalHttpServer("127.0.0.1", port);
            server.Get("/1234", new TestResponder());
            server.Start();

            using (var client = new HttpClient())
            {
                var request = new HttpRequestMessage
                {
                    RequestUri = new Uri($"http://{server.Host}:{server.Port}/1234")
                };

                var response = await client.SendAsync(request);
                Assert.Equal(418, (int)response.StatusCode);

                var request2 = new HttpRequestMessage
                {
                    RequestUri = new Uri($"http://{server.Host}:{server.Port}/12345")
                };

                var response2 = await client.SendAsync(request2);
                Assert.Equal(HttpStatusCode.InternalServerError, response2.StatusCode);
            }

            server.Stop();
        }

        [Fact]
        public void TestBrokenLifecycle()
        {
            var port = PortSelector.SelectForAddress("127.0.0.1");
            var server = new LocalHttpServer("127.0.0.1", port);
            server.Start();
            Assert.True(server.IsServerAvailable());
            server.Restart(port);
            Assert.True(server.IsServerAvailable());
            server.Restart(port);
            Assert.True(server.IsServerAvailable());
            server.Dispose();

            // FIXME: I should throw a disposed exception.
            Assert.False(server.IsServerAvailable());
        }

        [Fact]
        public void TestStartifecycle()
        {
            var port = PortSelector.SelectForAddress("127.0.0.1");
            var server = new LocalHttpServer("127.0.0.1", port);
            server.Start();
            server.Start();
            Assert.True(server.IsServerAvailable());
            server.Restart(port);
            Assert.True(server.IsServerAvailable());
            server.Restart(port);
            Assert.True(server.IsServerAvailable());
        }

        [Fact]
        public void TestStopLifecycle()
        {
            var port = PortSelector.SelectForAddress("127.0.0.1");
            var server = new LocalHttpServer("127.0.0.1", port);
            server.Stop();
            Assert.False(server.IsServerAvailable());
            server.Stop();
            Assert.False(server.IsServerAvailable());
            server.Start();
            Assert.True(server.IsServerAvailable());
            server.Stop();
            server.Restart(port);
            Assert.True(server.IsServerAvailable());
        }
    }

    class TestResponder : IHttpResponder
    {
        public void GenerateResponse(in HttpListenerRequest httpRequest, ref HttpListenerResponse httpResponse, IDictionary<string, string> parameters)
        {
            // I'm a little curious why they don't have this in the library...
            httpResponse.StatusCode = 418;
        }
    }
}
