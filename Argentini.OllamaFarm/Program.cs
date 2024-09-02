using System.Net;
using System.Reflection;
using System.Text.Json;
using Argentini.OllamaFarm.Models;
using Argentini.OllamaFarm.Services;

var maxConsoleWidth = Console.WindowWidth > 80 ? 80 : Console.WindowWidth - 1;
var version = await Identify.VersionAsync(Assembly.GetExecutingAssembly());
const string introString = "Ollama Farm: Combine Ollama API instances into a single Ollama API service";
var versionString = $"Version {version} for {Identify.GetOsPlatformName()} (.NET {Identify.GetRuntimeVersion()}/{Identify.GetProcessorArchitecture()})";

introString.WriteToConsole(maxConsoleWidth);
versionString.WriteToConsole(maxConsoleWidth);
"=".Repeat(maxConsoleWidth).WriteToConsole(maxConsoleWidth);

var builder = WebApplication.CreateBuilder(args);

// Configure logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Warning);

var _stateService = new StateService();

builder.Services.AddSingleton(_stateService);
builder.Services.AddHttpClient();

#if DEBUG

args = ["localhost","10.0.10.3"];

#endif

#region Process Arguments

if (args.Length == 0)
{
    "".WriteToConsole(maxConsoleWidth);
    "Usage:".WriteToConsole(maxConsoleWidth);
    "    ollamafarm [[--port | -p] [port]] [host host host ...]".WriteToConsole(maxConsoleWidth);
    "".WriteToConsole(maxConsoleWidth);
    "Parameters:".WriteToConsole(maxConsoleWidth);
    "    [[--port | -p] [port]] : Listen to HTTP port number (defaults to 4444)".WriteToConsole(maxConsoleWidth);
    "    [host host host ...]   : List of host names with optional ports".WriteToConsole(maxConsoleWidth);
    "".WriteToConsole(maxConsoleWidth);
    "Examples:".WriteToConsole(maxConsoleWidth);
    "    ollamafarm localhost 10.0.10.1 10.0.10.3".WriteToConsole(maxConsoleWidth);
    "    ollamafarm --port 1234 localhost 10.0.10.1 10.0.10.3".WriteToConsole(maxConsoleWidth);
    "    ollamafarm --port 1234 localhost:11234 10.0.10.1 10.0.10.3".WriteToConsole(maxConsoleWidth);
    "".WriteToConsole(maxConsoleWidth);
    
    "Ollama Farm Requests".WriteToConsole(maxConsoleWidth);
    "-".Repeat(maxConsoleWidth).WriteToConsole(maxConsoleWidth);
    "Make Ollama API requests to this service and they will be routed to one of the Ollama API hosts in the farm. Requests should be sent to this service (default port 4444) and follow the standard Ollama JSON request body format (HTTP POST to /api/generate/). Streaming is supported.".WriteToConsole(maxConsoleWidth);
    "".WriteToConsole(maxConsoleWidth);
    "To optimize performance Ollama Farm restricts each host to processing one request at a time. When all hosts are busy REST calls return status code 429 (too many requests). This allows requesters to poll until a resource is available.".WriteToConsole(maxConsoleWidth);
    "".WriteToConsole(maxConsoleWidth);
    "Additional properties:".WriteToConsole(maxConsoleWidth);
    "    farm_host (requests) : Request a specific host (e.g. localhost:11434)".WriteToConsole(maxConsoleWidth);
    "    farm_host (response) : Identify the host used".WriteToConsole(maxConsoleWidth);
    "".WriteToConsole(maxConsoleWidth);
    "Example:".WriteToConsole(maxConsoleWidth);
    "    { \"farm_host\": \"localhost\", \"model\": ... }".WriteToConsole(maxConsoleWidth);
    "".WriteToConsole(maxConsoleWidth);
    
    Environment.Exit(0);
}
else
{
    for (var i = 0; i < args.Length; i++)
    {
        var arg = args[i];
        
        if (string.IsNullOrEmpty(arg))
            continue;

        if (arg.Equals("--port", StringComparison.OrdinalIgnoreCase) || arg.Equals("-p", StringComparison.OrdinalIgnoreCase))
        {
            if (args.Length <= ++i)
            {
                Console.WriteLine("Error => passed --port parameter without a port number");
                Environment.Exit(1);
            }

            if (int.TryParse(args[i], out var listenPort) == false)
            {
                Console.WriteLine("Error => passed --port parameter without a port number");
                Environment.Exit(1);
            }
            
            if (listenPort is < 1 or > 65535)
            {
                Console.WriteLine("Error => passed --port parameter with an invalid port number");
                Environment.Exit(1);
            }

            _stateService.Port = listenPort;

            continue;
        }
        
        var segments = arg.Split(':', StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length < 1)
            continue;

        var port = 11434;

        if (segments.Length == 2)
            if (int.TryParse(segments[1], out port) == false)
            {
                Console.WriteLine($"Error => passed host {arg} specifies a port but the port is invalid");
                Environment.Exit(1);
            }
        
        if (port is < 1 or > 65535)
        {
            Console.WriteLine($"Error => passed host {arg} specifies a port but the port is invalid");
            Environment.Exit(1);
        }
        
        _stateService.Hosts.Add(new OllamaHost
        {
            Address = segments[0],
            Port = port,
            IsOnline = true
        });
    }
}

