using System;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
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

        [Function("webhooks-trigger-test")]
        public static async Task<HttpResponseData> WebhookTriggerTest([HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = "test")] HttpRequestData request, FunctionContext executionContext)
        {
            var log = executionContext.GetLogger("WebhooksTrigger");

            var query = System.Web.HttpUtility.ParseQueryString(request.Url.Query);
            string jobId = query["jobId"];

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
                        var response = request.CreateResponse(HttpStatusCode.OK);
                        await response.WriteAsJsonAsync(r.Item2);
                        return response;
                    }
                    else
                    {
                        await TestInfraLogic.WebhooksTest.post(log, StorageTableConnectionString, text);
                        return request.CreateResponse(HttpStatusCode.OK);
                    }
                }
                else
                {
                    log.LogWarning($"Unhandled request method {request.Method} when job id is not set");
                    return request.CreateResponse(HttpStatusCode.BadRequest);
                }
            }
            else
            {
                if (request.Method.ToLowerInvariant() == "get")
                {
                    log.LogInformation($"Getting webhook messages for job {jobId}");
                    var results = await TestInfraLogic.WebhooksTest.get(log, StorageTableConnectionString, jobId);
                    var response = request.CreateResponse(HttpStatusCode.OK);
                    await response.WriteAsJsonAsync(results);
                    return response;
                }
                else
                {
                    log.LogWarning($"Unhandled request method {request.Method} when job id is {jobId}");

                    return request.CreateResponse(HttpStatusCode.BadRequest);
                }
            }
        }
    }

    public class Program
    {
        public static void Main()
        {
            var host = new HostBuilder()
                .ConfigureFunctionsWorkerDefaults()
                .Build();

            host.Run();
        }
    }

}
