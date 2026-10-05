using System.ComponentModel.DataAnnotations;
using Renci.SshNet;
using Renci.SshNet.Common;
using System.Diagnostics;
using System.Device.Gpio;
using Iot.Device.Camera.Settings;
using ConnectionInfo = Renci.SshNet.ConnectionInfo;

namespace PowerControl.Services;

public class ShutdownWorker : BackgroundService
{
    private class LocalSSHClient
    {
        public bool? IsConnected => SSHClient?.IsConnected;
        
        private SshClient SSHClient { get; set; }

        private string PrivateKeyPath => "/app/ssh_keys/id_rsa_shutdown";

        [Required]
        public string HostName { get; set; }

        [Required]
        public string User { get; set; }

        public bool Connect()
        {
            try
            {
                var keyFile = new PrivateKeyFile(PrivateKeyPath);
                var keyAuth = new PrivateKeyAuthenticationMethod(User, keyFile);
                var connectionInfo = new ConnectionInfo(HostName, User, keyAuth) { Timeout = TimeSpan.FromSeconds(5) };
                SSHClient = new SshClient(connectionInfo);
                SSHClient.Connect();
                Console.WriteLine($"Connexion SSH établie à {HostName} avec succès : " + SSHClient.IsConnected);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erreur lors de la connexion SSH à {HostName} : {ex.Message}");
            }

            return true;
        }

        // Renvoie true si la commande a été remise au Pi (y compris s'il a coupé la connexion en s'éteignant)
        public bool ExecuteCommand(string command, out string commandOutput, out string errorOutput)
        {
            errorOutput = "";
            commandOutput = "";
            if (SSHClient == null || !SSHClient.IsConnected)
            {
                errorOutput = "Client SSH non connecté";
                return false;
            }

            try
            {
                using var cmd = SSHClient.CreateCommand(command);
                cmd.CommandTimeout = TimeSpan.FromSeconds(5);
                commandOutput = cmd.Execute();
                return true;
            }
            catch (SshConnectionException ex)
            {
                // Le Pi a coupé la connexion pendant la commande : cas normal d'un shutdown, l'ordre est passé
                errorOutput = ex.Message;
                SSHClient = null;
                return true;
            }
            catch (Exception ex)
            {
                // Liaison morte (timeout) : l'ordre n'est pas passé, l'appelant réessaiera.
                // Pas de Disconnect() ici : sur une liaison morte, il pourrait bloquer.
                errorOutput = ex.Message;
                SSHClient = null;
                return false;
            }
        }

        public void Disconnect()
        {
            if (SSHClient != null && SSHClient.IsConnected)
            {
                SSHClient.Disconnect();
                Console.WriteLine("Connexion SSH fermée.");
            }
        }
    }

    private LocalSSHClient RaspberryControl = new() { HostName = "raspberrypicontrol.local", User = "isochre" };
    private LocalSSHClient RaspberryPower = new() { HostName = "raspberrypi.local", User = "isochre" };


    private static GpioController gpioController;

    private readonly ILogger<ControlWorker> logger;

    private const int SHUTDOWN_GPIO = 25;

    // L'Arduino coupe le 12V 5 min après le signal : on laisse 4 min pour joindre P
    // (le réseau CPL peut revenir après une coupure courte), puis 1 min à C pour s'arrêter.
    private static readonly TimeSpan PowerOrderDeadline = TimeSpan.FromMinutes(4);

    private DateTime? shutdownTriggeredAt;
    private bool powerOrderDelivered;
    private string lastPowerError;

    public ShutdownWorker(ILogger<ControlWorker> _logger)
    {
        logger = _logger;

        InitGpios();

        var cts = new CancellationTokenSource();
        Task.Run(async () =>
        {
            while (!cts.IsCancellationRequested)
            {
                if ((RaspberryControl?.IsConnected) is not true) RaspberryControl?.Connect();
                if (RaspberryPower?.IsConnected is not true) RaspberryPower?.Connect();
                await Task.Delay(1000, cts.Token);
            }
        }, cts.Token);
    }

    private void InitGpios()
    {
        try
        {
            gpioController = new GpioController();
            gpioController.OpenPin(SHUTDOWN_GPIO, PinMode.InputPullUp);
        }
        catch (Exception e)
        {
            gpioController = null;
            Console.WriteLine("Impossible d'initialiser les GPIOs de contrôle : \n" + e.Message);
        }
    }


    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                //On vérifie l'état de 
                // Une fois déclenchée, la séquence va jusqu'au bout (comme côté Arduino)
                if (shutdownTriggeredAt == null && gpioController?.Read(SHUTDOWN_GPIO) == PinValue.Low)
                {
                    shutdownTriggeredAt = DateTime.UtcNow;
                    Console.WriteLine("Signal de coupure reçu : séquence d'arrêt engagée.");
                }

                if (shutdownTriggeredAt != null)
                {
                    if (!powerOrderDelivered)
                    {
                        powerOrderDelivered = RaspberryPower.ExecuteCommand(
                            "echo \"Commande d'arrêt reçue le $(date)\" >> shutdown_log.txt && sudo shutdown -h now",
                            out string commandOutputPower,
                            out string errorOutputPower);
                        if (powerOrderDelivered)
                            Console.WriteLine("Ordre d'arrêt remis à P.");
                        else if (errorOutputPower != lastPowerError)
                            Console.WriteLine($"Ordre d'arrêt vers P non remis, nouvelle tentative : {errorOutputPower}");
                        lastPowerError = errorOutputPower;
                    }

                    bool deadlineReached = DateTime.UtcNow - shutdownTriggeredAt >= PowerOrderDeadline;
                    if (powerOrderDelivered || deadlineReached)
                    {
                        if (!powerOrderDelivered)
                            Console.WriteLine("Délai dépassé : P n'a pas pu être joint, arrêt de C quand même.");
                        RaspberryControl.ExecuteCommand(
                            "echo \"Commande d'arrêt reçue le $(date)\" >> shutdown_log.txt && sudo shutdown -h now",
                            out string commandOutputControl,
                            out string errorOutputControl);
                    }
                }

                await Task.Delay(100, stoppingToken);
            }
        }
    }
}