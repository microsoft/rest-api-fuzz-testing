// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace AgentUtilities.Controllers

open System.Collections.Generic
open Microsoft.AspNetCore.Mvc
open Microsoft.Azure
open Raft.Message
open Microsoft.ApplicationInsights

[<AutoOpen>]
module AgentUtilities =
    type System.Threading.Tasks.Task with
        member x.ToAsync = Async.AwaitTask(x)

    type ServiceBus.Core.MessageSender with
        member inline x.SendRaftJobEvent (jobId: string) (message : ^T when ^T : (static member EventType : string)) =
            async {
                let raftJobEvent = Raft.Message.RaftEvent.createJobEvent message
                let message = ServiceBus.Message(RaftEvent.serializeToBytes raftJobEvent, SessionId = jobId.ToString())
                do! x.SendAsync(message).ToAsync
            }

    type RaftMessageSender(sas : string option, appInsightsKey: string option ) =
        let sender =
            match sas with
            | Some sas ->
                Some (ServiceBus.Core.MessageSender(ServiceBus.ServiceBusConnectionStringBuilder(sas), ServiceBus.RetryPolicy.Default))
            | None -> None

        let appInsights =
            match appInsightsKey with
            | None -> None
            | Some instrumentationKey ->
                Some(Microsoft.ApplicationInsights.TelemetryClient(new Extensibility.TelemetryConfiguration(instrumentationKey), InstrumentationKey = instrumentationKey))

        member __.AppInsights = appInsights

        member __.Sender = sender

        member __.CloseAsync() =
            async {
                match sender with
                | Some s ->
                    do! s.CloseAsync().ToAsync
                | None -> ()
            }

        member __.Flush()=
            match appInsights with
            | Some ai -> ai.Flush()
            | None -> ()

        member inline __.SendRaftJobEvent (jobId: string) (message : ^T when ^T : (static member EventType : string)) =
            async {
                let raftJobEvent = Raft.Message.RaftEvent.createJobEvent message
                match __.Sender with
                | Some s ->
                    let message = ServiceBus.Message(RaftEvent.serializeToBytes raftJobEvent, SessionId = jobId.ToString())
                    do! s.SendAsync(message).ToAsync
                    return ()
                | None ->
                    let json = RaftEvent.serializeToJson raftJobEvent
                    System.IO.File.WriteAllText(sprintf "/raft-events-sink/%A.json" (System.Guid.NewGuid()), json)
                    return ()
            }


[<ApiController>]
[<Route("[controller]")>]
[<Produces("application/json")>]
type MessagingController () =
    inherit ControllerBase()

    let messageSender = 
        let sbSas = System.Environment.GetEnvironmentVariable("RAFT_SB_OUT_SAS") |> Option.ofObj
        let appInsightsKey = System.Environment.GetEnvironmentVariable("RAFT_APP_INSIGHTS_KEY") |> Option.ofObj
        RaftMessageSender(sbSas, appInsightsKey)

    [<HttpPost("event/bugFound")>]
    member this.BugFound() =
        async {
            let! status = 
                use reader = new System.IO.StreamReader(this.Request.Body)
                reader.ReadToEndAsync() |> Async.AwaitTask

            let bug : Raft.JobEvents.BugFound = Microsoft.FSharpLu.Json.Compact.Strict.deserialize status
            do! messageSender.SendRaftJobEvent bug.JobId bug
            return this.Ok()
        } |> Async.StartAsTask

    [<HttpPost("event/jobStatus")>]
    member this.JobStatus() =
        async {
            let! status = 
                use reader = new System.IO.StreamReader(this.Request.Body)
                reader.ReadToEndAsync() |> Async.AwaitTask

            let json : Raft.JobEvents.JobStatus = Microsoft.FSharpLu.Json.Compact.Strict.deserialize status
            do! messageSender.SendRaftJobEvent json.JobId json
            return this.Ok()
        } |> Async.StartAsTask

    [<HttpPost("trace")>]
    member this.Trace([<FromBody>]traceData: {| Message: string; Severity: DataContracts.SeverityLevel; Tags: IDictionary<string, string> |}) =
        async {
            match messageSender.AppInsights with
            | Some ai -> ai.TrackTrace(traceData.Message, traceData.Severity, traceData.Tags)
            | None -> ()
            return this.Ok()
        } |> Async.StartAsTask

    [<HttpPost("flush")>]
    member this.Flush() =
        async {
            messageSender.Flush()
            return this.Ok()
        } |> Async.StartAsTask


