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
        /// POST /api/v1/queues/{queueName}/messages?type=...&amp;deferSeconds=...
        /// </summary>
        [HttpPost("{queueName}/messages")]
        [Consumes("application/json", "text/plain")]
        public IActionResult Enqueue(
            string queueName,
            [FromQuery] string type,
            [FromBody] JsonElement messageJson,
            [FromQuery] int? deferSeconds = null)
        {
            if (string.IsNullOrWhiteSpace(type))
            {
                return BadRequest(Localize("Query parameter 'type' (assembly-qualified type name) is required."));
            }
            if (deferSeconds < 0)
            {
                return BadRequest(Localize("Query parameter 'deferSeconds' cannot be negative."));
            }
            if (_mqServer.GetQueue(queueName) == null)
            {
                return NotFound(Localize("Queue not found: [{0}].", queueName));
            }

            TimeSpan? deferDuration = deferSeconds > 0 ? TimeSpan.FromSeconds(deferSeconds.Value) : null;

            _mqServer.Enqueue(queueName, deferDuration, type, messageJson.GetRawText());

            // The message is accepted for delivery, but delivery to subscribers happens asynchronously.
            return Accepted(new { Message = Localize("Enqueued."), queueName, type });
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
                return BadRequest(Localize("QueueName is required."));
            }

            if (_mqServer.GetQueue(config.QueueName) != null)
            {
                return Conflict(Localize("Queue already exists: [{0}].", config.QueueName));
            }

            try
            {
                _mqServer.CreateQueue(config.ToDomain());
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }

            return CreatedAtAction(nameof(GetQueue), new { queueName = config.QueueName },
                new { Message = Localize("Queue created successfully."), config.QueueName });
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
        /// Deletes a specific queue. Deleting a queue that does not exist is not an error.
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
            if (_mqServer.GetQueue(queueName) == null)
            {
                return NotFound(Localize("Queue not found: [{0}].", queueName));
            }

            _mqServer.PurgeQueue(queueName);
            return NoContent();
        }
    }
}
