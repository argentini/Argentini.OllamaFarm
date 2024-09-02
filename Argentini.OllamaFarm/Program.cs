using System.Reflection;
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

#region Load Settings

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
            Port = port
        });
    }
}

#endregion

builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.ListenAnyIP(stateService.Port);
});

await Console.Out.WriteLineAsync($"Listening on port {stateService.Port}");

foreach(var host in stateService.Hosts)
    await Console.Out.WriteLineAsync($"Using ollama host {host.Address}:{host.Port}");

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    // May use this block later
}

app.MapPost("/api/generate", async Task<IResult> (HttpRequest request) =>
    {
        using (var reader = new StreamReader(request.Body))
        {
            var json = await reader.ReadToEndAsync();
            
            await Console.Out.WriteLineAsync(json);
            
            
            
            
            
            
            
            
            
        }
        
        return Results.Ok();
    })
    .WithName("Generate")
    .WithOpenApi();

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet("/weatherforecast", () =>
    {
        var forecast = Enumerable.Range(1, 5).Select(index =>
                new WeatherForecast
                (
                    DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
                    Random.Shared.Next(-20, 55),
                    summaries[Random.Shared.Next(summaries.Length)]
                ))
            .ToArray();
        return forecast;
    })
    .WithName("GetWeatherForecast")
    .WithOpenApi();

app.Run();

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
