using System.Net;
using System.Reflection;
using System.Text.Json;
using Argentini.OllamaFarm.Models;
using Argentini.OllamaFarm.Services;

var version = await Identify.VersionAsync(Assembly.GetExecutingAssembly());
const string introString = "Ollama Farm: Combine one or more Ollama API instances into a single Ollama API service";
var versionString = $"Version {version} for {Identify.GetOsPlatformName()} (.NET {Identify.GetRuntimeVersion()}/{Identify.GetProcessorArchitecture()})";

await Console.Out.WriteLineAsync(introString);
await Console.Out.WriteLineAsync(versionString);
await Console.Out.WriteLineAsync("=".Repeat(versionString.Length > introString.Length ? versionString.Length : introString.Length));

var builder = WebApplication.CreateBuilder(args);

// Configure logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Warning);

var stateService = new StateService();

builder.Services.AddSingleton(stateService);

#if DEBUG

args = ["localhost","10.0.10.3"];

#endif

#region Process Arguments

if (args.Length == 0)
{
    await Console.Out.WriteLineAsync();
    await Console.Out.WriteLineAsync("Usage:");
    await Console.Out.WriteLineAsync("    ollamafarm [[--port | -p] [port]] [host host host ...]");
    await Console.Out.WriteLineAsync();
    await Console.Out.WriteLineAsync("Parameters:");
    await Console.Out.WriteLineAsync("    [[--port | -p] [port]] : Listen to HTTP port number (defaults to 4444)");
    await Console.Out.WriteLineAsync("    [host host host ...]   : List of host names with optional ports");
    await Console.Out.WriteLineAsync();
    await Console.Out.WriteLineAsync("Examples:");
    await Console.Out.WriteLineAsync("    ollamafarm localhost 10.0.10.1 10.0.10.3");
    await Console.Out.WriteLineAsync("    ollamafarm --port 1234 localhost 10.0.10.1 10.0.10.3");
    await Console.Out.WriteLineAsync("    ollamafarm --port 1234 localhost:11234 10.0.10.1 10.0.10.3");
    
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
                await Console.Out.WriteLineAsync("Error => passed --port parameter without a port number");
                Environment.Exit(1);
            }

            if (int.TryParse(args[i], out var listenPort) == false)
            {
                await Console.Out.WriteLineAsync("Error => passed --port parameter without a port number");
                Environment.Exit(1);
            }
            
            if (listenPort is < 1 or > 65535)
            {
                await Console.Out.WriteLineAsync("Error => passed --port parameter with an invalid port number");
                Environment.Exit(1);
            }

            stateService.Port = listenPort;

            continue;
        }
        
        var segments = arg.Split(':', StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length < 1)
            continue;

        var port = 11434;

        if (segments.Length == 2)
            if (int.TryParse(segments[1], out port) == false)
            {
                await Console.Out.WriteLineAsync($"Error => passed host {arg} specifies a port but the port is invalid");
                Environment.Exit(1);
            }
        
        if (port is < 1 or > 65535)
        {
            await Console.Out.WriteLineAsync($"Error => passed host {arg} specifies a port but the port is invalid");
            Environment.Exit(1);
        }
        
        stateService.Hosts.Add(new OllamaHost
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
    serverOptions.ListenAnyIP(stateService.Port);
});

foreach (var host in stateService.Hosts)
{
    await StateService.ServerAvailableAsync(host);
    await Console.Out.WriteLineAsync($"Using ollama host {host.Address}:{host.Port} ({(host.IsOnline ? "Online" : "Offline")})");

    if (host.IsOffline)
        host.NextPing = DateTime.Now;
}

await Console.Out.WriteLineAsync($"Listening on port {stateService.Port}; press ESC or Control+C to exit");
await Console.Out.WriteLineAsync();

var app = builder.Build();

#region Endpoints

app.MapPost("/api/generate/", async Task<IResult> (HttpRequest request) =>
    {
        var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromMinutes(15));
        var jsonRequest = string.Empty;

        using (var reader = new StreamReader(request.Body))
        {
            jsonRequest = await reader.ReadToEndAsync();
        }

        do
        {
            OllamaHost? host = null;

            while (host is null)
            {
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
                        await Console.Out.WriteLineAsync($"{DateTime.Now:s} => Ollama host {_host.Address}:{_host.Port} offline; retry in {StateService.RetrySeconds} secs");
                    }

                    if (_host.IsOnline && wasOffline)
                    {
                        _host.IsBusy = false;
                        await Console.Out.WriteLineAsync($"{DateTime.Now:s} => Ollama host {_host.Address}:{_host.Port} back online");
                    }

                    if (_host.IsOffline)
                    {
                        _host.IsBusy = false;
                    }
                    else
                    {
                        if (host is not null)
                            _host.IsBusy = false;
                        else
                            host = _host;
                    }
                }

                if (host is null)
                    await Task.Delay(25);
            }

            try
            {
                await Console.Out.WriteLineAsync($"{DateTime.Now:s} => Sending request to host {host.Address}:{host.Port}");
                
                var ollamaRequest = new HttpRequestMessage(HttpMethod.Post, $"http://{host.Address}:{host.Port}/api/generate/")
                {
                    Content = new StringContent(jsonRequest, Encoding.UTF8, "application/json")
                };

                using (var httpClient = new HttpClient())
                {
                    httpClient.Timeout = TimeSpan.FromSeconds(host.RequestTimeoutSeconds);

                    var httpResponse = await httpClient.SendAsync(ollamaRequest, HttpCompletionOption.ResponseHeadersRead);
                    var responseJson = await httpResponse.Content.ReadAsStringAsync();

                    responseJson = responseJson.TrimStart('{');
                    responseJson = $"{{\"farm_host\": \"{host.Address}:{host.Port}\"," + responseJson;
                    
                    var jsonObject = JsonSerializer.Deserialize<object>(responseJson);
                    
                    cancellationTokenSource.Cancel();

                    return Results.Json(jsonObject, contentType: "application/json", statusCode: (int)httpResponse.StatusCode);
                }
            }
            catch
            {
                await StateService.ServerAvailableAsync(host);

                if (host.IsOffline)
                    await Console.Out.WriteLineAsync($"{DateTime.Now:s} => Ollama host {host.Address}:{host.Port} offline; retry in {StateService.RetrySeconds} secs");
            }
            finally
            {
                host.IsBusy = false;
            }
            
            await Console.Out.WriteLineAsync($"{DateTime.Now:s} => Ollama host {host.Address}:{host.Port} => unavailable, using new host...");
            
        } while (cancellationTokenSource.IsCancellationRequested == false);
        
        var result = new
        {
            Error = "Request could not be completed in time"
        };
                
        return Results.Json(result, contentType: "application/json", statusCode: (int)HttpStatusCode.InternalServerError);
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
                await Console.Out.WriteLineAsync($"{DateTime.Now:s} => Escape key pressed, exiting...");
            }
        }
        await Task.Delay(100); // Check every 100ms
    }
});

await app.RunAsync(cts.Token);
