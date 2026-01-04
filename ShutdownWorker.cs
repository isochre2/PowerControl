using System.ComponentModel.DataAnnotations;
using Renci.SshNet;
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
                var connectionInfo = new ConnectionInfo(HostName, User, keyAuth);
                SSHClient = new SshClient(connectionInfo);
                SSHClient.Connect();
                Console.WriteLine($"Connexion SSH établie à {HostName} avec succès : " + SSHClient.IsConnected);
                ExecuteCommand("echo \"Connexion établie le $(date)\" >> shutdown_log.txt",
                    out string CommandOutputType, out string errorOutput);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erreur lors de la connexion SSH à {HostName} : {ex.Message}");
            }

            return true;
        }

        public bool ExecuteCommand(string command, out string commandOutput, out string errorOutput)
        {
            errorOutput = "";
            commandOutput = "";
            if (SSHClient == null || !SSHClient.IsConnected)
            {
                errorOutput = "Client SSH non connecté";
                return false;
            }

            var cmd = SSHClient.RunCommand(command);
            commandOutput = cmd.Result;
            return true;
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

    private LocalSSHClient RaspberryControl = new() { HostName = "raspberrypicontrol", User = "isochre" };
    private LocalSSHClient RaspberryPower = new() { HostName = "raspberrypi", User = "isochre" };


    private static GpioController gpioController;

    private readonly ILogger<ControlWorker> logger;

    private const int SHUTDOWN_GPIO = 25;

    public ShutdownWorker(ILogger<ControlWorker> _logger)
    {
        logger = _logger;

        InitGpios();

        var cts = new CancellationTokenSource();
        Task.Run(async () =>
        {
            while (!cts.IsCancellationRequested)
            {
                
                //Console.WriteLine("(RaspberryControl?.SSHClient?.IsConnected) : " + RaspberryControl.SSHClient.IsConnected.ToString());
                if ((RaspberryControl?.IsConnected) is not true) RaspberryControl?.Connect();
                if (RaspberryPower?.IsConnected is not true) RaspberryPower?.Connect();


                Console.WriteLine("GPIO " + SHUTDOWN_GPIO + " : " + (gpioController?.Read(SHUTDOWN_GPIO).ToString() ?? "Unknown"));
                Console.WriteLine("------------------------------------------------------");
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
                if (gpioController?.Read(SHUTDOWN_GPIO) == PinValue.Low)
                {
                    var shutdownCommandResult = RaspberryPower.ExecuteCommand(
                        "echo \"Commande d'arrêt reçue le $(date)\" >> shutdown_log.txt && sudo shutdown -h now",
                        out string errorOutputPower,
                        out string commandOutputPower);
                    Console.WriteLine(errorOutputPower);
                    if (shutdownCommandResult)
                    {
                        RaspberryControl.ExecuteCommand(
                            "echo \"Commande d'arrêt reçue le $(date)\" >> shutdown_log.txt && sudo shutdown -h now",
                            out string errorOutputControl,
                            out string commandOutputControl);
                    }
                }

                await Task.Delay(100, stoppingToken);
            }
        }
    }
}