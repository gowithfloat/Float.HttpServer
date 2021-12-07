using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Web;

namespace Float.HttpServer
{
    /// <summary>
    /// Serves static files for HttpServer.
    /// </summary>
    public class StaticFileResponder : IHttpResponder
    {
        /// <summary>
        /// Special header to include when the filepath should include query string.
        /// </summary>
        public const string ExtendedFilePathCheckHeaderKey = "X-Expanded-File-Path";

        static readonly IDictionary<string, string> MimeTypeMappings = new Dictionary<string, string>(StringComparer.InvariantCultureIgnoreCase)
        {
            { ".asf", "video/x-ms-asf" },
            { ".asx", "video/x-ms-asf" },
            { ".avi", "video/x-msvideo" },
            { ".bin", "application/octet-stream" },
            { ".cco", "application/x-cocoa" },
            { ".crt", "application/x-x509-ca-cert" },
            { ".css", "text/css" },
            { ".deb", "application/octet-stream" },
            { ".der", "application/x-x509-ca-cert" },
            { ".dll", "application/octet-stream" },
            { ".dmg", "application/octet-stream" },
            { ".ear", "application/java-archive" },
            { ".eot", "application/octet-stream" },
            { ".exe", "application/octet-stream" },
            { ".flv", "video/x-flv" },
            { ".gif", "image/gif" },
            { ".hqx", "application/mac-binhex40" },
            { ".htc", "text/x-component" },
            { ".htm", "text/html" },
            { ".html", "text/html" },
            { ".ico", "image/x-icon" },
            { ".img", "application/octet-stream" },
            { ".iso", "application/octet-stream" },
            { ".jar", "application/java-archive" },
            { ".jardiff", "application/x-java-archive-diff" },
            { ".jng", "image/x-jng" },
            { ".jnlp", "application/x-java-jnlp-file" },
            { ".jpeg", "image/jpeg" },
            { ".jpg", "image/jpeg" },
            { ".js", "application/x-javascript" },
            { ".mml", "text/mathml" },
            { ".mng", "video/x-mng" },
            { ".mov", "video/quicktime" },
            { ".mp3", "audio/mpeg" },
            { ".mp4", "application/mp4" },
            { ".mpeg", "video/mpeg" },
            { ".mpg", "video/mpeg" },
            { ".msi", "application/octet-stream" },
            { ".msm", "application/octet-stream" },
            { ".msp", "application/octet-stream" },
            { ".pdb", "application/x-pilot" },
            { ".pdf", "application/pdf" },
            { ".pem", "application/x-x509-ca-cert" },
            { ".pl", "application/x-perl" },
            { ".pm", "application/x-perl" },
            { ".png", "image/png" },
            { ".prc", "application/x-pilot" },
            { ".ra", "audio/x-realaudio" },
            { ".rar", "application/x-rar-compressed" },
            { ".rpm", "application/x-redhat-package-manager" },
            { ".rss", "text/xml" },
            { ".run", "application/x-makeself" },
            { ".sea", "application/x-sea" },
            { ".shtml", "text/html" },
            { ".sit", "application/x-stuffit" },
            { ".svg", "image/svg+xml" },
            { ".swf", "application/x-shockwave-flash" },
            { ".tcl", "application/x-tcl" },
            { ".tk", "application/x-tcl" },
            { ".ttf", "font/ttf" },
            { ".txt", "text/plain" },
            { ".war", "application/java-archive" },
            { ".wbmp", "image/vnd.wap.wbmp" },
            { ".wmv", "video/x-ms-wmv" },
            { ".woff", "font/woff" },
            { ".woff2", "font/woff" },
            { ".xml", "text/xml" },
            { ".xpi", "application/x-xpinstall" },
            { ".zip", "application/zip" },
        };

        static readonly string[] IndexFiles =
        {
            "index.html",
            "index.htm",
            "default.html",
            "default.htm",
        };

        readonly string rootDirectory;

