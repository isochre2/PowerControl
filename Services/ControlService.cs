using Grpc.Core;
using PowerControl;
using Protos;

namespace PowerControl.Services
{
    public class ControlService : Control.ControlBase
    {
        private readonly ILogger<ControlService> _logger;

        private readonly ControlWorker controlWorker;

        public ControlService(ILogger<ControlService> logger, IServiceProvider _controlWorkerProvider)
        {
            controlWorker = _controlWorkerProvider.GetRequiredService<ControlWorker>();
            _logger = logger;
        }

        public override Task<WaterStateReply> GetWaterState(WaterStateRequest request, ServerCallContext context)
        {
            return Task.FromResult(controlWorker.fakeWaterState);
        }

        public override Task<ValveStateReply> GetValveState(ValveStateRequest request, ServerCallContext context)
        {
            _logger.LogInformation("Valve state requested !");
            return Task.FromResult(controlWorker.fakeValveState);
        }
    }
}
