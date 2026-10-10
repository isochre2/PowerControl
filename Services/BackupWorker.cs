using Renci.SshNet;
using ConnectionInfo = Renci.SshNet.ConnectionInfo;

namespace PowerControl.Services
{
    // Sauvegarde hebdomadaire de la base InfluxDB de raspberrypi (rétention autogen : énergie par heure,
    // référence de vibration), gardée ici, sur la carte SD de raspberrypicontrol : elle protège d'une
    // panne de la carte SD de raspberrypi.
    // Les Pi ne sont pas allumés en permanence : au lieu d'un horaire fixe, on vérifie toutes les heures
    // si la dernière sauvegarde a plus d'une semaine, et on réessaie l'heure suivante en cas d'échec.
    // La clé id_backup ne permet que d'obtenir une sauvegarde : sur raspberrypi, sa commande forcée
    // lance sauvegarde_influxdb.sh, qui renvoie le fichier tar.gz dans la connexion SSH.
    public class BackupWorker : BackgroundService
    {
        private const string HostName = "raspberrypi.local";
        private const string User = "isochre";
        private const string PrivateKeyPath = "/app/ssh_keys/id_backup";
        private const string BackupFolder = "/app/sauvegardes"; // dossier de l'hôte monté dans le conteneur
        private const int BackupsKept = 8;                       // environ deux mois

        private static readonly TimeSpan BackupPeriod = TimeSpan.FromDays(7);
        private static readonly TimeSpan CheckPeriod = TimeSpan.FromHours(1);
        private static readonly TimeSpan FirstCheckDelay = TimeSpan.FromMinutes(5); // laisser raspberrypi démarrer
        private static readonly TimeSpan BackupTimeout = TimeSpan.FromMinutes(2);

        private readonly ILogger<BackupWorker> logger;
        private string? lastError;

        public BackupWorker(ILogger<BackupWorker> logger)
        {
            this.logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                await Task.Delay(FirstCheckDelay, stoppingToken);
                while (!stoppingToken.IsCancellationRequested)
                {
                    if (DateTime.UtcNow - LastBackupTime() >= BackupPeriod)
                        await TryBackup(stoppingToken);
                    await Task.Delay(CheckPeriod, stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                // arrêt du conteneur
            }
        }

        // Date de la sauvegarde la plus récente (DateTime.MinValue s'il n'y en a aucune)
        private static DateTime LastBackupTime() =>
            Directory.Exists(BackupFolder)
                ? Directory.GetFiles(BackupFolder, "influxdb_*.tar.gz").Select(File.GetLastWriteTimeUtc).DefaultIfEmpty(DateTime.MinValue).Max()
                : DateTime.MinValue;

        private async Task TryBackup(CancellationToken stoppingToken)
        {
            try
            {
                byte[] backup = await DownloadBackup(stoppingToken);

                // Écriture sous un nom temporaire puis renommage : jamais de sauvegarde à moitié écrite
                Directory.CreateDirectory(BackupFolder);
                string path = Path.Combine(BackupFolder, $"influxdb_{DateTime.UtcNow:yyyyMMdd_HHmm}.tar.gz"); // heure UTC
                await File.WriteAllBytesAsync(path + ".tmp", backup, stoppingToken);
                File.Move(path + ".tmp", path, overwrite: true);

                DeleteOldBackups();
                logger.LogInformation("Sauvegarde de la base InfluxDB de raspberrypi enregistrée : {File} ({Size} octets)", Path.GetFileName(path), backup.Length);
                lastError = null;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // raspberrypi éteint ou injoignable : nouvel essai dans une heure, sans répéter le même message
                if (ex.Message != lastError)
                    logger.LogWarning("Sauvegarde de la base InfluxDB impossible, nouvel essai toutes les heures : {Message}", ex.Message);
                lastError = ex.Message;
            }
        }

        private static async Task<byte[]> DownloadBackup(CancellationToken stoppingToken)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            timeout.CancelAfter(BackupTimeout);

            var keyAuth = new PrivateKeyAuthenticationMethod(User, new PrivateKeyFile(PrivateKeyPath));
            using var client = new SshClient(new ConnectionInfo(HostName, User, keyAuth) { Timeout = TimeSpan.FromSeconds(10) });
            await client.ConnectAsync(timeout.Token);

            // La commande envoyée est ignorée : le serveur exécute la commande forcée de la clé
            using var command = client.CreateCommand("sauvegarde");
            var execution = command.ExecuteAsync(timeout.Token);
            using var output = new MemoryStream();
            await command.OutputStream.CopyToAsync(output, timeout.Token);
            await execution;
            client.Disconnect();

            byte[] backup = output.ToArray();
            if (command.ExitStatus != 0)
                throw new InvalidOperationException($"le script de sauvegarde a échoué (code {command.ExitStatus}) : {command.Error.Trim()}");
            if (backup.Length < 2 || backup[0] != 0x1f || backup[1] != 0x8b) // signature d'un fichier gzip
                throw new InvalidOperationException($"réponse inattendue ({backup.Length} octets), pas un fichier tar.gz");
            return backup;
        }

        private static void DeleteOldBackups()
        {
            foreach (var file in Directory.GetFiles(BackupFolder, "influxdb_*.tar.gz")
                         .OrderByDescending(File.GetLastWriteTimeUtc)
                         .Skip(BackupsKept))
                File.Delete(file);
        }
    }
}
