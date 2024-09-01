using System.Collections.Concurrent;
using Argentini.OllamaFarm.Models;

namespace Argentini.OllamaFarm.Services;

public sealed class StateService
{
    public int Port { get; set; } = 4444;
    public ConcurrentBag<OllamaHost> Hosts { get; } = [];

}