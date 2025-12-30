using Renci.SshNet;
using System.Diagnostics;
using System.Device.Gpio;
using ConnectionInfo = Renci.SshNet.ConnectionInfo;

namespace PowerControl.Services;

public class ShutdownWorker : BackgroundService
{
    private SshClient _sshClient;

    private static GpioController gpioController;

    private readonly ILogger<ControlWorker> logger;

    public static bool initialized = false;

    Stopwatch debugStopwatch = new Stopwatch();

    private const int SHUTDOWN_GPIO = 24 /*25*/;

    public ShutdownWorker(ILogger<ControlWorker> _logger)
    {
        logger = _logger;
        debugStopwatch.Start();
        try
        {
            InitGpios();
        }
        catch (Exception e)
        {
            Console.WriteLine("Impossible d'initialiser les GPIOs de contrôle : \n" + e.Message);
            //throw;
        }
    }

    private void InitGpios()
    {
        gpioController = new GpioController();
        gpioController.OpenPin(SHUTDOWN_GPIO, PinMode.InputPullUp);
        Connect("raspberrypi", "isochre");
        Connect("raspberrypi.local", "isochre");
        
        initialized = true;
        
        var cts = new CancellationTokenSource();
        Task.Run(async () =>
        {
            while (!cts.IsCancellationRequested)
            {
                Console.WriteLine("GPIO " + SHUTDOWN_GPIO + " : " + gpioController.Read(SHUTDOWN_GPIO));
                Console.WriteLine("------------------------------------------------------");
                await Task.Delay(1000, cts.Token);
            }
        }, cts.Token);
    }

    public void Connect(string host, string username)
    {
        try
        {
            string privateKeyPath = "/app/ssh_keys/id_rsa_shutdown";
            var keyFile = new PrivateKeyFile(privateKeyPath);
            var keyAuth = new PrivateKeyAuthenticationMethod(username, keyFile);
            var connectionInfo = new ConnectionInfo(host, username, keyAuth);
            _sshClient = new SshClient(connectionInfo);
            _sshClient.Connect();
            Console.WriteLine($"Connexion SSH établie à {host} avec succès !");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erreur lors de la connexion SSH : {ex.Message}");
        }
    }

    public void Disconnect()
    {
        if (_sshClient != null && _sshClient.IsConnected)
        {
            _sshClient.Disconnect();
            Console.WriteLine("Connexion SSH fermée.");
        }
    }

    public string ExecuteCommand(string command)
    {
        if (_sshClient != null && _sshClient.IsConnected)
        {
            var cmd = _sshClient.RunCommand(command);
            return cmd.Result;
        }

        return "Non connecté.";
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                debugStopwatch.Restart();
                //On vérifie l'état de 
                if (gpioController.Read(SHUTDOWN_GPIO) == PinValue.Low)
                {
                    var result = ExecuteCommand("ls -l");
                    Console.WriteLine(result);
                }

                await Task.Delay(100, stoppingToken);
            }
        }
    }
}