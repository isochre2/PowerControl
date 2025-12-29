using Protos;
using System.Diagnostics;
using System.Device.Gpio;

namespace PowerControl
{
    public class ControlWorker : BackgroundService
    {
        private readonly ILogger<ControlWorker> logger;
        public WaterStateReply fakeWaterState { get; set; } = new WaterStateReply();
        public ValveStateReply fakeValveState { get; set; } = new ValveStateReply();

        public static bool initialized = false;

        private static GpioController gpioController;

        private List<int> gpioNumbers = new List<int>() { 4, 17, 18, 22, 23, 24 };

        Stopwatch debugStopwatch = new Stopwatch();

        public ControlWorker(ILogger<ControlWorker> _logger)
        {
            logger = _logger;
            ;
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
            foreach (var pinNumber in gpioNumbers)
            {
                gpioController.OpenPin(pinNumber, PinMode.InputPullUp);
            }

            initialized = true;
            var cts = new CancellationTokenSource();
            Task.Run(async () =>
            {
                while (!cts.IsCancellationRequested)
                {
                    foreach (var pinNumber in gpioNumbers)
                    {
                        Console.WriteLine("GPIO " + pinNumber + " : " + gpioController.Read(pinNumber));
                    }

                    Console.WriteLine("------------------------------------------------------");
                    await Task.Delay(1000, cts.Token);
                }
            }, cts.Token);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            //TODO : en fonction du initialised, lire les GPIO ou simuler
            fakeWaterState.WaterDown = true;
            fakeWaterState.WaterUp = true;
            fakeValveState.ValveDown = true;
            fakeValveState.ValveUp = false;
            while (!stoppingToken.IsCancellationRequested)
            {
                if (logger.IsEnabled(LogLevel.Information))
                {
                    if (debugStopwatch.ElapsedMilliseconds >
                        5000) //toutes les Xmin on met à jour artificielement l'état de l'unité de contrôle
                    {
                        debugStopwatch.Restart();
                        Console.WriteLine("Updating fake valve state and fake water state fakeWaterState.WaterDown = " +
                                          fakeWaterState.WaterDown);

                        if (fakeWaterState.WaterDown && fakeWaterState.WaterUp)
                        {
                            fakeWaterState.WaterDown = false;
                            fakeWaterState.WaterUp = true;


                            if (fakeValveState.ValveDown)
                            {
                                fakeValveState.ValveDown = false;
                                fakeValveState.ValveUp = true;
                            }
                            else if (fakeValveState.ValveUp)
                            {
                                fakeValveState.ValveDown = true;
                                fakeValveState.ValveUp = false;
                            }
                        }
                        else if (!fakeWaterState.WaterDown && fakeWaterState.WaterUp)
                        {
                            fakeWaterState.WaterDown = false;
                            fakeWaterState.WaterUp = false;
                        }
                        else if (!fakeWaterState.WaterDown && !fakeWaterState.WaterUp)
                        {
                            fakeWaterState.WaterDown = true;
                            fakeWaterState.WaterUp = true;
                        }

                        await Task.Delay(10, stoppingToken);
                    }
                }
            }
        }
    }
}