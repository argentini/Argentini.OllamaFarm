using System.Net.Sockets;
using Argentini.OllamaFarm.Models;

namespace Argentini.OllamaFarm.Services;

public sealed class StateService
{
    #region Properties
    
    public int Port { get; set; } = 4444;
    public static int RetrySeconds => 30;
    public ConcurrentBag<OllamaHost> Hosts { get; } = [];

    #endregion
    
    #region Methods
    
    public static async Task ServerAvailableAsync(OllamaHost host)
    {
        try
        {
            host.NextPing = DateTime.Now.AddSeconds(RetrySeconds);
            
            using var tcpClient = new TcpClient();

            var cancellationTokenSource = new CancellationTokenSource(host.ConnectTimeoutSeconds);

            await tcpClient.ConnectAsync(host.Address, host.Port, cancellationTokenSource.Token);

            host.IsOnline = true;
            host.NextPing = DateTime.Now.AddSeconds(RetrySeconds);

            return;
        }
        catch
        {
            // ignored
        }

        host.IsOnline = false;
        host.NextPing = DateTime.Now.AddSeconds(RetrySeconds);
    }
    
    #endregion
}