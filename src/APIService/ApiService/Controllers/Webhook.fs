// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Raft.Controllers
//
// https://docs.microsoft.com/en-us/azure/event-grid/post-to-custom-topic
// https://github.com/Azure-Samples/event-grid-dotnet-publish-consume-events/blob/master/EventGridPublisher/TopicPublisher/Program.cs

open System
open Raft.Webhooks
open Raft.Controllers.AppInsights
open Microsoft.Azure.Management.EventGrid
open Microsoft.Azure.EventGrid
open Microsoft.Azure.Management.ResourceManager.Fluent
open Microsoft.Rest.Azure
open Raft.JobEvents
open Raft.Utilities
open Raft
open Microsoft.AspNetCore.Mvc
open Microsoft.Extensions.Configuration
open FSharp.Control.Tasks.V2
open Microsoft.ApplicationInsights
open Raft.Telemetry
open Microsoft.AspNetCore.Authorization
open Raft.Errors
open Microsoft.Extensions.Logging
open Raft.Telemetry.TelemetryApiService
open Microsoft.Azure.Cosmos.Table

[<Authorize>]
[<Route("[controller]")>]
[<Produces("application/json")>]
[<ApiController>]
type webhooksController(configuration : IConfiguration, telemetryClient : TelemetryClient, logger : ILogger<webhooksController>) =
    inherit ControllerBase()
    // Table storage : https://docs.microsoft.com/en-us/dotnet/fsharp/using-fsharp-on-azure/table-storage
    let ModuleName = "Webhook-"
    let tags = ["Service", "WebhookService"]
    let log = Log telemetryClient 

    let webhookEventNames =
        let events = Microsoft.FSharp.Reflection.FSharpType.GetUnionCases(typeof<JobEvents.Events.JobEventTypes>)
        events |> Array.map (fun e -> e.Name)

    let validateEventName (eventName:string) =
        let foundEventName = 
            webhookEventNames
            |> Array.filter(fun name -> name.ToLowerInvariant() = eventName.ToLowerInvariant())
            
        if foundEventName.Length = 0 then
            raiseApiError { Error = { Code = ApiErrorCode.ParseError
                                      Message = sprintf "The event '%s' failed to parse" eventName
                                      Target = "parseEvent"
                                      Details = [||]
                                      InnerError = {Message = "Verify that the webhook event is an allowed event value."}
                                 }}
        else
            foundEventName.[0]

    let getCredentials () =
        let applicationId = getSetting configuration "RAFT_SERVICE_PRINCIPAL_CLIENT_ID"
        let clientSecret = getSetting configuration "RAFT_SERVICE_PRINCIPAL_CLIENT_SECRET"
        let tenantId = getSetting configuration "RAFT_SERVICE_PRINCIPAL_TENANT_ID"

        Authentication.AzureCredentialsFactory()
            .FromServicePrincipal(
                applicationId, 
                clientSecret, 
                tenantId, AzureEnvironment.AzureGlobalCloud)

    let createTopic (gridClient : EventGridManagementClient) (topicName : string) = 
        async {
            //event grid error
            if topicName.Length < 3 || topicName.Length > 128 then
                raiseApiError { Error = { Code = ApiErrorCode.ParseError
                                          Message = sprintf "DomainTopic name must be between 3 and 128 characters in length."
                                          Target = "createTopic"
                                          Details = [||]
                                          InnerError = {Message = ""}
                                     }}

            let resourceGroup = getSetting configuration "RAFT_RESOURCE_GROUP_NAME"
            let eventDomain = getSetting configuration "RAFT_EVENT_DOMAIN"
            try
                return! gridClient.DomainTopics.GetAsync(resourceGroup, eventDomain, topicName) |> Async.AwaitTask
            with ex ->
                return! gridClient.DomainTopics.CreateOrUpdateAsync(resourceGroup, eventDomain, topicName) |> Async.AwaitTask
        }

    let createSubscription (gridClient : EventGridManagementClient) topicId targetUrl (event : string) = 
        async {
            let destination = Models.WebHookEventSubscriptionDestination(targetUrl.ToString())
            let filter = Models.EventSubscriptionFilter(event)
            let subscription = Models.EventSubscription(event, event, "", topicId , "", destination, filter)
            let eventSubscriptionScope = topicId
            return! gridClient.EventSubscriptions.CreateOrUpdateAsync(eventSubscriptionScope, event, subscription) 
                    |> Async.AwaitTask 
        }

    let createGridClient () = 
        let subscriptionId = getSetting configuration "RAFT_SERVICE_PRINCIPAL_SUBSCRIPTION_ID"
        let gridClient = new EventGridManagementClient(getCredentials ())
        gridClient.SubscriptionId <- subscriptionId
        gridClient 

    let tryGetSubscriptions (gridClient : EventGridManagementClient) topic  =
        try
            let resourceGroup = getSetting configuration "RAFT_RESOURCE_GROUP_NAME"
            let eventDomain = getSetting configuration "RAFT_EVENT_DOMAIN"

            let webhooks = gridClient.EventSubscriptions.ListByDomainTopic(resourceGroup, eventDomain, topic)
                           |> Seq.map(fun (sub) -> let wh = sub.Destination :?> Models.WebHookEventSubscriptionDestination
                                                   {
                                                      WebhookName = topic.ToLowerInvariant()
                                                      Event = sub.Filter.SubjectBeginsWith
                                                      TargetUrl = Uri(wh.EndpointBaseUrl)
                                                   })
            Some(webhooks)
        with ex ->
            raiseApiError { Error = { Code = ApiErrorCode.InvalidTag
                                             Message = ex.ToString()
                                             Target = ""
                                             Details = [||]
                                             InnerError = {Message = "The webhook tag must be an existing value."}
                                 }}

    // https://docs.microsoft.com/en-us/rest/api/eventgrid/version2020-04-01-preview/eventsubscriptions/createorupdate
    [<HttpPut>]
    [<HttpPost>]
    /// <summary>
    /// Associates a webhook "tag" with an event and target URL
    /// </summary>
    /// <remarks>
    /// Sample response:
    /// {
    ///    "WebhookName" : "fooTag"
    ///    "Event" : "BugFound"
    ///    "TargetUrl" : "https://mywebhookreceiver"
    /// }
    /// </remarks>
    /// <param name="webhookRequest">A WebHook data type</param>
    /// <response code="200">Returns success.</response>
    /// <response code="400">Returns bad request if an exception occurs. The exception text will give the reason for the failure.</response>
    member this.Put([<FromBody>] webhookRequest:WebHook) =
        task {
            let stopWatch = System.Diagnostics.Stopwatch()
            stopWatch.Start()

            // Externally we use the term SubscriptionName, this equates to a topic name in 
            // the event grid model. Topics have subscriptions. The subscription will be named the name of the event.
            let method = ModuleName + "Put"
            let tags = tags @ ["Method", method]

            try
                // By using <FromBody> we need to:
                // Need to validate that the fields are not null
                if String.IsNullOrWhiteSpace webhookRequest.WebhookName 
                   || String.IsNullOrWhiteSpace webhookRequest.Event
                   || String.IsNullOrWhiteSpace (webhookRequest.TargetUrl.ToString()) then
                    raiseApiError { Error = {   Code = ApiErrorCode.ParseError
                                                Message = "Webhook fields cannot be null"
                                                Target = "PUT"
                                                Details = [||]
                                                InnerError = {Message = "Verify you are using the correct data structure and values."}
                                             }}

                // Need to validate that the URL is a URL. 
                let targetParseResult, url = Uri.TryCreate(webhookRequest.TargetUrl.ToString(), UriKind.Absolute)
                if targetParseResult = false then
                    raiseApiError { Error = {   Code = ApiErrorCode.ParseError
                                                Message = "TargetUrl must be a proper Url."
                                                Target = "PUT"
                                                Details = [||]
                                                InnerError = {Message = "Verify you are using the correct data structure and values."}
                                             }}

                log.Info "Creating webhook" (tags@["name", webhookRequest.WebhookName; "event", webhookRequest.Event])

                // Validates that the event name a legitimate value we support. 
                let requestedEvent = validateEventName webhookRequest.Event
                let webhookName = webhookRequest.WebhookName.ToLowerInvariant()
                let targetUrl = webhookRequest.TargetUrl.ToString()
                
                let gridClient = createGridClient()
                let! topic = createTopic gridClient webhookName
                log.Info "Creating topic" (tags@["name", webhookRequest.WebhookName; "event", webhookRequest.Event])
                let! _ = createSubscription gridClient topic.Id targetUrl (requestedEvent.ToString()) 

                let hook = {
                                WebhookName = topic.Name
                                Event = requestedEvent.ToString()
                                TargetUrl = webhookRequest.TargetUrl
                           }
                stopWatch.Stop()
                Central.Telemetry.TrackMetric (TelemetryValues.ApiRequest(method, float stopWatch.ElapsedMilliseconds), "milliseconds", this :> ControllerBase)
                return JsonResult(hook)

            with
            | Errors.ApiErrorException (apiError) as ex->
                Central.Telemetry.TrackError (TelemetryValues.Exception ex)
                log.Exception ex tags
                return setApiErrorStatusCode apiError Microsoft.AspNetCore.Http.StatusCodes.Status400BadRequest
        }

    [<HttpGet>]
    /// <summary>
    /// List the webhooks associated with the tag. Optionally provide an event in the query string to just show that one event.
    /// </summary>
    /// <remarks>
    /// Sample response:
    /// {
    ///    "WebhookName" : "fooTag"
    ///    "Event" : "BugFound"
    ///    "TargetUrl" : "https://mywebhookreceiver"
    /// }
    /// </remarks>
    /// <param name="name">Name of the webhook "tag"</param>
    /// <param name="event">Optional query string identifying the event</param>
    /// <response code="200">Returns success.</response>
    /// <response code="404">If the webhook tag is not found</response>
    /// <response code="400">If the event name in the query string is not a supported value</response>
    member this.List ([<FromQuery>] name:string, [<FromQuery>]event:string) =
        task {
            let stopWatch = System.Diagnostics.Stopwatch()
            stopWatch.Start()

            let method = ModuleName + "List"
            let tags = tags @ ["Method", method]

            try
                if String.IsNullOrWhiteSpace name then
                    raiseApiError { Error = { Code = ApiErrorCode.QueryStringMissing
                                              Message = "Missing required query string"
                                              Target = method
                                              Details = [||]
                                              InnerError = {Message = "name"}
                                             }}


                // validate the eventName
                let gridClient = createGridClient()
                let subsOption = tryGetSubscriptions gridClient (name.ToLowerInvariant())
                match subsOption with
                | Some subs -> 
                    if isNull event then
                        return JsonResult(subs)
                    else
                        let eventFilter = validateEventName event
                        let sub = subs |> Seq.filter(fun s -> s.Event.ToString().ToLowerInvariant() = eventFilter.ToString().ToLowerInvariant())
                        stopWatch.Stop()
                        Central.Telemetry.TrackMetric (TelemetryValues.ApiRequest(method, float stopWatch.ElapsedMilliseconds), "milliseconds", this :> ControllerBase)
                        return JsonResult(sub)

                | None -> return raiseApiError { Error = 
                                                  { Code = ApiErrorCode.NotFound
                                                    Message = "No subscriptions found"
                                                    Target = method
                                                    Details = [||]
                                                    InnerError = {Message = ""}
                                              }} 

            with
            | Errors.ApiErrorException (apiError) as ex -> 
                Central.Telemetry.TrackError (TelemetryValues.Exception ex)
                log.Exception ex tags
                match apiError.Error.Code with
                | ApiErrorCode.NotFound ->
                    return setApiErrorStatusCode apiError Microsoft.AspNetCore.Http.StatusCodes.Status404NotFound
                | _ ->
                    return setApiErrorStatusCode apiError Microsoft.AspNetCore.Http.StatusCodes.Status400BadRequest
        }

    // Return an array of names.
    [<HttpGet("events")>]
    /// <summary>
    /// List all the supported event names.
    /// </summary>
    member this.ListEvents () =
        task {
            let stopWatch = System.Diagnostics.Stopwatch()
            stopWatch.Start()

            let method = ModuleName + "ListEvents"

            stopWatch.Stop()
            Central.Telemetry.TrackMetric (TelemetryValues.ApiRequest(method, float stopWatch.ElapsedMilliseconds), "milliseconds", this :> ControllerBase)
            return JsonResult(webhookEventNames)
        }

    [<HttpDelete("{webhookName}/{eventName}")>]
    /// <summary>
    /// Delete the webhook for a specific event
    /// </summary>
    /// <remarks>
    /// If the name or event are not found, no error is returned.
    /// </remarks>
    /// <param name="webhookName">Name of the webhook tag</param>
    /// <param name="eventName">Name of the event</param>
    /// <response code="200">Returns success.</response>
    /// <response code="400">If the event name is not a supported value.</response>
    member this.Delete (webhookName : string, eventName : string) =
        task {
            let stopWatch = System.Diagnostics.Stopwatch()
            stopWatch.Start()

            let method = ModuleName + "Delete"
            let tags = tags @ ["Method", method]

            try
                if String.IsNullOrWhiteSpace webhookName then
                    raiseApiError { Error = {   Code = ApiErrorCode.ParseError
                                                Message = "WebhookName cannot be empty"
                                                Target = "DELETE"
                                                Details = [||]
                                                InnerError = {Message = "Verify you are using the correct data structure and values."}
                                             }}
           
                let resourceGroup = getSetting configuration "RAFT_RESOURCE_GROUP_NAME"
                let eventDomain = getSetting configuration "RAFT_EVENT_DOMAIN"
                let gridClient = createGridClient()
                let requestedEvent = validateEventName eventName
                let topicOption =
                    try
                        Some(gridClient.DomainTopics.Get(resourceGroup, eventDomain, webhookName.ToLowerInvariant()))
                    with ex ->
                        None

                match topicOption with
                | Some topic -> gridClient.EventSubscriptions.Delete(topic.Id, requestedEvent.ToString())
                | None -> ()

                stopWatch.Stop()
                Central.Telemetry.TrackMetric (TelemetryValues.ApiRequest(method, float stopWatch.ElapsedMilliseconds), "milliseconds", this :> ControllerBase)
                return JsonResult("")
            with
            | Errors.ApiErrorException (apiError) as ex -> 
                Central.Telemetry.TrackError (TelemetryValues.Exception ex)
                log.Exception ex tags
                return setApiErrorStatusCode apiError Microsoft.AspNetCore.Http.StatusCodes.Status400BadRequest
        }
    [<HttpPut("test/{webhookName}/{eventName}")>]
    member this.TestWebHook (webhookName : string, eventName : string) =
        task {
            let stopWatch = System.Diagnostics.Stopwatch()
            stopWatch.Start()

            let method = ModuleName + "TestWebHook"
            let webhookQueue = Raft.Message.ServiceBus.Topic.events
            try
                if String.IsNullOrWhiteSpace webhookName then
                    raiseApiError { Error = {   Code = ApiErrorCode.ParseError
                                                Message = "WebhookName cannot be empty."
                                                Target = "TestWebHook"
                                                Details = [||]
                                                InnerError = {Message = "Verify you are using the correct values."}
                                             }}

                let _ = validateEventName eventName
                let jobId = sprintf "webhook-test-job-status-%O" (Guid.NewGuid().ToString())
                log.Info "Testing Webhook" ["name", webhookName; "event", eventName; "jobId", sprintf "%A" jobId]
                if eventName.ToLowerInvariant() = Raft.JobEvents.JobStatus.EventType.ToLowerInvariant() then
                    
                    let status : JobStatus = {
                                    AgentName = "1"
                                    Tool = "RESTler"
                                    JobId = jobId
                                    State = JobState.Running
                                    Metrics = None
                                    UtcEventTime = DateTime.UtcNow
                                    Details = Some (Map.empty.Add("Method", method))
                                    Metadata = None
                                    }
                    log.Info "Setting JobStatus webhook in webhooks table" ["name", webhookName; "event", eventName; "jobId", sprintf "%A" jobId]
                    let entity = Raft.StorageEntities.JobWebhookEntity(jobId, webhookName) :> TableEntity
                    match! Utilities.raftStorage.InsertEntity Raft.StorageEntities.JobWebHookTableName entity with
                    | Result.Ok() ->
                        log.Info "Sending JobStatus test webhook" ["name", webhookName; "event", eventName; "jobId", sprintf "%A" jobId]
                        do! sendJobEvent webhookQueue jobId (Message.RaftEvent.createJobEvent status)

                    | Result.Error (statusCode) ->
                        log.Error "Failed to set JobStatus webhook in Webhooks table" ["httpErrorCode", sprintf "%A" statusCode; "webhookName", webhookName; "event", eventName; "jobId", sprintf "%A" jobId]
                        raiseApiError 
                            { 
                                Error = 
                                    { 
                                        Code = ApiErrorCode.InternalError
                                        Message = "Failed to set JobStatus test webhook"
                                        Target = method
                                        Details = [||]
                                        InnerError = {Message = ""}
                                    }
                            }
                
                else if eventName.ToLowerInvariant() = Raft.JobEvents.BugFound.EventType.ToLowerInvariant() then
                    let jobId = sprintf "webhook-test-bug-found-%O" (Guid.NewGuid())
                    let bugFound = 
                        { 
                            Tool = "RESTler"
                            JobId = jobId
                            AgentName = "1"
                            Metadata = None
                            BugDetails = Some(Map.empty.Add("Experiment", "experiment23").Add("BugBucket", "main_driver_500_1.txt"))
                        }
                    log.Info "Setting JobStatus webhook in webhooks table" ["name", webhookName; "event", eventName; "jobId", sprintf "%A" jobId]
                    let entity = Raft.StorageEntities.JobWebhookEntity(jobId, webhookName) :> TableEntity
                    match! Utilities.raftStorage.InsertEntity Raft.StorageEntities.JobWebHookTableName entity with
                    | Result.Ok() ->
                        log.Info "Sending BugFound test webhook" ["webhookName", webhookName; "event", eventName; "jobId", sprintf "%A" jobId]
                        do! sendJobEvent webhookQueue jobId (Message.RaftEvent.createJobEvent bugFound)

                    | Result.Error (statusCode) ->
                        log.Error "Failed to set BugFound webhook in Webhooks table" ["httpErrorCode", sprintf "%A" statusCode; "webhookName", webhookName; "event", eventName; "jobId", sprintf "%A" jobId]
                        raiseApiError 
                            { 
                                Error = 
                                    { 
                                        Code = ApiErrorCode.InternalError
                                        Message = "Failed to set BugFound test webhook"
                                        Target = method
                                        Details = [||]
                                        InnerError = {Message = ""}
                                    }
                            }

                else 
                    log.Error "No matching test webhook found" ["webhookName", webhookName; "event", eventName; "jobId", sprintf "%A" jobId]
                    raiseApiError 
                        { 
                            Error = 
                                { 
                                    Code = ApiErrorCode.NotFound
                                    Message = "The event was not found"
                                    Target = method
                                    Details = [||]
                                    InnerError = {Message = ""}
                                }
                        }
                stopWatch.Stop()
                Central.Telemetry.TrackMetric (TelemetryValues.ApiRequest(method, float stopWatch.ElapsedMilliseconds), "milliseconds", this :> ControllerBase)
                return JsonResult({ Event = eventName
                                    Status = "Sent"})
            with
            | Errors.ApiErrorException (apiError) as ex -> 
                Central.Telemetry.TrackError (TelemetryValues.Exception ex)
                return setApiErrorStatusCode apiError Microsoft.AspNetCore.Http.StatusCodes.Status400BadRequest

        } 
