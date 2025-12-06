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