        /// <summary>
        /// Initializes a new instance of the <see cref="StaticFileResponder"/> class.
        /// </summary>
        /// <param name="filepath">Path to files.</param>
        public StaticFileResponder(string filepath)
        {
            rootDirectory = filepath;
        }

        /// <summary>
        /// Decodes file path for a request and document root.
        /// </summary>
        /// <param name="rootDirectory">The directory to which to check the files.</param>
        /// <param name="uri">The request uri to check.</param>
        /// <param name="headers">The request headers to check.</param>
        /// <returns>A file path for a given request and docroot.</returns>
        public static string FilePathForRequest(string rootDirectory, Uri uri, NameValueCollection headers = null)
        {
            if (uri == null)
            {
                throw new ArgumentNullException(nameof(uri));
            }

            if (rootDirectory == null)
            {
                throw new ArgumentNullException(nameof(rootDirectory));
            }

            var requestedPath = uri.AbsolutePath.TrimStart('/');

            // If the request path has no extension, then the request might
            // refer to an index path in that directory.
            if (!Path.HasExtension(requestedPath))
            {
                var directory = Path.Combine(rootDirectory, requestedPath);

                if (Directory.Exists(directory))
                {
                    foreach (string indexFile in IndexFiles)
                    {
                        var candidate = Path.Combine(directory, indexFile);
                        if (FileExistsAtLocation(candidate, out var indexPath))
                        {
                            return indexPath;
                        }
                    }
                }
            }

            // !! Special check !!
            // Sometimes files contain the query path so we need to append it to the file name.
            if (!string.IsNullOrEmpty(uri.Query) && headers?[ExtendedFilePathCheckHeaderKey] == "true")
            {
                var adjustedPath = Path.Combine(rootDirectory, uri.PathAndQuery.TrimStart('/'));
                if (FileExistsAtLocation(adjustedPath, out var queryInclusivePath))
                {
                    return queryInclusivePath;
                }
            }

            var absolutePath = Path.Combine(rootDirectory, requestedPath);
            if (FileExistsAtLocation(absolutePath, out var path))
            {
                return path;
            }

            return absolutePath;
        }

        /// <inheritdoc/>
        public void GenerateResponse(in HttpListenerRequest httpRequest, ref HttpListenerResponse httpResponse, IDictionary<string, string> parameters)
        {
            if (httpRequest == null)
            {
                throw new ArgumentNullException(nameof(httpRequest));
            }

            if (httpResponse == null)
            {
                throw new ArgumentNullException(nameof(httpResponse));
            }

            if (httpRequest.HttpMethod.ToUpperInvariant() != "GET")
            {
                throw new Exception("Method not supported");
            }

            var filename = FilePathForRequest(rootDirectory, httpRequest.Url, httpRequest.Headers);
            var fileExt = Path.GetExtension(filename);

            // Drop the possible query parameter that was appended in the FilePathForRequest.
            fileExt = fileExt.Split('?')[0];

            // If unwrappedFilename is not null we already found the file.
            // We should still check the filename in case there was some other
            // alterations.
            if (File.Exists(filename))
            {
                try
                {
                    // Adding permanent http response headers
                    httpResponse.ContentType = MimeTypeMappings.TryGetValue(fileExt, out string mime) ? mime : "application/octet-stream";
                    httpResponse.AddHeader("Access-Control-Allow-Origin", "*");
                    httpResponse.AddHeader("Date", DateTime.Now.ToString("r"));
                    httpResponse.AddHeader("Last-Modified", File.GetLastWriteTime(filename).ToString("r"));
                    httpResponse.AddHeader("Accept-Ranges", "bytes");
                    var outputStream = httpResponse.OutputStream;

                    using var fileStream = File.OpenRead(filename);
                    var rangeValue = httpRequest.Headers.Get("Range");

                    if (rangeValue != null)
                    {
                        var range = RangeHeaderValue.Parse(rangeValue);

                        if (range.Ranges.Count != 1)
                        {
                            httpResponse.StatusCode = (int)HttpStatusCode.NotImplemented;
                            return;
                        }

                        ContentRangeHeaderValue contentRange = null;
                        byte[] buffer;

                        try
                        {
                            buffer = GetRequestedBytes(range.Ranges.First(), fileStream, out contentRange);
                        }
                        catch (ArgumentOutOfRangeException)
                        {
                            httpResponse.StatusCode = (int)HttpStatusCode.RequestedRangeNotSatisfiable;
                            httpResponse.AddHeader("Content-Range", contentRange.ToString());
                            return;
                        }

                        // If the entire range was requested, then return a 200 OK status code.
                        if (buffer.Length == fileStream.Length)
                        {
                            httpResponse.StatusCode = (int)HttpStatusCode.OK;
                        }
                        else
                        {
                            httpResponse.AddHeader("Content-Range", contentRange.ToString());
                            httpResponse.StatusCode = (int)HttpStatusCode.PartialContent;
                        }

                        httpResponse.ContentLength64 = buffer.Length;

                        outputStream.Write(buffer, 0, buffer.Length);
                    }
                    else
                    {
                        httpResponse.ContentLength64 = fileStream.Length;
                        fileStream.CopyTo(outputStream);
                    }
                }
                catch (Exception)
                {
                    throw;
                }

                return;
            }

            httpResponse.StatusCode = (int)HttpStatusCode.NotFound;
            throw new FileNotFoundException();
        }

