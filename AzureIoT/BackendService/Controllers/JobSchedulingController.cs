using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Devices;
using Microsoft.Extensions.Options;

namespace BackendService.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class JobSchedulingController : ControllerBase
    {
        private readonly JobClient _jobClient;
        private readonly IotSettings _iotSettings;

        public JobSchedulingController(JobClient jobClient, IOptions<IotSettings> iotSettings)
        {
            _jobClient = jobClient;
            _iotSettings = iotSettings.Value;
        }

        [HttpGet("GetJobStatus")]
        public async Task<JobResponse?> GetJobStatus(string jobId)
        {
            var result = await _jobClient.GetJobAsync(jobId);
            return result;
        }

        [HttpPost("StartIsAliveJob")]
        public async Task<JobResponse?> StartIsAliveJob()
        {
            var directMethod = new CloudToDeviceMethod("IsAlive", TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));

            var result = await _jobClient.ScheduleDeviceMethodAsync(Guid.NewGuid().ToString(), 
                //$"DeviceId IN ['{_iotSettings.DeviceId}']",
                "tags.location IN ['EU', 'US']",
                directMethod,
                DateTime.UtcNow, 
                (long)TimeSpan.FromMinutes(2).TotalSeconds);
            return result;
        }

        [HttpPost("StartLongRunningMethodJob")]
        public async Task<JobResponse?> StartLongRunningMethodJob()
        {
            var directMethod = new CloudToDeviceMethod("StartLongRunning", TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));

            var result = await _jobClient.ScheduleDeviceMethodAsync(Guid.NewGuid().ToString(),
                $"DeviceId IN ['{_iotSettings.DeviceId}']",
                directMethod,
                DateTime.UtcNow,
                (long)TimeSpan.FromMinutes(2).TotalSeconds);
            return result;
        }
    }
}
