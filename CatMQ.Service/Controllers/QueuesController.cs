using CatMQ.Service.ApiDto;
using CatMQ.Service.ApiDto.Extensions;
using Microsoft.AspNetCore.Mvc;
using NTDLS.CatMQ.Server;
using System.Text.Json;

namespace CatMQ.Service.Controllers
{
    [ApiController]
    [Route("api/v1/queues")]
    public class QueuesController : ControllerBase
    {
        private readonly CMqServer _mqServer;

        public QueuesController(CMqServer mqServer)
        {
            _mqServer = mqServer;
        }

        /// <summary>
        /// Enqueue a message onto an existing queue.
        /// POST /api/v1/queues/{queueName}/messages?type=...
        /// </summary>
        [HttpPost("{queueName}/messages")]
        [Consumes("application/json", "text/plain")]
        public IActionResult Enqueue(
            string queueName,
            [FromQuery] string type,
            [FromBody] JsonElement messageJson)
        {
            if (string.IsNullOrWhiteSpace(type))
            {
                return BadRequest("Query parameter 'type' (assembly-qualified type name) is required.");
            }

            string jsonText = messageJson.GetRawText();

            _mqServer.Enqueue(queueName, null, type, jsonText);

            // 202 Accepted is common for async queue work, but 200 OK is also fine.
            return Accepted(new { Message = "Enqueued.", queueName, type });
        }

        /// <summary>
        /// Creates a queue.
        /// POST /api/v1/queues
        /// </summary>
        [HttpPost]
        public IActionResult CreateQueue([FromBody] CMqQueueConfigurationDto config)
        {
            if (config == null || string.IsNullOrEmpty(config.QueueName))
            {
                return BadRequest("QueueName is required.");
            }

            _mqServer.CreateQueue(config.ToDomain());

            return Ok(new { Message = "Queue created successfully.", config.QueueName });
        }

        /// <summary>
        /// Lists all queues.
        /// GET /api/v1/queues
        /// </summary>
        [HttpGet]
        public IActionResult ListQueues()
        {
            var queues = _mqServer.GetQueues()?
                .Select(q => q.ToDto()) ?? [];

            return Ok(queues);
        }

        /// <summary>
        /// Gets a specific queue.
        /// GET /api/v1/queues/{queueName}
        /// </summary>
        [HttpGet("{queueName}")]
        public IActionResult GetQueue(string queueName)
        {
            var queue = _mqServer.GetQueue(queueName);
            if (queue == null)
                return NotFound();

            return Ok(queue.ToDto());
        }

        /// <summary>
        /// Deletes a specific queue.
        /// DELETE /api/v1/queues/{queueName}
        /// </summary>
        [HttpDelete("{queueName}")]
        public IActionResult DeleteQueue(string queueName)
        {
            _mqServer.DeleteQueue(queueName);
            return NoContent();
        }

        /// <summary>
        /// Deletes all messages in from a queue.
        /// DELETE /api/v1/queues/{queueName}/messages
        /// </summary>
        [HttpDelete("{queueName}/messages")]
        public IActionResult PurgeQueue(string queueName)
        {
            _mqServer.PurgeQueue(queueName);
            return NoContent();
        }
    }
}
