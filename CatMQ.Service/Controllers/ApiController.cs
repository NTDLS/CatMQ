using Microsoft.AspNetCore.Mvc;
using NTDLS.CatMQ.Server;
using NTDLS.CatMQ.Shared;

namespace CatMQ.Service.Controllers
{
    [Route("api")]
    public class ApiController : Controller
    {
        private readonly CMqServer _mqServer;

        public ApiController(CMqServer mqServer)
        {
            _mqServer = mqServer;
        }

        [HttpPost("Enqueue/{queueName}/{assemblyQualifiedTypeName}")]
        [Consumes("text/plain", "application/json")]
        public IActionResult Enqueue(string queueName, string assemblyQualifiedTypeName, [FromBody] dynamic messageJson)
        {
            string jsonText = messageJson.ToString();
            _mqServer.EnqueueMessage(queueName, null, assemblyQualifiedTypeName, jsonText);
            return Ok();
        }

        /// <summary>
        /// Creates a queue with the default configuration.
        /// </summary>
        /// <param name="queueName"></param>
        /// <returns></returns>
        [HttpPost("CreateQueue/{queueName}")]
        public IActionResult CreateQueue(string queueName)
        {
            _mqServer.CreateQueue(new CMqQueueConfiguration
            {
                QueueName = queueName
            });
            return Ok();
        }

        /// <summary>
        /// Creates a queue with a custom configuration.
        /// </summary>
        /// <param name="config"></param>
        /// <returns></returns>
        [HttpPost("CreateQueue")]
        public IActionResult CreateQueue([FromBody] CMqQueueConfiguration config)
        {
            if (config == null || string.IsNullOrEmpty(config.QueueName))
            {
                return BadRequest("QueueName is required.");
            }

            _mqServer.CreateQueue(config);

            return Ok(new { Message = "Queue created successfully.", config.QueueName });
        }

        [HttpDelete("Purge/{queueName}")]
        public IActionResult Purge(string queueName)
        {
            _mqServer.PurgeQueue(queueName);
            return Ok();
        }


        [HttpDelete("DeleteQueue/{queueName}")]
        public IActionResult DeleteQueue(string queueName)
        {
            _mqServer.DeleteQueue(queueName);
            return Ok();
        }
    }
}
