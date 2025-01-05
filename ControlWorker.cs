using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace PowerControl
{
    public class ControlWorker : BackgroundService
    {
        private readonly ILogger<ControlWorker> logger;

        Stopwatch debugStopwatch = new Stopwatch();

        public ControlWorker(ILogger<ControlWorker> _logger)
        {
            logger = _logger;
            debugStopwatch.Start();
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                if (logger.IsEnabled(LogLevel.Information))
                {
                    if (debugStopwatch.ElapsedMilliseconds > 300000) //toutes les 5min on met à jour artificielement l'état de l'unité de contrôle
                    {
                        debugStopwatch.Restart();
                        Console.WriteLine("debugStopwatch elapsed");
                    }
                }
                await Task.Delay(10, stoppingToken);
            }
        }
    }
}

