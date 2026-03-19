using CatMQ.Service.Models.Page;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NTDLS.CatMQ.Server;

namespace CatMQ.Service.Pages
{
    [Authorize]
    public class InstrumentationModel(ILogger<QueuesModel> logger, CMqServer mqServer) : BasePageModel
    {
        private readonly ILogger<QueuesModel> _logger = logger;

        public void OnGet()
        {
            try
            {
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex.Message);
                ErrorMessage = ex.Message;
            }
        }

        public JsonResult OnGetInstrumentation()
        {
            var instrumentation = mqServer.InstrumentationSnapshot();
            return new JsonResult(instrumentation);
        }
    }
}
