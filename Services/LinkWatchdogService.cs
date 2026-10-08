using Renci.SshNet;
using Renci.SshNet.Common;
using ConnectionInfo = Renci.SshNet.ConnectionInfo;

namespace PowerControl.Services
{
    // Détection de coupure secteur par la perte du lien Ethernet. Les deux Pi sont branchés
    // directement sur le même boîtier CPL, alimenté par le secteur (l'alimentation principale
    // passe par sa prise gigogne) : quand le secteur tombe, le boîtier s'éteint et le lien
    // Ethernet disparaît aussitôt. C'est un signal matériel local, qui ne dépend pas du réseau.
    // Chaque Pi se surveille seul : ce service n'éteint que raspberrypicontrol.
    //
    // Le GPIO de l'Arduino (ShutdownWorker.cs) reste en place : il couvre le cas rare d'une
    // coupure secteur sans perte du lien, et les coupures trop courtes pour atteindre le seuil
    // ci-dessous mais assez longues pour engager la séquence de l'Arduino.
    //
    // Seuil de 30 s : l'Arduino n'engage sa séquence que ~14,6 s après la perte du lien
    // (debounce de 10 s + son propre délai de détection), et le port Ethernet du boîtier
    // revient ~2,5 s après le retour du courant (mesures du 08/10/2026). Un Pi éteint sans
    // que l'Arduino ait engagé sa séquence resterait éteint (rien ne le rallume) : le seuil
    // doit dépasser ~17 s avec de la marge. Voir COUPURE_SECTEUR.md.
    public class LinkWatchdogService : BackgroundService
    {
        private readonly ILogger<LinkWatchdogService> logger;

        // /sys/class/net/eth0 de l'hôte, monté en lecture seule (composecontrol.yml) : le eth0
        // vu depuis le conteneur est son interface virtuelle, pas celle de l'hôte.
        private const string CarrierPath = "/host_eth0/carrier";
        private const string LocalHost = "raspberrypicontrol.local";
        private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan LinkDownThreshold = TimeSpan.FromSeconds(30);

        private DateTime? linkDownSince;
        private bool readErrorLogged;
        private bool shutdownLogged;

        public LinkWatchdogService(ILogger<LinkWatchdogService> logger)
        {
            this.logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                if (IsLinkUp())
                {
                    if (linkDownSince != null)
                    {
                        logger.LogInformation("Lien Ethernet revenu après {Seconds:F1}s", (DateTime.UtcNow - linkDownSince.Value).TotalSeconds);
                        linkDownSince = null;
                    }
                }
                else if (linkDownSince == null)
                {
                    linkDownSince = DateTime.UtcNow;
                    logger.LogWarning("Lien Ethernet perdu (boîtier CPL hors tension ?)");
                }
                else if (DateTime.UtcNow - linkDownSince.Value >= LinkDownThreshold)
                {
                    if (!shutdownLogged)
                    {
                        logger.LogCritical("Lien Ethernet perdu depuis plus de {Seconds}s : extinction locale.", LinkDownThreshold.TotalSeconds);
                        shutdownLogged = true;
                    }
                    // En cas d'échec on réessaie au tour suivant : l'Arduino coupera le 12V de toute façon
                    if (TriggerLocalShutdown())
                        break;
                }

                await Task.Delay(CheckInterval, stoppingToken);
            }
        }

        // En cas de doute (fichier illisible, montage absent...), le lien est considéré présent :
        // un faux positif éteindrait le Pi sans que rien ne le rallume.
        private bool IsLinkUp()
        {
            try
            {
                return File.ReadAllText(CarrierPath).Trim() != "0";
            }
            catch (Exception ex)
            {
                if (!readErrorLogged)
                {
                    logger.LogError("Impossible de lire l'état du lien ({Path}) : {Message} — surveillance inactive", CarrierPath, ex.Message);
                    readErrorLogged = true;
                }
                return true;
            }
        }

        // Renvoie true si l'ordre est parti (y compris si l'hôte a coupé la connexion en s'éteignant)
        private bool TriggerLocalShutdown()
        {
            bool connected = false;
            try
            {
                // "raspberrypicontrol.local" et non "localhost" : chaque conteneur a sa propre
                // interface de loopback, distincte de celle de l'hôte.
                var keyFile = new PrivateKeyFile("/app/ssh_keys/id_rsa_shutdown");
                var keyAuth = new PrivateKeyAuthenticationMethod("isochre", keyFile);
                var connectionInfo = new ConnectionInfo(LocalHost, "isochre", keyAuth) { Timeout = TimeSpan.FromSeconds(5) };

                using var client = new SshClient(connectionInfo);
                client.Connect();
                connected = true;
                // Commande remplacée côté hôte par la commande forcée de authorized_keys
                using var command = client.CreateCommand("sudo shutdown -h now");
                command.CommandTimeout = TimeSpan.FromSeconds(5);
                command.Execute();
                return true;
            }
            catch (SshConnectionException) when (connected)
            {
                // L'hôte a coupé la connexion pendant la commande : cas normal d'un shutdown
                return true;
            }
            catch (Exception ex)
            {
                logger.LogError("Échec de l'extinction via SSH, nouvelle tentative : {Message}", ex.Message);
                return false;
            }
        }
    }
}
