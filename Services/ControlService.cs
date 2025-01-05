using Grpc.Core;
using PowerControl;
using Protos;

namespace PowerControl.Services
{
    public class ControlService : Control.ControlBase
    {
        private readonly ILogger<ControlService> _logger;
        public ControlService(ILogger<ControlService> logger)
        {
            _logger = logger;
        }

        public override Task<WaterStateReply> GetWaterState(WaterStateRequest request, ServerCallContext context)
        {
            return Task.FromResult(new WaterStateReply
            {
                //Message = "Hello " + request.Name
            });
        }

        public override Task<ValveStateReply> GetValveState(ValveStateRequest request, ServerCallContext context)
        {
            return Task.FromResult(new ValveStateReply
            {
                //Message = "Hello " + request.Name
            });
        }
    }
}
