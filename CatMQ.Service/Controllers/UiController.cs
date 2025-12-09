using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NTDLS.CatMQ.Server;
using System.Runtime;

namespace CatMQ.Service.Controllers
{
    [ApiController]
    [Authorize]
    [Route("ui")]
    public class UiController(CMqServer mqServer) : ControllerBase
    {
        /// <summary>
        /// Forces garbage collection with aggressive compaction of the large object heap (LOH).
        /// </summary>
        /// <remarks>This method sets the large object heap compaction mode to <see
        /// cref="GCLargeObjectHeapCompactionMode.CompactOnce"/>  and triggers garbage collection for generation 2
        /// objects using aggressive collection mode. The operation blocks the calling thread and compacts the
        /// memory.</remarks>
        [HttpPost("LohCollect")]
        public IActionResult LohCollect(string queueName, Guid subscriberId)
        {
            // Request LOH compaction on the next full blocking GC.
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;

            // Force a full, blocking, compacting GC of all generations.
            GC.Collect(
                GC.MaxGeneration,
                GCCollectionMode.Aggressive,
                blocking: true,
                compacting: true);

            // Ensure finalizers run and clean up any finalizable objects.
            GC.WaitForPendingFinalizers();

            // Run a second full GC to reclaim anything finalized above.
            GC.Collect(
                GC.MaxGeneration,
                GCCollectionMode.Aggressive,
                blocking: true,
                compacting: true);

            return Ok();
        }

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

        [HttpPost("DeleteAccount/{accountId}")]
        public IActionResult DeleteAccount(Guid accountId)
        {
            var accounts = Configs.GetAccounts();
            accounts.RemoveAll(o => o.Id.Equals(accountId));
            Configs.PutAccounts(accounts);
            return Ok();
        }

        [HttpPost("DeleteAPIKey/{accountId}/{apiKeyId}")]
        public IActionResult DeleteAPIKey(Guid accountId, Guid apiKeyId)
        {
            var accounts = Configs.GetAccounts();

            accounts.Where(o => o.Id.Equals(accountId)).Single()
                .ApiKeys.RemoveAll(o => o.Id == apiKeyId);

            Configs.PutAccounts(accounts);
            return Ok();
        }
    }
}
