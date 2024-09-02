namespace Argentini.OllamaFarm.Models;

public sealed class OllamaHost
{
    public string Address { get; set; } = string.Empty;
    public int Port { get; set; } = 11434;
    public string FullAddress => $"{Address}:{Port}";

    public int ConnectTimeoutSeconds { get; set; } = 15;
    public int RequestTimeoutSeconds { get; set; } = 300;

    public DateTime NextPing { get; set; } = DateTime.Now;
    public bool IsBusy { get; set; }
    public bool IsOnline { get; set; }
    public bool IsOffline => IsOnline == false;
}