// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

module Raft.Message

open Newtonsoft
open Newtonsoft.Json
open Microsoft.FSharpLu

module ServiceBus =
    module Queue =
        let [<Literal>] create = "raft-jobcreate"
        let [<Literal>] delete = "raft-jobdelete"
     
    module Topic =
        let [<Literal>] events = "raft-jobevents"


module InternalHelpers =
    type TupleAsArraySettings =
        static member formatting = Newtonsoft.Json.Formatting.Indented
        static member settings =
            JsonSerializerSettings(
                NullValueHandling = NullValueHandling.Ignore,
                MissingMemberHandling = MissingMemberHandling.Ignore,
                Converters = [| Microsoft.FSharpLu.Json.CompactUnionJsonConverter(true, true) |]
            )
    type private J = Microsoft.FSharpLu.Json.With<TupleAsArraySettings>

    type EventType =
        {
            EventType : string
        }

    let getEventType (msg: string) : string =
        (J.deserialize<EventType> msg).EventType


module RaftEvent =

    type RaftJobEvent< ^T when ^T : (static member EventType : string) > =
        {
            EventType : string
            Message: ^T
        }

    let inline createJobEvent (message : ^T when ^T : (static member EventType : string)) =
        {
            EventType = ( ^T  : (static member EventType : string)()) // call static EventType getter
            Message = message
        }

    let inline serializeToJson (msg : RaftJobEvent< ^T >) =
        Json.Compact.serialize msg

    let inline serializeToBytes (msg: RaftJobEvent< ^T >) =
        msg |> serializeToJson |> System.Text.Encoding.UTF8.GetBytes

    let inline deserializeJson (json: string) =
        Json.Compact.deserialize< RaftJobEvent< ^T > > json
    
    let inline deserializeBytes (bytes: byte array) =
        bytes |> System.Text.Encoding.UTF8.GetString |> deserializeJson
    
    let inline tryDeserializeJson (json: string) =
        Json.Compact.tryDeserialize< RaftJobEvent< ^T > > json

    let getEventType (msg: string) : string = InternalHelpers.getEventType msg

    let inline deserializeEvent (message: string)  =
        deserializeJson message

    let inline tryDeserializeEvent (message: string) =
        tryDeserializeJson message

module RaftCommand =

    type RaftCommand< 'T > =
        {
            Message: 'T
            MessagePostCount : int
        }

    let createCommand (message : 'T) =
        {
            MessagePostCount = 0
            Message = message
        }

    let inline serializeToJson (msg : RaftCommand<'T>) =
        Json.Compact.serialize msg

    let inline serializeToBytes (msg: RaftCommand<'T>) =
        msg |> serializeToJson |> System.Text.Encoding.UTF8.GetBytes

    let inline deserializeJson (json: string) =
        Json.Compact.deserialize< RaftCommand<_> > json

    let inline deserializeBytes (bytes: byte array) =
        bytes |> System.Text.Encoding.UTF8.GetString |> deserializeJson

    let inline deserializeCommand (message: string)  =
        deserializeJson message



type MetricType =
    | ContainerConsumedResources
    | JobStatus
    | CreateJob
    | JobService
    | CustomerService
    | OperatorService

type RaftMetricMessage< 'T > =
    {
        metricType: MetricType
        message: 'T
    }

    static member SerializeToJson (msg : RaftMetricMessage<'T>) =
        Json.Compact.serialize msg

    static member SerializeToBytes (msg: RaftMetricMessage<'T>) =
        msg |> RaftMetricMessage.SerializeToJson |> System.Text.Encoding.UTF8.GetBytes

    member this.SerializeToBytes () = RaftMetricMessage<_>.SerializeToBytes this