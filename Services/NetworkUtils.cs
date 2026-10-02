using System.Net.Sockets;

namespace PowerControl.Services
{
    public static class NetworkUtils
    {
        // Connexion TCP plutôt qu'un vrai ping ICMP : ce dernier demande des privilèges
        // réseau (CAP_NET_RAW) qu'on a justement retirés du conteneur lors du durcissement
        // (plus de privileged: true). Une tentative de connexion TCP n'en a pas besoin.
        public static async Task<bool> IsReachableAsync(string host, int port, TimeSpan timeout)
        {
            using var client = new TcpClient();
            using var cts = new CancellationTokenSource(timeout);

            try
            {
                await client.ConnectAsync(host, port, cts.Token);
                return client.Connected;
            }
            catch
            {
                return false;
            }
        }
    }
}
