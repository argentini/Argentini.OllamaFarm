# Ollama Farm

Ollama Farm is a CLI tool that intermediates REST API calls to multiple ollama API services. Simply make calls to the Ollama Farm REST API as if it were an ollama REST API and the rest is handled for you.

## Installation

Install dotnet 8 or later from [https://dotnet.microsoft.com/en-us/download](https://dotnet.microsoft.com/en-us/download) and then install Ollama Farm with the following command:

```
dotnet tool install --global argentini.ollamafarm
```

You should relaunch Terminal/cmd/PowerShell so that the system path will be reloaded and the *ollamafarm* command can be found. If you've previously installed the *dotnet* runtime, this won't be necessary.

## Usage

Ollama Farm is a system-level command line interface application (CLI). After installing you can access Ollama Farm at any time.

To get help on the available commands, just run `ollamafarm` in Terminal, cmd, or PowerShell.

```
ollamafarm
```

This will launch the application in help mode which displays the commands and options. For example, to launch it with one or more server addresses running the ollama API service on the default port:

```
ollamafarm localhost 192.168.0.5 192.168.0.6
```

It will listen on port 4444 for ollama API requests to `/api/generate`. You would simply POST the same JSON request as you would to the ollama API service. The request will get sent to the first available host.

You can also change the listening port:

```
ollamafarm --port 5555 localhost 192.168.0.5 192.168.0.6
```

And if you run your ollama hosts on a custom port, just use colon syntax:

```
ollamafarm --port 5555 localhost:12345 192.168.0.5 192.168.0.6
```
## Ollama Farm Requests

Make Ollama API requests to this service and they will be routed to one of the Ollama API hosts in the farm. Requests should be sent to this service (default port 4444) and follow the standard Ollama JSON request body format (HTTP POST to **/api/generate/**). *Streaming is supported*.

**To optimize performance Ollama Farm restricts each host to processing one request at a time.** When all hosts are busy REST calls return status code **429** (too many requests). This allows requesters to poll until a resource is available.

### Additional Properties

- **farm_host** : Request a specific host (e.g. localhost:11434)
- **farm_host** : Identify the host used

#### Example:
```
{
    "farm_host": "localhost",
    "model": "gemma2:27b-instruct-q8_0",
    ...
}
```