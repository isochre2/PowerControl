using Microsoft.Extensions.Logging;
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

        private static GpioController gpioController = new GpioController();

        private List<int> gpioNumbers = new List<int>() { 4, 17, 18, 22, 23, 24};

        Stopwatch debugStopwatch = new Stopwatch();

        public ControlWorker(ILogger<ControlWorker> _logger)
        {
            logger = _logger;
            debugStopwatch.Start();
            InitGpios();
        }

        private void InitGpios()
        {
            foreach (var pinNumber in gpioNumbers)
            {
                gpioController.OpenPin(pinNumber, PinMode.InputPullUp);
            }

            Task.Run(() =>
            {
                while (true)
                {
                    foreach (var pinNumber in gpioNumbers)
                    {
                        Console.WriteLine("GPIO " + pinNumber + " : " + gpioController.Read(pinNumber));
                    }
                    Console.WriteLine("------------------------------------------------------");
                    Thread.Sleep(1000);
                }

            });
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            fakeWaterState.WaterDown = true;
            fakeWaterState.WaterUp = true;
            fakeValveState.ValveDown = true;
            fakeValveState.ValveUp = false;
            while (!stoppingToken.IsCancellationRequested)
            {
                if (logger.IsEnabled(LogLevel.Information))
                {
                    if (debugStopwatch.ElapsedMilliseconds > 5000) //toutes les Xmin on met à jour artificielement l'état de l'unité de contrôle
                    {
                        debugStopwatch.Restart();
                        Console.WriteLine("Updating fake valve state and fake water state fakeWaterState.WaterDown = " + fakeWaterState.WaterDown);

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

