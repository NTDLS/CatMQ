using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NTDLS.CatMQ.Server;

namespace CatMQ.Service.Controllers
{
    [Authorize]
    [Route("ui")]
    public class UiController(CMqServer mqServer) : Controller
    {
        [HttpPost("Unsubscribe/{queueName}/{subscriberId}")]
        public IActionResult Unsubscribe(string queueName, Guid subscriberId)
        {
            mqServer.Unsubscribe(subscriberId, queueName);
            return Ok();
        }

        [HttpPost("PurgeQueue/{queueName}")]
        public IActionResult PurgeQueue(string queueName)
        {
            mqServer.PurgeQueue(queueName);
            return Ok();
        }

        [HttpPost("DeleteQueue/{queueName}")]
        public IActionResult DeleteQueue(string queueName)
        {
            mqServer.DeleteQueue(queueName);
            return Ok();
        }
    }
}
