using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace PowerControl
{
    public class ControlWorker(ILogger<ControlWorker> logger) : BackgroundService
    {
        Stopwatch debugStopwatch = new Stopwatch();

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

