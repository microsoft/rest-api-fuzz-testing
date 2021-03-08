using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace TestInfraFunc
{
    public static class TestInfra
    {
        private static String GetSetting(String envVariableName)
        {
            return Environment.GetEnvironmentVariable(envVariableName, EnvironmentVariableTarget.Process);
        }

        static private string StorageTableConnectionString = GetSetting("RAFT_STORAGE_TABLE_CONNECTION_STRING");

        [FunctionName("webhooks-trigger-test")]
        public static async Task<IActionResult> WebhookTriggerTest([HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = "test")] HttpRequest request, ILogger log)
        {
            string jobId = request.Query["jobId"];

            if (string.IsNullOrEmpty(jobId))
            {
                if (request.Method.ToLowerInvariant() == "post")
                {
                    var s = new System.IO.StreamReader(request.Body);
                    var text = await s.ReadToEndAsync();
                    log.LogInformation($"Body: {text}");
                    //https://docs.microsoft.com/en-us/azure/event-grid/webhook-event-delivery

                    var r = await TestInfraLogic.WebhooksTest.validation(log, text);
                    if (r.Item1)
                    {
                        log.LogInformation($"Returning validation code {r.Item2}");
                        return new OkObjectResult(r.Item2);
                    }
                    else
                    {
                        await TestInfraLogic.WebhooksTest.post(log, StorageTableConnectionString, text);
                        return new OkResult();
                    }
                }
                else
                {
                    log.LogWarning($"Unhandled request method {request.Method} when job id is not set");
                    return new BadRequestResult();
                }
            }
            else
            {
                if (request.Method.ToLowerInvariant() == "get")
                {
                    log.LogInformation($"Getting webhook messages for job {jobId}");
                    var results = await TestInfraLogic.WebhooksTest.get(log, StorageTableConnectionString, jobId);
                    return new OkObjectResult(results);
                }
                else
                {
                    log.LogWarning($"Unhandled request method {request.Method} when job id is {jobId}");
                    return new BadRequestResult();
                }
            }
        }
    }
}