        /// <summary>
        /// Checks if a file exists for a given location.
        /// It will decode the filename to check if the filename
        /// given was encoded and return the proper path.
        /// </summary>
        /// <param name="location">The location of the file.</param>
        /// <param name="resolvedLocation">On return, contains the location where the file was found.</param>
        /// <returns><c>true</c> if the file exists at the requested location.</returns>
        static bool FileExistsAtLocation(string location, out string resolvedLocation)
        {
            var decodedPath = HttpUtility.UrlDecode(location);
            if (File.Exists(decodedPath))
            {
                resolvedLocation = decodedPath;
                return true;
            }

            if (File.Exists(location))
            {
                resolvedLocation = location;
                return true;
            }

            resolvedLocation = null;
            return false;
        }

        /// <summary>
        /// Retrieves the requested set of bytes from the input stream.
        /// </summary>
        /// <param name="range">The range of bytes to retrieve.</param>
        /// <param name="inputStream">The source of data.</param>
        /// <param name="contentRange">On return, contains a value for the Content-Range header.</param>
        /// <returns>The requested bytes.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown if the request range is invalid.</exception>
        /// <see href="https://developer.mozilla.org/en-US/docs/Web/HTTP/Headers/Range"/>
        static byte[] GetRequestedBytes(RangeItemHeaderValue range, Stream inputStream, out ContentRangeHeaderValue contentRange)
        {
            long from;
            long to;

            if (range.From != null && range.To != null)
            {
                from = (long)range.From;
                to = (long)range.To;
            }
            else if (range.From != null && range.To == null)
            {
                // If only the range start was provided, then the end of the document is considered to be the end of the range.
                from = (long)range.From;
                to = inputStream.Length - 1;
            }
            else if (range.From == null && range.To != null)
            {
                // If only the range end (suffix length) is provided, return that many bytes from the end of the document.
                from = inputStream.Length - (long)range.To;
                to = inputStream.Length - 1;
            }
            else
            {
                contentRange = new ContentRangeHeaderValue(inputStream.Length);
                throw new ArgumentOutOfRangeException(nameof(range));
            }

            var expectedSize = to - from + 1;
            var buffer = new byte[expectedSize];

            try
            {
                inputStream.Seek(from, SeekOrigin.Begin);
                inputStream.Read(buffer, 0, (int)expectedSize);
                contentRange = new ContentRangeHeaderValue(from, to, inputStream.Length);
            }
            catch (ArgumentException)
            {
                contentRange = new ContentRangeHeaderValue(inputStream.Length);
                throw new ArgumentOutOfRangeException(nameof(range));
            }

            return buffer;
        }
    }
}
