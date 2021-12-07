# Http Server

This provides a local HTTP server on device. It allows the app to create a server by defining routes for given methods.

## Using the Server

### Install the Nuget

```bash
nuget install Float.HttpServer
```

### Creating a Server

#### Create the Routes

```C#
using System;
using System.Collections.Generic;
using System.Net;
using Float.HttpServer;
using Xamarin.Essentials;
using static Float.HttpServer.HttpRouter;
using HttpServer = Float.HttpServer.LocalHttpServer;

public class LocalHttpServer
{
    private readonly HttpServer server;
    private Uri uri;

    /// <summary>
    /// Initializes a new instance of the <see cref="LocalHttpServer"/> class.
    /// </summary>
    public LocalHttpServer()
    {
      var host = "127.0.0.1";
      var port = 33616;
      server = new HttpServer(host, port);
      uri = new Uri($"http://{host}:{port}/");
      // If the route is not matched directly, always use the StaticFileResponder.
      server.SetDefaultResponder(new StaticFileResponder(FileSystem.CacheDirectory));
      // Create an error page when things aren't found.
      server.SetErrorResponder(new ErrorResponder404());
      // Inject some middleware that stops the request if the user-agent
      // doesn't contain the word "Mozilla".
      server.Use(
          new List<HttpMethod> { HttpMethod.GET },
          (HttpListenerRequest request, ref HttpListenerResponse response) =>
          {
              if (request.Headers.HasKeys() &&
                  string.Join(string.Empty, request.Headers.GetValues("User-Agent")).Contains("Mozilla") == false)
              {
                  response.StatusCode = 401;
                  return false;
              }

              return true;
          });
      // Create a dynamic route that responds to post
      // requests at /node/:nodeId where nodeId is a parameter injected
      // into the responder.
      server.Post("/node/:nodeId", new NodePostResponder());

      // Create a route that responds to GET, PUT, and POST requests
      // at /agents/profile.
      server.AddResponser(
          new List<HttpMethod>
          {
              HttpMethod.GET,
              HttpMethod.POST,
              HttpMethod.PUT,
          },
          "/agents/profile",
          new AgentProfileResponder());
    });

    /// <summary>
    /// Starts the default server.
    /// </summary>
    public void Start()
    {
        server.Start();
    }

    /// <summary>
    /// Stops the default server.
    /// </summary>
    public void Stop()
    {
        server.Stop();
    }
}
```

#### Start the Server

```C#
var localHttpServer = new LocalHttpServer();
localHttpServer.Start();
```

#### Stop the Server

```C#
localHttpServer.Stop()
```

## Building the Nuget

```bash
./build.sh
```