#endregion

builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.ListenAnyIP(_stateService.Port);
});

foreach (var host in _stateService.Hosts)
{
    await StateService.ServerAvailableAsync(host);
    Console.WriteLine($"Using Ollama host {host.Address}:{host.Port} ({(host.IsOnline ? "Online" : "Offline")})");

    if (host.IsOffline)
        host.NextPing = DateTime.Now;
}

Console.WriteLine($"Listening on port {_stateService.Port}; press ESC or Control+C to exit");
Console.WriteLine();

var app = builder.Build();

#region Endpoints

app.MapPost("/api/generate/", async Task<IResult> (HttpRequest request, HttpResponse response, StateService stateService, HttpClient httpClient) =>
    {
        var jsonRequest = string.Empty;

        using (var reader = new StreamReader(request.Body))
        {
            jsonRequest = await reader.ReadToEndAsync();
        }

        if (string.IsNullOrEmpty(jsonRequest))
        {
            return Results.Json(new
            {
                Message = "No JSON payload"
                
            }, contentType: "application/json", statusCode: (int)HttpStatusCode.BadRequest);
        }

        var farmModel = JsonSerializer.Deserialize<FarmSubmodel>(jsonRequest);
        var requestedHost = farmModel?.farm_host ?? string.Empty;

        if (requestedHost.Length > 0)
        {
            if (requestedHost.Contains(':') == false)
                requestedHost = $"{requestedHost}:11434";
        }
        
        OllamaHost? host = null;

        foreach (var _host in stateService.Hosts)
        {
            if (_host.IsBusy || (_host.IsOffline && _host.NextPing > DateTime.Now))
                continue;

            _host.IsBusy = true;

            var wasOnline = _host.IsOnline;
            var wasOffline = _host.IsOnline == false;
            
            if (_host.NextPing <= DateTime.Now)
                await StateService.ServerAvailableAsync(_host);
            
            if (_host.IsOffline && wasOnline)
            {
                _host.IsBusy = false;
                Console.WriteLine($"{DateTime.Now:s} => Ollama host {_host.Address}:{_host.Port} offline; retry in {StateService.RetrySeconds} secs");
            }

            if (_host.IsOnline && wasOffline)
            {
                _host.IsBusy = false;
                Console.WriteLine($"{DateTime.Now:s} => Ollama host {_host.Address}:{_host.Port} back online");
            }

            if (_host.IsOnline && host is null && (string.IsNullOrEmpty(requestedHost) || requestedHost.Equals(_host.FullAddress, StringComparison.InvariantCultureIgnoreCase)))
            {
                host = _host;
            }
            else
            {
                _host.IsBusy = false;
            }
        }

        if (host is null)
        {
            return Results.Json(new
            {
                Message = requestedHost == string.Empty ? "All Ollama hosts are currently busy" : $"Requested host {requestedHost} is currently busy"
                
            }, contentType: "application/json", statusCode: (int)HttpStatusCode.TooManyRequests);
        }

        var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(host.RequestTimeoutSeconds));
        
        try
        {
            var timer = new Stopwatch();
            var requestId = jsonRequest.Crc32();
            
            timer.Start();
            
            Console.WriteLine($"{DateTime.Now:s} => Request to {host.Address}:{host.Port} (#{requestId})");
            
            var completion = farmModel?.stream ?? false
                ? HttpCompletionOption.ResponseHeadersRead
                : HttpCompletionOption.ResponseContentRead;                

            var httpResponse = await httpClient.SendAsync(new HttpRequestMessage(HttpMethod.Post, $"http://{host.Address}:{host.Port}/api/generate/")
            {
                Content = new StringContent(jsonRequest, Encoding.UTF8, "application/json")
                
            }, completion, cancellationTokenSource.Token);

            if (farmModel?.stream ?? false)
            {
                response.ContentType = "application/json";

                await using (var stream = await httpResponse.Content.ReadAsStreamAsync())
                {
                    using (var reader = new StreamReader(stream))
                    {
                        while (reader.EndOfStream == false && cancellationTokenSource.IsCancellationRequested == false)
                        {
                            var line = await reader.ReadLineAsync(cancellationTokenSource.Token) + '\n';

                            if (string.IsNullOrEmpty(line))
                                continue;
                            
                            line = line.TrimStart('{');
                            line = $"{{\"farm_host\":\"{host.Address}:{host.Port}\"," + line;

                            await response.Body.WriteAsync(Encoding.UTF8.GetBytes(line), cancellationTokenSource.Token);
                            await response.Body.FlushAsync(cancellationTokenSource.Token);
                        }
                    }
                }

                timer.Stop();
                Console.WriteLine($"{DateTime.Now:s} => Request to {host.Address}:{host.Port} (#{requestId}) streamed in {(double)timer.ElapsedMilliseconds / 1000:F2}s");

                return Results.Empty;
            }
            else
            {
                var responseJson = await httpResponse.Content.ReadAsStringAsync(cancellationTokenSource.Token);

                responseJson = responseJson.TrimStart().TrimStart('{');
                responseJson = $"{{\"farm_host\":\"{host.Address}:{host.Port}\"," + responseJson;
                
                var jsonObject = JsonSerializer.Deserialize<object>(responseJson);

                timer.Stop();
                Console.WriteLine($"{DateTime.Now:s} => Request to {host.Address}:{host.Port} (#{requestId}) complete in {(double)timer.ElapsedMilliseconds / 1000:F2}s");

                return Results.Json(jsonObject, contentType: "application/json", statusCode: (int)httpResponse.StatusCode);
            }
        }
        catch (Exception e)
        {
            if (cancellationTokenSource.IsCancellationRequested)
            {
                return Results.Json(new
                {
                    Message = $"The Ollama host request timeout of {host.RequestTimeoutSeconds} secs has expired."
                
                }, contentType: "application/json", statusCode: (int)HttpStatusCode.RequestTimeout);
            }
            
            await StateService.ServerAvailableAsync(host);

            if (host.IsOffline)
                Console.WriteLine($"{DateTime.Now:s} => Ollama host {host.Address}:{host.Port} offline; retry in {StateService.RetrySeconds} secs");
            
            return Results.Json(new
            {
                Message = $"{(host.IsOffline ? $"Ollama host {host.Address}:{host.Port} offline; retry in {StateService.RetrySeconds} secs => " : string.Empty)}{e.Message}"
                
            }, contentType: "application/json", statusCode: (int)HttpStatusCode.InternalServerError);
        }
        finally
        {
            host.IsBusy = false;
        }
    });

#endregion

var cts = new CancellationTokenSource();

Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
    Console.Out.WriteLineAsync($"{DateTime.Now:s} => Control+C pressed, exiting...");
};

_ = Task.Run(async () =>
{
    while (cts.Token.IsCancellationRequested == false)
    {
        if (Console.KeyAvailable)
        {
            var key = Console.ReadKey(intercept: true);

            if (key.Key == ConsoleKey.Escape)
            {
                cts.Cancel();
                Console.WriteLine($"{DateTime.Now:s} => Escape key pressed, exiting...");
            }
        }
        await Task.Delay(100); // Check every 100ms
    }
});

await app.RunAsync(cts.Token);
