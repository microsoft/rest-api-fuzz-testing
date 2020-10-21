// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Raft.Controllers
//
// https://docs.microsoft.com/en-us/azure/event-grid/post-to-custom-topic
// https://github.com/Azure-Samples/event-grid-dotnet-publish-consume-events/blob/master/EventGridPublisher/TopicPublisher/Program.cs

open Giraffe
open Microsoft.AspNetCore.Http
open FSharp.Control.Tasks.V2
open System
open Raft.Errors.ErrorHandling
open Raft.Webhooks
open Raft.Controllers.AppInsights
open Microsoft.Azure.Management.EventGrid
open Microsoft.Azure.EventGrid
open Microsoft.Azure.Management.ResourceManager.Fluent
open Microsoft.Rest.Azure
open Raft.JobStatus
open Raft.Telemetry
open Raft.Utilities
open Raft

// Table storage : https://docs.microsoft.com/en-us/dotnet/fsharp/using-fsharp-on-azure/table-storage
module WebhookVerbs = 
    let ModuleName = "WebhookVerbs-"
    let tags = ["Service", "WebhookService"]

    let parseEvent (eventString:string) =
        let parseResult, eventRequested = Enum.TryParse<WebhookEvents> (eventString.ToLower())
        if parseResult = false then
            raiseApiError { Error = { Code = ApiErrorCode.ParseError
                                             Message = sprintf "The event '%s' failed to parse" eventString
                                             Target = "parseEvent"
                                             Details = [||]
                                             InnerError = {Message = "Verify that the webhook event is an allowed event value."}
                                 }}
        eventRequested


    let getCredentials (ctx : HttpContext) =
        let applicationId = getSetting ctx "RAFT_SERVICE_PRINCIPAL_CLIENT_ID"
        let clientSecret = getSetting ctx "RAFT_SERVICE_PRINCIPAL_CLIENT_SECRET"
        let tenantId = getSetting ctx "RAFT_SERVICE_PRINCIPAL_TENANT_ID"

        Authentication.AzureCredentialsFactory()
            .FromServicePrincipal(
                applicationId, 
                clientSecret, 
                tenantId, AzureEnvironment.AzureGlobalCloud)

    let createTopic ctx (gridClient : EventGridManagementClient) (topicName : string) (eventName : WebhookEvents) = 
        async {
            let resourceGroup = getSetting ctx "RAFT_RESOURCE_GROUP_NAME"
            let eventDomain = getSetting ctx "RAFT_EVENT_DOMAIN"
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

    let createGridClient ctx = 
        let subscriptionId = getSetting ctx "RAFT_SERVICE_PRINCIPAL_SUBSCRIPTION_ID"
        let gridClient = new EventGridManagementClient(getCredentials ctx)
        gridClient.SubscriptionId <- subscriptionId
        gridClient 

    let tryGetSubscription ctx (gridClient : EventGridManagementClient) subscriptionName (event:WebhookEvents) =
        let resourceGroup = getSetting ctx "RAFT_RESOURCE_GROUP_NAME"
        let eventDomain = getSetting ctx "RAFT_EVENT_DOMAIN"
        try
            let subscriptions = gridClient.EventSubscriptions.ListByDomainTopic(resourceGroup, eventDomain,subscriptionName)
            let sub = subscriptions |> Seq.filter(fun s -> s.Name = event.ToString())
            match Seq.length sub with
            | 1 -> Some (sub |> Seq.head)
            | _ -> None
        with exn -> 
            None

    let tryGetSubscriptions ctx (gridClient : EventGridManagementClient) topic  =
        try
            let resourceGroup = getSetting ctx "RAFT_RESOURCE_GROUP_NAME"
            let eventDomain = getSetting ctx "RAFT_EVENT_DOMAIN"

            let webhooks = gridClient.EventSubscriptions.ListByDomainTopic(resourceGroup, eventDomain, topic)
                           |> Seq.map(fun (sub) -> let wh = sub.Destination :?> Models.WebHookEventSubscriptionDestination
                                                   {
                                                      WebhookName = topic.ToLower()
                                                      Event = sub.Filter.SubjectBeginsWith
                                                      TargetUrl = Uri(wh.EndpointBaseUrl)
                                                   })
            Some(webhooks)
        with ex ->
            raiseApiError { Error = { Code = ApiErrorCode.InvalidCustomer
                                             Message = ex.ToString()
                                             Target = ""
                                             Details = [||]
                                             InnerError = {Message = "The webhook tag must be an existing value."}
                                 }}

    // https://docs.microsoft.com/en-us/rest/api/eventgrid/version2020-04-01-preview/eventsubscriptions/createorupdate
    let CreateOrUpdateWebHookSubscription : HttpHandler =
        fun (next : HttpFunc) (ctx : HttpContext) -> task {
            // Externally we use the term SubscriptionName, this equates to a topic name in 
            // the event grid model. Topics have subscriptions. The subscription will be named the name of the event.
            let method = ModuleName + "CreateOrUpdateWebHookSubscription"
            let log = Log ctx
            let tags = tags @ ["Method", method]

            try
                use reader = new System.IO.StreamReader(ctx.Request.Body)
                let body = reader.ReadToEnd()
                let webhookRequest = 
                    try
                        Microsoft.FSharpLu.Json.Compact.deserialize<Raft.Webhooks.WebHookDto>(body)
                    with ex ->
                        raiseApiError { Error = { Code = ApiErrorCode.ParseError
                                                  Message = sprintf "Unable to parse %s body" ctx.Request.Method
                                                  Target = method
                                                  Details = [||]
                                                  InnerError = {Message = ex.Message}
                                                  }} 

                let requestedEvent = parseEvent (webhookRequest.Event.ToLower())
                let webhookName = webhookRequest.WebhookName.ToLower()
                let targetUrl = webhookRequest.TargetUrl.ToString()

                let gridClient = createGridClient ctx
                let! topic = createTopic ctx gridClient webhookName requestedEvent
                let! _ = createSubscription gridClient topic.Id targetUrl (requestedEvent.ToString()) 

                let hook = {
                                WebhookName = topic.Name
                                Event = requestedEvent.ToString()
                                TargetUrl = webhookRequest.TargetUrl
                           }

                Central.Telemetry.TrackMetric (TelemetryValues.ApiRequest  method) (Some ctx) None
                return! Successful.OK hook next ctx

            with
            | ApiErrorException (apiError) as ex->
                Central.Telemetry.TrackError (TelemetryValues.Exception ex)
                log.Exception ex tags
                return! RequestErrors.BAD_REQUEST apiError next ctx
            | ex ->
                Central.Telemetry.TrackError (TelemetryValues.Exception ex)
                log.Exception ex tags
                return! RequestErrors.BAD_REQUEST { Error = { Code = ApiErrorCode.InternalError
                                                              Message = ex.Message
                                                              Target = method
                                                              Details = [||]
                                                              InnerError = {Message = "Unexpected error."}
                                                      }} next ctx
        }

    let GetWebHooks  : HttpHandler =
        fun (next : HttpFunc) (ctx : HttpContext) -> task {
            let method = "GetWebHooks"
            let log = Log ctx
            let tags = tags @ ["Method", method]

            // Query strings
            // name=<webhookName>&event=<eventName>

            try
                let webhookNameOption = ctx.TryGetQueryStringValue "name"
                let webhookName = 
                    match webhookNameOption with
                    | Some sn -> sn
                    | None -> raiseApiError { Error = { Code = ApiErrorCode.QueryStringMissing
                                                        Message = "Missing required query string"
                                                        Target = method
                                                        Details = [||]
                                                        InnerError = {Message = "name"}
                                                       }}

                let eventNameOption = ctx.TryGetQueryStringValue "event"

                // validate the eventName
                let gridClient = createGridClient ctx
                let subsOption = tryGetSubscriptions ctx gridClient webhookName
                match subsOption with
                | Some subs -> 
                    match eventNameOption with
                    | None -> return! Successful.OK subs next ctx
                    | Some name -> 
                        let event = parseEvent name
                        let sub = subs |> Seq.filter(fun s -> s.Event.ToLower() = event.ToString().ToLower())
                        Central.Telemetry.TrackMetric (TelemetryValues.ApiRequest  method) (Some ctx) None
                        return! Successful.OK sub next ctx

                | None -> return raiseApiError { Error = 
                                                  { Code = ApiErrorCode.NotFound
                                                    Message = "No subscriptions found"
                                                    Target = method
                                                    Details = [||]
                                                    InnerError = {Message = ""}
                                              }} next ctx

            with
            | ApiErrorException (apiError) as ex -> 
                Central.Telemetry.TrackError (TelemetryValues.Exception ex)
                log.Exception ex tags
                return! RequestErrors.BAD_REQUEST apiError next ctx

            | ex -> 
                Central.Telemetry.TrackError (TelemetryValues.Exception ex)
                log.Exception ex tags
                return! RequestErrors.BAD_REQUEST { Error = 
                                                    { Code = ApiErrorCode.InternalError
                                                      Message = ex.Message
                                                      Target = method
                                                      Details = [||]
                                                      InnerError = {Message = "Unexpected error."}
                                                    }} next ctx
        }

    // Return an array of names.
    let GetWebHookEvents  : HttpHandler =
        fun (next : HttpFunc) (ctx : HttpContext) -> task {
            let method = "GetWebHookEvents"
            let allEvents = Enum.GetNames(typeof<WebhookEvents>)
            Central.Telemetry.TrackMetric (TelemetryValues.ApiRequest  method) (Some ctx) None
            return! Successful.OK allEvents next ctx
        }

    let DeleteWebHook (webhookName : string) (eventName : string) : HttpHandler =
        fun (next : HttpFunc) (ctx : HttpContext) -> task {
            let method = "DeleteWebHook"
            let log = Log ctx
            let tags = tags @ ["Method", method]

            try
                let event = parseEvent eventName
                let resourceGroup = getSetting ctx "RAFT_RESOURCE_GROUP_NAME"
                let eventDomain = getSetting ctx "RAFT_EVENT_DOMAIN"
                let gridClient = createGridClient ctx
                let topicOption =
                    try
                        Some(gridClient.DomainTopics.Get(resourceGroup, eventDomain, webhookName.ToLower()))
                    with ex ->
                        None

                match topicOption with
                | Some topic -> gridClient.EventSubscriptions.Delete(topic.Id, event.ToString())
                | None -> ()

                Central.Telemetry.TrackMetric (TelemetryValues.ApiRequest  method) (Some ctx) None
                return! Successful.OK "" next ctx
            with
            | ApiErrorException (apiError) as ex -> 
                Central.Telemetry.TrackError (TelemetryValues.Exception ex)
                log.Exception ex tags
                return! RequestErrors.BAD_REQUEST apiError next ctx

            | ex -> 
                Central.Telemetry.TrackError (TelemetryValues.Exception ex)
                log.Exception ex tags
                return! RequestErrors.BAD_REQUEST { Error = 
                                                        { Code = ApiErrorCode.InternalError
                                                          Message = ex.Message
                                                          Target = method
                                                          Details = [||]
                                                          InnerError = {Message = "Unexpected error."}
                                                        }
                                                        } next ctx
        }

    let createWebHook<'T> customerId event (date:DateTime) (data:'T) =
        {
            topic = customerId.ToString()
            id = Guid.NewGuid().ToString()
            eventType = event.ToString()
            subject = event.ToString()
            eventTime = date.ToString("O")
            data = data
            dataVersion = "1.0"
        }

    let TestWebHook (webhookName : string) (eventName : string) : HttpHandler =
            fun (next : HttpFunc) (ctx : HttpContext) -> task {
                let method = "TestWebHook"
                let webhookQueue = Raft.Message.ServiceBus.Queue.webhook
                try
                    let event = parseEvent eventName
                    do! task {
                        match event with
                        | WebhookEvents.jobstatus -> 
                            let status : JobStatus = {
                                            agentName = "1"
                                            tool = Some Tool.RESTler
                                            jobId = Guid.NewGuid()
                                            state = JobState.Running
                                            metrics = None
                                            utcEventTime = DateTime.UtcNow
                                            details = [""]
                                            webhookName = Some(webhookName)
                                            }
                            do! sendMessage ctx webhookQueue { message = {
                                                               webhookName = webhookName
                                                               event = WebhookEvents.jobstatus
                                                               data = status
                                                              }} 
                        | WebhookEvents.bugfound -> 
                            let bugfound = { JobId = (Guid.NewGuid().ToString()) }
                            do! sendMessage ctx webhookQueue { message = {
                                                               webhookName = webhookName
                                                               event = WebhookEvents.bugfound
                                                               data = bugfound
                                                              }} 
                        | _ -> raiseApiError { Error = { Code = ApiErrorCode.NotFound
                                                         Message = "The event was not found"
                                                         Target = method
                                                         Details = [||]
                                                         InnerError = {Message = ""}
                                                        }}
                    }
                    Central.Telemetry.TrackMetric (TelemetryValues.ApiRequest  method) (Some ctx) None
                    return! Successful.OK (sprintf "Event '%O' Sent" event) next ctx
                with
                | ApiErrorException (apiError) as ex -> 
                    Central.Telemetry.TrackError (TelemetryValues.Exception ex)
                    return! RequestErrors.BAD_REQUEST apiError next ctx

                | ex -> 
                    Central.Telemetry.TrackError (TelemetryValues.Exception ex)
                    return! RequestErrors.BAD_REQUEST { Error = 
                                                            { Code = ApiErrorCode.InternalError
                                                              Message = ex.Message
                                                              Target = method
                                                              Details = [||]
                                                              InnerError = {Message = "Unexpected error."}
                                                            }
                                                            } next ctx
            } 
