using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NTDLS.CatMQ.Server;

namespace CatMQ.Service.Pages
{
    [Authorize]
    public class InstrumentationModel(ILogger<QueuesModel> logger, CMqServer mqServer)
        : PageModel
    {
        public string? ErrorMessage { get; set; }

        public JsonResult OnGetInstrumentation()
        {
            try
            {
                var instrumentation = mqServer.InstrumentationSnapshot();
                return new JsonResult(instrumentation);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error fetching instrumentation data.");

                return new JsonResult(new
                {
                    error = true,
                    message = ex.Message
                })
                {
                    StatusCode = 500
                };
            }
        }
    }
}
