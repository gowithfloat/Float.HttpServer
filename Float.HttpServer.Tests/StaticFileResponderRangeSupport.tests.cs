using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Float.TinCan.ActivityLibrary;
using Xunit;

namespace Float.HttpServer.Tests
{
    public class StaticFileResponderRangeSupport
    {
        readonly string file;
        readonly string path;
        readonly HttpClient client = new HttpClient();
        readonly LocalHttpServer server;
        readonly long contentLength;

        public StaticFileResponderRangeSupport()
        {
            var directory = Directory.GetCurrentDirectory();

            file = Path.GetRandomFileName();
            path = Path.Combine(directory, file);
            using (StreamWriter sw = File.CreateText(path))
            {
                sw.Write("Hello World");
            }

            using (Stream input = new FileStream(path, FileMode.Open, FileAccess.Read))
            {
                contentLength = input.Length;
            }

            var port = PortSelector.SelectForAddress("127.0.0.1", 56555);
            server = new LocalHttpServer("127.0.0.1", port);
            server.SetDefaultResponder(new StaticFileResponder(directory));
            server.Start();
        }

        ~StaticFileResponderRangeSupport()
        {
            server.Stop();
            File.Delete(path);
        }

        [Fact]
        public async Task CheckRangeRequestResponse()
        {
            var request = new HttpRequestMessage
            {
                RequestUri = new Uri($"http://{server.Host}:{server.Port}/{file}")
            };
            request.Headers.Range = new RangeHeaderValue(0, 4);
            var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
            Assert.Equal("bytes", response.Headers.AcceptRanges.FirstOrDefault());
            Assert.Equal($"bytes 0-4/{contentLength}", response.Content.Headers.ContentRange.ToString());
            Assert.Equal(5, response.Content.Headers.ContentLength);

            var body = await response.Content.ReadAsStringAsync();
            Assert.Equal("Hello", body);
        }

        [Fact]
        public async Task CheckInvalidStartRangeRequestResponse()
        {
            var request = new HttpRequestMessage
            {
                RequestUri = new Uri($"http://{server.Host}:{server.Port}/{file}")
            };
            request.Headers.Range = new RangeHeaderValue(100, 101);
            var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.RequestedRangeNotSatisfiable, response.StatusCode);
            Assert.Equal($"bytes */{contentLength}", response.Content.Headers.ContentRange?.ToString());
        }

        [Fact]
        public async Task CheckInvalidEndRangeRequestResponse()
        {
            var request = new HttpRequestMessage
            {
                RequestUri = new Uri($"http://{server.Host}:{server.Port}/{file}")
            };
            request.Headers.Range = new RangeHeaderValue(0, 100);
            var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.RequestedRangeNotSatisfiable, response.StatusCode);
            Assert.Equal($"bytes */{contentLength}", response.Content.Headers.ContentRange?.ToString());
        }

        [Fact]
        public async Task CheckWholeRangeRequestResponse()
        {
            var request = new HttpRequestMessage
            {
                RequestUri = new Uri($"http://{server.Host}:{server.Port}/{file}")
            };
            request.Headers.Range = new RangeHeaderValue(0, null);
            var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("bytes", response.Headers.AcceptRanges.FirstOrDefault());
            Assert.Equal(contentLength, response.Content.Headers.ContentLength);

            var body = await response.Content.ReadAsStringAsync();
            Assert.Equal("Hello World", body);
        }

        [Fact]
        public async Task CheckStartOnlyRangeRequestResponse()
        {
            var request = new HttpRequestMessage
            {
                RequestUri = new Uri($"http://{server.Host}:{server.Port}/{file}")
            };
            request.Headers.Range = new RangeHeaderValue(6, null);
            var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
            Assert.Equal("bytes", response.Headers.AcceptRanges.FirstOrDefault());
            Assert.Equal($"bytes 6-10/{contentLength}", response.Content.Headers.ContentRange.ToString());
            Assert.Equal(5, response.Content.Headers.ContentLength);

            var body = await response.Content.ReadAsStringAsync();
            Assert.Equal("World", body);
        }

        [Fact]
        public async Task CheckEndOnlyRangeRequestResponse()
        {
            var request = new HttpRequestMessage
            {
                RequestUri = new Uri($"http://{server.Host}:{server.Port}/{file}")
            };
            request.Headers.Range = new RangeHeaderValue(null, 5);
            var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
            Assert.Equal("bytes", response.Headers.AcceptRanges.FirstOrDefault());
            Assert.Equal($"bytes 6-10/{contentLength}", response.Content.Headers.ContentRange.ToString());
            Assert.Equal(5, response.Content.Headers.ContentLength);

            var body = await response.Content.ReadAsStringAsync();
            Assert.Equal("World", body);
        }

        [Fact]
        public async Task CheckMultipleRangeRequestResponse()
        {
            var request = new HttpRequestMessage
            {
                RequestUri = new Uri($"http://{server.Host}:{server.Port}/{file}")
            };
            var range = new RangeHeaderValue();
            range.Ranges.Add(new RangeItemHeaderValue(0, 1));
            range.Ranges.Add(new RangeItemHeaderValue(2, 3));
            range.Ranges.Add(new RangeItemHeaderValue(3, 4));
            request.Headers.Range = range;
            var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);
        }
    }
}
