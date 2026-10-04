using Renci.SshNet;
using ConnectionInfo = Renci.SshNet.ConnectionInfo;

namespace PowerControl.Services
{
    // Filet de secours si le signal GPIO de l'Arduino ne déclenche jamais (fil coupé,
    // panne de lecture, bug) : C vérifie lui aussi, indépendamment, sa propre
    // connectivité (comme P), et s'éteint (en essayant aussi de prévenir P) s'il perd
    // le contact avec P ET la passerelle en même temps, pendant un temps soutenu.
    // Le GPIO (ShutdownWorker.cs) reste le mécanisme principal, rapide et fiable ;
    // celui-ci ne sert que si le premier échoue silencieusement.
    public class PingWatchdogService : BackgroundService
    {
        private readonly ILogger<PingWatchdogService> logger;

        private const int CheckIntervalSeconds = 5;
        private const int UnreachableThresholdSeconds = 90;
        private const string PowerHost = "raspberrypi.local";
        private const int PowerPort = 22; // SSH, quasi toujours ouvert sur un Raspberry Pi
        private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(3);

        private DateTime lastContactTime = DateTime.UtcNow;

        public PingWatchdogService(ILogger<PingWatchdogService> logger)
        {
            this.logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                bool powerReachable = await NetworkUtils.IsReachableAsync(PowerHost, PowerPort, ConnectTimeout);
                bool gatewayReachable = await IsGatewayReachableViaSshAsync();

                // OR, pas AND : dès qu'UNE des deux cibles répond, tout va bien. Ça
                // distingue "RPi Power redémarre pour maintenance" (passerelle encore
                // joignable) d'une vraie coupure généralisée (plus rien ne répond).
                if (powerReachable || gatewayReachable)
                {
                    lastContactTime = DateTime.UtcNow;
                }
                else
                {
                    var unreachableFor = DateTime.UtcNow - lastContactTime;
                    logger.LogWarning("RPi Power et passerelle injoignables depuis {Seconds}s", unreachableFor.TotalSeconds);

                    if (unreachableFor.TotalSeconds >= UnreachableThresholdSeconds)
                    {
                        logger.LogCritical("Seuil dépassé : extinction locale (filet de secours, le GPIO n'a pas réagi).");
                        TriggerShutdown();
                        break;
                    }
                }

                await Task.Delay(TimeSpan.FromSeconds(CheckIntervalSeconds), stoppingToken);
            }
        }

        private void TriggerShutdown()
        {
            // On tente de prévenir P au passage : si ce watchdog se déclenche, c'est que
            // le chemin GPIO habituel n'a rien relayé, donc personne d'autre ne l'a fait.
            TrySshShutdown(PowerHost, "RPi Power");
            TrySshShutdown("raspberrypicontrol.local", "RPi Control (local)");
        }

        private void TrySshShutdown(string host, string label)
        {
            try
            {
                var keyFile = new PrivateKeyFile("/app/ssh_keys/id_rsa_shutdown");
                var keyAuth = new PrivateKeyAuthenticationMethod("isochre", keyFile);
                var connectionInfo = new ConnectionInfo(host, "isochre", keyAuth);

                using var client = new SshClient(connectionInfo);
                client.Connect();
                var command = client.RunCommand(
                    "echo \"Extinction déclenchée par le watchdog de secours le $(date)\" >> shutdown_log.txt && sudo shutdown -h now");
                logger.LogInformation("Commande d'extinction envoyée à {Label} : {Result}", label, command.Result);
                client.Disconnect();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Échec de l'extinction via SSH vers {Label}", label);
            }
        }

        // Même principe que côté powermonitorbackend (voir COUPURE_SECTEUR.md) : le
        // conteneur ne peut pas lire la vraie passerelle de l'hôte directement, on passe
        // donc par SSH vers l'hôte lui-même pour lui demander de vérifier. Ce canal
        // fonctionne même en cas de vraie coupure réseau (adresse locale, routée en
        // interne par le noyau), contrairement à la commande `ping` qu'il exécute.
        private async Task<bool> IsGatewayReachableViaSshAsync()
        {
            try
            {
                var keyFile = new PrivateKeyFile("/app/ssh_keys/id_gateway_check");
                var keyAuth = new PrivateKeyAuthenticationMethod("isochre", keyFile);
                var connectionInfo = new ConnectionInfo("raspberrypicontrol.local", "isochre", keyAuth);

                using var client = new SshClient(connectionInfo);
                await Task.Run(() => client.Connect());
                var command = client.RunCommand(
                    "ping -c 1 -W 2 $(ip route | awk '/^default/ {print $3; exit}') > /dev/null 2>&1 && echo OK || echo FAIL");
                client.Disconnect();

                return command.Result.Trim() == "OK";
            }
            catch
            {
                return false;
            }
        }
    }
}
