// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights;
using Microsoft.Extensions.Logging;
using Microsoft.Azure.ServiceBus.Core;

using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Hosting;
using Microsoft.Azure.ServiceBus;
using Microsoft.Azure.Management.Fluent;
using Microsoft.Azure.Management.ResourceManager.Fluent;
using Microsoft.Azure.Cosmos.Table;
using System.Collections.Generic;


namespace OrchestratorFunc
{

    public class EventGridEvent
    {
        public string Id { get; set; }

        public string Topic { get; set; }

        public string Subject { get; set; }

        public string EventType { get; set; }

        public DateTime EventTime { get; set; }

        public object Data { get; set; }
    }

    public class TimerInfo
    {
        public TimerScheduleStatus ScheduleStatus { get; set; }

        public bool IsPastDue { get; set; }
    }

    public class TimerScheduleStatus
    {
        public DateTime Last { get; set; }

        public DateTime Next { get; set; }

        public DateTime LastUpdated { get; set; }
    }


    public static class Orchestrator
    {
        private static String GetSetting(String envVariableName)
        {
            return Environment.GetEnvironmentVariable(envVariableName, EnvironmentVariableTarget.Process);
        }
        private static IAzure azure;
        private static OrchestratorLogic.ContainerInstances.AgentConfig agentConfig;
        private static OrchestratorLogic.ContainerInstances.CommunicationClients communicationClients;
        private static IDictionary<string, Microsoft.FSharp.Core.FSharpResult<Tuple<string, OrchestratorLogic.ContainerInstances.ToolConfig>, string>> toolConfigs;

        private static IEnumerable<Tuple<string, OrchestratorLogic.ContainerInstances.DockerConfig>> dockerConfigs;
        private static IDictionary<string, string> secrets;

        private static IAzure Authenticate() {
            int maxAttempts = 10;
            IAzure azure = null;

            for (int i = 0; i < maxAttempts; i++)
            {
                try
                {
                    var credentials =
                        new Microsoft.Azure.Management.ResourceManager.Fluent.Authentication.AzureCredentialsFactory()
                            .FromServicePrincipal(
                                GetSetting("RAFT_SERVICE_PRINCIPAL_CLIENT_ID"),
                                GetSetting("RAFT_SERVICE_PRINCIPAL_CLIENT_SECRET"),
                                GetSetting("RAFT_SERVICE_PRINCIPAL_TENANT_ID"), AzureEnvironment.AzureGlobalCloud);

                    azure = Microsoft.Azure.Management.Fluent.Azure
                                    .Configure()
                                    .Authenticate(credentials)
                                    .WithSubscription(GetSetting("RAFT_SERVICE_PRINCIPAL_SUBSCRIPTION_ID"));
                }
                catch (Exception)
                {
                    if (i == maxAttempts - 1)
                    {
                        throw;
                    }
                    else {
                        System.Console.Out.WriteLine("Got exception when authenticating, trying again...");
                        System.Threading.Thread.Sleep(3000);
                    }
                }
            }
            return azure;
        }

        private static async Task CreateDefaultTables() {
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(GetSetting("RAFT_STORAGE_TABLE_CONNECTION_STRING"));
            CloudTableClient tableClient = storageAccount.CreateCloudTableClient();
            CloudTable table = tableClient.GetTableReference("WebhooksTriggerTest");
            var result = await table.CreateIfNotExistsAsync();

            table = tableClient.GetTableReference(Raft.StorageEntities.JobStatusTableName);
            result = await table.CreateIfNotExistsAsync();

            table = tableClient.GetTableReference(Raft.StorageEntities.JobTableName);
            result = await table.CreateIfNotExistsAsync();

            table = tableClient.GetTableReference(Raft.StorageEntities.JobWebHookTableName);
            result = await table.CreateIfNotExistsAsync();
        }

