// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights;
using Microsoft.Extensions.Logging;
using Microsoft.Azure.ServiceBus.Core;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.ServiceBus;
using Microsoft.Azure.Management.Fluent;
using Microsoft.Azure.Management.ResourceManager.Fluent;
using Microsoft.Azure.Cosmos.Table;
using System.Collections.Generic;
using Microsoft.Azure.Management.AppService.Fluent.Models;
using Microsoft.Azure.Management.Monitor.Fluent.Models;

namespace OrchestratorFunc
{
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
                        resultsStorageAccountKey: resultsStorageAccountKey.Result
                    );

                var allSecrets = OrchestratorLogic.ContainerInstances.initializeSecretsFromKeyvault(azure, agentConfig);
                allSecrets.Wait();

                secrets = allSecrets.Result.Item1;
                dockerConfigs = allSecrets.Result.Item2;

                communicationClients =
                                new OrchestratorLogic.ContainerInstances.CommunicationClients(
                                    jobEventsSender: new MessageSender(GetSetting("RAFT_REPORT_JOB_STATUS"), Raft.Message.ServiceBus.Topic.events, RetryPolicy.Default),
                                    jobCreationSender: new MessageSender(GetSetting("RAFT_REPORT_JOB_STATUS"), Raft.Message.ServiceBus.Queue.create, RetryPolicy.Default),
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

        [FunctionName(Raft.Message.ServiceBus.Queue.create)]
        public static async Task CreateJob([ServiceBusTrigger(Raft.Message.ServiceBus.Queue.create, IsSessionsEnabled = true)] string createJobMessage, ILogger log)
        {
            await OrchestratorLogic.ContainerInstances.createJob(
                log,
                secrets,
                dockerConfigs,
                toolConfigs,
                azure,
                agentConfig,
                communicationClients,
                createJobMessage);
        }

        [FunctionName(Raft.Message.ServiceBus.Queue.delete)]
        public static async Task Delete([ServiceBusTrigger(Raft.Message.ServiceBus.Queue.delete, IsSessionsEnabled = true)]string deleteJobMessage, ILogger log)
        {
            await OrchestratorLogic.ContainerInstances.delete(
                log,
                azure,
                agentConfig,
                communicationClients,
                deleteJobMessage);
        }


        [FunctionName("jobstatus-handler")]
        public static async Task Status([ServiceBusTrigger(Raft.Message.ServiceBus.Topic.events, "jobstatus-handler")]string statusMessage, ILogger log)
        {
            await OrchestratorLogic.ContainerInstances.status(
                log,
                azure,
                agentConfig,
                communicationClients,
                statusMessage);
        }

        [FunctionName("webhooks-handler")]
        public static async Task WebhookMessage([ServiceBusTrigger(Raft.Message.ServiceBus.Topic.events, "webhooks-handler")] string webhookMessage, ILogger log)
        {
            await OrchestratorLogic.ContainerInstances.webhookMessage(
                log,
                azure,
                agentConfig,
                communicationClients,
                webhookMessage);
        }

        [FunctionName("raft-timer-garbage-collection")]
        public static async Task TimerGarbageCollection([TimerTrigger("0 */1 * * * *")] TimerInfo t, ILogger log)
        {
            await OrchestratorLogic.ContainerInstances.gc(
                log,
                azure,
                agentConfig,
                communicationClients);
        }
    }
}
