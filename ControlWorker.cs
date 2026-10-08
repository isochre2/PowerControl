using Protos;
using System.Diagnostics;
using System.Device.Gpio;

namespace PowerControl
{
    public class ControlWorker : BackgroundService
    {
        private readonly ILogger<ControlWorker> logger;

        // Remplacés en bloc (jamais modifiés en place) quand on lit les vrais GPIO : le
        // service gRPC renvoie toujours un état cohérent
        public WaterStateReply WaterState { get; private set; } = new WaterStateReply();
        public ValveStateReply ValveState { get; private set; } = new ValveStateReply();

        private static GpioController gpioController;

        // Entrées avec pull-up, tirées à la masse quand le capteur s'active : Low = actif
        private const int GPIO_VALVE_UP = 4;
        private const int GPIO_VALVE_DOWN = 17;
        private const int GPIO_WATER_UP = 18;
        private const int GPIO_WATER_DOWN = 22;
        private const int GPIO_VALVE_UPWARD = 23;
        private const int GPIO_VALVE_DOWNWARD = 24;

        private static readonly Dictionary<int, string> gpioLabels = new()
        {
            { GPIO_VALVE_UP, "Vanne en haut" },
            { GPIO_VALVE_DOWN, "Vanne en bas" },
            { GPIO_WATER_UP, "Eau haute" },
            { GPIO_WATER_DOWN, "Eau basse" },
            { GPIO_VALVE_UPWARD, "Vanne en montée" },
            { GPIO_VALVE_DOWNWARD, "Vanne en descente" },
        };

        private readonly Dictionary<int, bool> lastActive = new();

        Stopwatch debugStopwatch = new Stopwatch();

        public ControlWorker(ILogger<ControlWorker> _logger)
        {
            logger = _logger;
            debugStopwatch.Start();

            InitGpios();
        }

        private void InitGpios()
        {
            try
            {
                gpioController = new GpioController();
                foreach (var pinNumber in gpioLabels.Keys)
                {
                    gpioController.OpenPin(pinNumber, PinMode.InputPullUp);
                }
            }
            catch (Exception e)
            {
                gpioController = null;
                Console.WriteLine("Impossible d'initialiser les GPIOs de contrôle : \n" + e.Message);
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            Console.WriteLine(gpioController != null
                ? "État de la vanne et de l'eau : lecture des GPIO réels"
                : "État de la vanne et de l'eau : SIMULATION (GPIO indisponibles)");

            if (gpioController == null)
            {
                ValveState.ValveDown = true;
                WaterState.WaterDown = true;
                WaterState.WaterUp = true;
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                if (gpioController != null)
                    ReadGpios();
                else
                    UpdateSimulation();

                // Pause à chaque tour : sans elle la boucle occupe un cœur à 100 %
                await Task.Delay(100, stoppingToken);
            }
        }

        private void ReadGpios()
        {
            ValveState = new ValveStateReply
            {
                ValveUp = IsActive(GPIO_VALVE_UP),
                ValveDown = IsActive(GPIO_VALVE_DOWN),
                ValveUpward = IsActive(GPIO_VALVE_UPWARD),
                ValveDownward = IsActive(GPIO_VALVE_DOWNWARD),
            };
            WaterState = new WaterStateReply
            {
                WaterUp = IsActive(GPIO_WATER_UP),
                WaterDown = IsActive(GPIO_WATER_DOWN),
            };
        }

        private bool IsActive(int pinNumber)
        {
            bool active = gpioController.Read(pinNumber) == PinValue.Low;

            // Seulement les changements d'état : limite les écritures sur la carte SD
            if (!lastActive.TryGetValue(pinNumber, out var last) || last != active)
            {
                Console.WriteLine($"{gpioLabels[pinNumber]} (GPIO {pinNumber}) : {(active ? "actif" : "inactif")}");
                lastActive[pinNumber] = active;
            }
            return active;
        }

        // Sans GPIO (ex : développement sur PC) : cycle artificiel toutes les 5 s
        private void UpdateSimulation()
        {
            if (debugStopwatch.ElapsedMilliseconds <= 5000)
                return;
            debugStopwatch.Restart();

            if (WaterState.WaterDown && WaterState.WaterUp)
            {
                WaterState.WaterDown = false;
                WaterState.WaterUp = true;

                if (ValveState.ValveDown)
                {
                    ValveState.ValveDown = false;
                    ValveState.ValveUp = true;
                }
                else if (ValveState.ValveUp)
                {
                    ValveState.ValveDown = true;
                    ValveState.ValveUp = false;
                }
            }
            else if (!WaterState.WaterDown && WaterState.WaterUp)
            {
                WaterState.WaterDown = false;
                WaterState.WaterUp = false;
            }
            else if (!WaterState.WaterDown && !WaterState.WaterUp)
            {
                WaterState.WaterDown = true;
                WaterState.WaterUp = true;
            }
        }
    }
}