        static Orchestrator()
        {
            try
            {
                azure = Authenticate();

                var resourceGroup = GetSetting("RAFT_CONTAINER_RUN_RESOURCE_GROUP");

                var utilsStorageAccount = GetSetting("RAFT_UTILS_STORAGE");
                var utilsStorageAccountKey = OrchestratorLogic.ContainerInstances.getStorageKeyTask(azure, resourceGroup, utilsStorageAccount);
                utilsStorageAccountKey.Wait();

                var resultsStorageAccount = GetSetting("RAFT_RESULTS_STORAGE");
                var resultsStorageAccountKey = OrchestratorLogic.ContainerInstances.getStorageKeyTask(azure, resourceGroup, resultsStorageAccount);
                resultsStorageAccountKey.Wait();


                var metricsKey = GetSetting("RAFT_METRICS_APP_INSIGHTS_KEY");
                var aiKey = GetSetting("RAFT_APPINSIGHTS");

                agentConfig = new OrchestratorLogic.ContainerInstances.AgentConfig(
                        resourceGroup: resourceGroup,
                        keyVault: GetSetting("RAFT_KEY_VAULT"),
                        appInsightsKey: aiKey,
                        outputSas: GetSetting("RAFT_SERVICEBUS_AGENT_SEND_EVENTS_CONNECTION_STRING"),
                        storageTableConnectionString: GetSetting("RAFT_STORAGE_TABLE_CONNECTION_STRING"),
                        eventGridEndpoint: GetSetting("RAFT_EVENT_GRID_ENDPOINT"),
                        eventGridKey: GetSetting("RAFT_EVENT_GRID_KEY"),
                        siteHash: GetSetting("RAFT_SITE_HASH"),

                        utilsStorageAccount: utilsStorageAccount,
                        utilsStorageAccountKey: utilsStorageAccountKey.Result,
                        utilsFileShare: GetSetting("RAFT_UTILS_FILESHARE"),
                        resultsStorageAccount: resultsStorageAccount,
                        resultsStorageAccountKey: resultsStorageAccountKey.Result,

                        networkProfileName: GetSetting("RAFT_NETWORK_PROFILE_NAME"),
                        vNetResourceGroup: GetSetting("RAFT_VNET_RESOURCE_GROUP")
                    );

                var allSecrets = OrchestratorLogic.ContainerInstances.initializeSecretsFromKeyvault(azure, agentConfig);
                allSecrets.Wait();

                secrets = allSecrets.Result.Item1;
                dockerConfigs = allSecrets.Result.Item2;

                communicationClients =
                                new OrchestratorLogic.ContainerInstances.CommunicationClients(
                                    jobEventsSender: new MessageSender(GetSetting("RAFT_REPORT_JOB_STATUS"), Raft.Message.ServiceBus.Topic.events, RetryPolicy.Default),
                                    jobCreationSender: new MessageSender(GetSetting("RAFT_REPORT_JOB_STATUS"), Raft.Message.ServiceBus.Queue.create, RetryPolicy.Default),
                                    jobDeletionSender: new MessageSender(GetSetting("RAFT_REPORT_JOB_STATUS"), Raft.Message.ServiceBus.Queue.delete, RetryPolicy.Default),
                                    webhookSender: new System.Net.Http.HttpClient()
                                );

                //Initialize shared dependencies here
                Raft.Telemetry.Central.Initialize(new TelemetryClient(new Microsoft.ApplicationInsights.Extensibility.TelemetryConfiguration(metricsKey)), agentConfig.SiteHash);

                var createTables = CreateDefaultTables();
                createTables.Wait();

                var tools = OrchestratorLogic.ContainerInstances.initializeTools(agentConfig);
                tools.Wait();
                toolConfigs = tools.Result;
            }
            catch (Exception ex) {
                Raft.Telemetry.Central.Telemetry.TrackError(Raft.Telemetry.TelemetryValues.NewException(ex));
                throw;
            }
        }

        [Function("OnSecretChanged")]
        public static void EventGridKeyVaultEvent([EventGridTrigger] EventGridEvent eventGridEvent, FunctionContext context)
        {
            var log = context.GetLogger("OnSecretChanged");
            log.LogInformation("OnSecretChanged: " + eventGridEvent.Data.ToString());

            azure = Authenticate();
            var allSecrets = OrchestratorLogic.ContainerInstances.initializeSecretsFromKeyvault(azure, agentConfig);
            allSecrets.Wait();

            secrets = allSecrets.Result.Item1;
            dockerConfigs = allSecrets.Result.Item2;

            log.LogInformation("OnSecretChanged: Secrets updated from Key Vault");
        }

        [Function(Raft.Message.ServiceBus.Queue.create)]
        public static async Task CreateJob([ServiceBusTrigger(Raft.Message.ServiceBus.Queue.create, IsSessionsEnabled = true)] string createJobMessage, FunctionContext context)
        {
            await OrchestratorLogic.ContainerInstances.createJob(
                context.GetLogger("JobCreate"),
                secrets,
                dockerConfigs,
                toolConfigs,
                azure,
                agentConfig,
                communicationClients,
                createJobMessage);
        }

        [Function(Raft.Message.ServiceBus.Queue.delete)]
        public static async Task Delete([ServiceBusTrigger(Raft.Message.ServiceBus.Queue.delete, IsSessionsEnabled = true)]string deleteJobMessage, FunctionContext context)
        {
            await OrchestratorLogic.ContainerInstances.delete(
                context.GetLogger("JobDelete"),
                azure,
                agentConfig,
                communicationClients,
                deleteJobMessage);
        }


        [Function("jobstatus-handler")]
        public static async Task Status([ServiceBusTrigger(Raft.Message.ServiceBus.Topic.events, "jobstatus-handler")]string statusMessage, FunctionContext context)
        {
            await OrchestratorLogic.ContainerInstances.status(
                context.GetLogger("JobStatus"),
                azure,
                agentConfig,
                communicationClients,
                statusMessage);
        }

        [Function("webhooks-handler")]
        public static async Task WebhookMessage([ServiceBusTrigger(Raft.Message.ServiceBus.Topic.events, "webhooks-handler")] string webhookMessage, FunctionContext context)
        {
            await OrchestratorLogic.ContainerInstances.webhookMessage(
                context.GetLogger("Webhooks"),
                azure,
                agentConfig,
                communicationClients,
                webhookMessage);
        }

        [Function("raft-timer-garbage-collection")]
        public static async Task TimerGarbageCollection([TimerTrigger("0 */1 * * * *")] TimerInfo t, FunctionContext context)
        {
            await OrchestratorLogic.ContainerInstances.gc(
                context.GetLogger("GC"),
                azure,
                agentConfig,
                communicationClients);
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
