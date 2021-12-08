using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace Float.HttpServer.Tests
{
    public class StaticFileResponderTests
    {
        readonly HttpClient client = new HttpClient();
        readonly string workingDirectory = Directory.GetCurrentDirectory();

        [Fact]
        public async Task CheckFileResponderTest()
        {
            var directory = Directory.GetCurrentDirectory();
            var path = Path.Combine(directory, "test.html");
            using (StreamWriter sw = File.CreateText(path))
            {
                sw.Write("Hello World");
            }

            long contentLength = 0;
            using (Stream input = new FileStream(path, FileMode.Open, FileAccess.Read))
            {
                contentLength = input.Length;
            }   

            var port = PortSelector.SelectForAddress("127.0.0.1", 56555);
            var server = new LocalHttpServer("127.0.0.1", port);
            server.Start();
            var response = await GetResponse($"http://{server.Host}:{server.Port}/test.html");
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

            server.SetDefaultResponder(new StaticFileResponder(directory));
            response = await GetResponse($"http://{server.Host}:{server.Port}/test.html");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var results = await response.Content.ReadAsStringAsync();
            Assert.Equal("Hello World", results);
            Assert.Equal(contentLength, response.Content.Headers.ContentLength);
            server.Stop();
            File.Delete(path);
        }

        [Fact]
        public async Task CheckFilenameEncodedTest()
        {
            var directory = Directory.GetCurrentDirectory();
            var path = Path.Combine(directory, "test%20.html");
            using (StreamWriter sw = File.CreateText(path))
            {
                sw.Write("Hello World!");
            }

            var port = PortSelector.SelectForAddress("127.0.0.1", 56555);
            var server = new LocalHttpServer("127.0.0.1", port);
            server.Start();
            var response = await GetResponse($"http://{server.Host}:{server.Port}/test%20.html");
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

            server.SetDefaultResponder(new StaticFileResponder(directory));
            response = await GetResponse($"http://{server.Host}:{server.Port}/test%20.html");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var results = await response.Content.ReadAsStringAsync();
            Assert.Equal("Hello World!", results);

            // Basically we are checking if HttpClient encodes the url for the request...
            response = await GetResponse($"http://{server.Host}:{server.Port}/test .html");
            results = await response.Content.ReadAsStringAsync();
            Assert.Equal("Hello World!", results);

            server.Stop();
            File.Delete(path);
        }

        [Fact]
        public async Task CheckFilenameSpaceTest()
        {
            var directory = Directory.GetCurrentDirectory();
            var path = Path.Combine(directory, "te st.html");
            using (StreamWriter sw = File.CreateText(path))
            {
                sw.Write("Hello World!!");
            }

            var port = PortSelector.SelectForAddress("127.0.0.1", 56555);
            var server = new LocalHttpServer("127.0.0.1", port);
            server.Start();
            var response = await GetResponse($"http://{server.Host}:{server.Port}/te%20st.html");
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

            server.SetDefaultResponder(new StaticFileResponder(directory));
            response = await GetResponse($"http://{server.Host}:{server.Port}/te%20st.html");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var results = await response.Content.ReadAsStringAsync();
            Assert.Equal("Hello World!!", results);

            response = await GetResponse($"http://{server.Host}:{server.Port}/te st.html");
            results = await response.Content.ReadAsStringAsync();
            Assert.Equal("Hello World!!", results);

            server.Stop();
            File.Delete(path);
        }

        [Fact]
        public async Task GenerateResponse_Headers_IncludeDates()
        {
            var now = DateTime.Now;
            using var file = new TestFile("test.txt");
            using var server = CreateServer();
            var response = await GetResponse($"http://{server.Host}:{server.Port}/{file.Name}");
            var responseDate = response.Headers.Date?.DateTime ?? DateTime.MinValue;
            var modifiedDate = response.Content?.Headers?.LastModified?.DateTime ?? DateTime.MinValue;

            Assert.Equal(now, responseDate, TimeSpan.FromSeconds(1));
            Assert.Equal(now, modifiedDate, TimeSpan.FromSeconds(1));
        }

        [Fact]
        public async Task GenerateResponse_Headers_IncludesAccessControlOrigin()
        {
            using var file = new TestFile("test.txt");
            using var server = CreateServer();
            var response = await GetResponse($"http://{server.Host}:{server.Port}/{file.Name}");
            var allowedOrigin = response.Headers.TryGetValues("Access-Control-Allow-Origin", out var value) ? value.FirstOrDefault() : null;

            Assert.Equal("*", allowedOrigin);
        }

        [Theory]
        [InlineData("test.txt", "text/plain")]
        [InlineData("test.jpg", "image/jpeg")]
        [InlineData("test.js", "application/x-javascript")]
        [InlineData("test", "application/octet-stream")]
        [InlineData("test.unknown", "application/octet-stream")]
        public async Task GenerateResponse_ContentTypeHeader_BasedOnFileExtension(string filename, string expectedContentType)
        {
            using var file = new TestFile(filename);
            using var server = CreateServer();
            var response = await GetResponse($"http://{server.Host}:{server.Port}/{file.Name}");

            Assert.Equal(expectedContentType, response.Content.Headers?.ContentType?.ToString());
        }

        [Fact]
        public async Task GenerateResponse_ContentTypeHeaderForExtendedFilePathCheckHeader_BasedOnFileExtensionBeforeQueryParams()
        {
            using var file = new TestFile("test.html?query");
            using var server = CreateServer();
            var headers = new Dictionary<string, string> { { StaticFileResponder.ExtendedFilePathCheckHeaderKey, "true" } };
            var response = await GetResponse($"http://{server.Host}:{server.Port}/{file.Name}", headers);

            Assert.Equal("text/html", response.Content.Headers?.ContentType?.ToString());
        }

        [Fact]
        public async Task GenerateResponse_NonExistentFile_ReturnsNotFound()
        {
            using var server = CreateServer();
            var response = await GetResponse($"http://{server.Host}:{server.Port}/nonexistant.txt");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public void FilePathWithoutAbsolute()
        {
            var rootDirectory = Directory.GetCurrentDirectory();
            var uri = new Uri("http://nopath.com");
            var filePath = StaticFileResponder.FilePathForRequest(rootDirectory, uri);

            Assert.Equal(rootDirectory, filePath);
        }

        [Theory]
        [InlineData("test.html", "test.html")]
        [InlineData("directory/test.html", "directory/test.html")]
        [InlineData("one/two/three.html", "one/two/three.html")]
        [InlineData("te st.html", "te%20st.html")]
        [InlineData("te st.html", "te st.html")]
        [InlineData("te%20st.html", "te%2520st.html")]
        [InlineData("te%20st.html", "te%20st.html")]
        [InlineData("test+file.html", "test+file.html")]
        [InlineData("test-file.html", "test-file.html")]
        [InlineData("test.file.html", "test.file.html")]
        [InlineData("test&file.html", "test&file.html")]
        [InlineData("test&file.html", "test%26file.html")]
        [InlineData("test?file.html", "test%3Ffile.html")]
        [InlineData("test#file.html", "test%23file.html")]
        [InlineData("test.html%3Ffoo=bar", "test.html%3Ffoo=bar")]
        [InlineData("test.html", "test.html?foo=bar")]
        [InlineData("test.html", "test.html#hello")]
        public void FilePathForRequest_FileNames_ResolveToAbsoluteFilePath(string filename, string request)
        {
            using var file = new TestFile(filename);
            var uri = new Uri($"http://example.com/{request}");
            var resolvedPath = StaticFileResponder.FilePathForRequest(workingDirectory, uri);

            Assert.Equal(file.Location, resolvedPath);
        }

        [Fact]
        public void FilePathForRequest_SimilarFileNames_PrefersPercentDecoded()
        {
            using var desiredFile = new TestFile("test file.txt");
            using var similarFile = new TestFile("test%20file.txt");
            var uri1 = new Uri($"http://example.com/test%20file.txt");
            var resolvedPath1 = StaticFileResponder.FilePathForRequest(workingDirectory, uri1);
            var uri2 = new Uri($"http://example.com/test file.txt");
            var resolvedPath2 = StaticFileResponder.FilePathForRequest(workingDirectory, uri2);

            Assert.Equal(desiredFile.Location, resolvedPath1);
            Assert.Equal(desiredFile.Location, resolvedPath2);
        }

        [Fact]
        public void FilePathForRequest_FileNameWithoutExtension_ResolvesToAbsolutePath()
        {
            var filePath = StaticFileResponder.FilePathForRequest(workingDirectory, new Uri("http://example.com/noextension"));

            Assert.Equal($"{workingDirectory}/noextension", filePath);
        }

        [Theory]
        [InlineData("test.html?foo=bar", "test.html?foo=bar")]
        [InlineData("te st.html?foo=bar", "te%20st.html?foo=bar")]
        [InlineData("te st.html?foo=bar", "te st.html?foo=bar")]
        [InlineData("test.html?foo=bar&bar=foo", "test.html?foo=bar&bar=foo")]
        public void FilePathForRequest_ExtendedFilePathCheckHeader_AllowsQueryParamsInFileName(string filename, string request)
        {
            using var file = new TestFile(filename);
            var uri = new Uri($"http://example.com/{request}");
            var headers = new NameValueCollection { { StaticFileResponder.ExtendedFilePathCheckHeaderKey, "true" } };
            var resolvedPath = StaticFileResponder.FilePathForRequest(workingDirectory, uri, headers);

            Assert.Equal(file.Location, resolvedPath);
        }

        [Fact]
        public void FilePathForRequest_ExtendedFilePathCheckHeader_PrefersFileWithQueryParams()
        {
            using var desiredFile = new TestFile("test.html?query=param");
            using var similarFile = new TestFile("test.html");
            var uri = new Uri($"http://example.com/{desiredFile.Name}");
            var headers = new NameValueCollection { { StaticFileResponder.ExtendedFilePathCheckHeaderKey, "true" } };
            var resolvedPath = StaticFileResponder.FilePathForRequest(workingDirectory, uri, headers);

            Assert.Equal(desiredFile.Location, resolvedPath);
        }

        [Theory]
        [InlineData("")]
        [InlineData("test")]
        [InlineData("test/test")]
        public void FilePathForRequest_Directories_ResolveToIndex(string pathToTest)
        {
            using var file = new TestFile(Path.Combine(pathToTest, "index.html"));
            var uri = new Uri($"http://example.com/{pathToTest}");
            var resolvedPath = StaticFileResponder.FilePathForRequest(workingDirectory, uri);

            Assert.Equal(file.Location, resolvedPath);
        }

        [Fact]
        public void FilePathForRequest_TrailingSlash_ResolvesToIndex()
        {
            using var file = new TestFile("index.html");
            var uri = new Uri($"http://example.com/");
            var resolvedPath = StaticFileResponder.FilePathForRequest(workingDirectory, uri);

            Assert.Equal(file.Location, resolvedPath);
        }

        LocalHttpServer CreateServer(string host = "127.0.0.1", ushort desiredPort = 56555)
        {
            var port = PortSelector.SelectForAddress(host, desiredPort);
            var server = new LocalHttpServer(host, port);
            server.Start();
            server.SetDefaultResponder(new StaticFileResponder(workingDirectory));
            return server;
        }

        async Task<HttpResponseMessage> GetResponse(string url, Dictionary<string, string> headers = null)
        {
            var request = new HttpRequestMessage
            {
                RequestUri = new Uri(url),
            };

            if (headers != null)
            {
                foreach (var header in headers)
                {
                    request.Headers.Add(header.Key, header.Value);
                }
            }

            return await client.SendAsync(request);
        }

        /// <summary>
        /// Convenience class for creating a file on disk that only exists for
        /// the duration of the scope where it's created.
        /// </summary>
        class TestFile : IDisposable
        {
            readonly string workingDirectory = Directory.GetCurrentDirectory();

            /// <summary>
            /// Initializes a new instance of the <see cref="TestFile"/> class.
            /// Creates a new file in the current working directory;
            /// the file is deleted when the object is disposed.
            /// </summary>
            /// <param name="path">A relative path where the file should be created.</param>
            public TestFile(string path)
            {
                Location = Path.Combine(workingDirectory, path);

                // Create intermediate directories
                var directory = Path.GetDirectoryName(Location);
                Directory.CreateDirectory(directory);

                // Create the file
                File.Create(Location);
            }

            /// <summary>
            /// The full path to the file.
            /// </summary>
            public string Location { get; }

            /// <summary>
            /// The name of the file.
            /// </summary>
            public string Name => Path.GetFileName(Location);

            /// <inheritdoc />
            public void Dispose()
            {
                File.Delete(Location);

                var directory = Path.GetDirectoryName(Location);
                while (directory != workingDirectory && !Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    Directory.Delete(directory);
                    directory = Path.GetDirectoryName(directory);
                }
            }
        }
    }
}
