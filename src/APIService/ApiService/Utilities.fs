// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Raft

open Microsoft.AspNetCore.Http
open Message
open Microsoft.Extensions.Configuration
open Raft.Errors
open Microsoft.Azure.ServiceBus.Core
open Microsoft.Azure
open Interfaces
open System
open Storage
open Microsoft.AspNetCore.Mvc

module Utilities =
    let mutable serviceStartTime = System.DateTimeOffset.MinValue
    let mutable serviceBusSenders : Map<string, IMessageSender> = Map.empty
    let mutable raftStorage : IRaftStorage = RaftStorage("") :> IRaftStorage

    let tryGetQueryStringValue (request:HttpRequest) (key:string) =
        request.Query
        |> Seq.tryFind (fun (keyValue) -> System.String.Compare(keyValue.Key, key, StringComparison.InvariantCultureIgnoreCase) = 0)
        |> Option.map (fun pair -> pair.Value.[0])

    let getSetting (settings : IConfiguration) (name:string) = 
        let value = settings.GetValue(name, System.String.Empty).ToString()
        if value = System.String.Empty then
            raiseApiError 
                { Error = { Code = ApiErrorCode.QueryStringMissing
                            Message = "Setting not found."
                            Target = "getSetting"
                            Details = [||]
                            InnerError = {Message = name }
                }}
        else
            value

    let deserializeCommand (message: string)  =
        let decodedMessage: 'a =
            let m = RaftCommand.deserializeJson message
            m.Message
        decodedMessage

    let sendCommand queue (sessionId: string) data =
        async { 
            let sender =
                if obj.ReferenceEquals(serviceBusSenders, null) then 
                    // This should never happen.
                    failwith "ServiceBus sender object not set"
                else
                    serviceBusSenders.[queue]

            let message = ServiceBus.Message(Raft.Message.RaftCommand.serializeToBytes data, SessionId = sessionId.ToString())
            do! sender.SendAsync(message) |> Async.AwaitTask
        }

    let inline sendJobEvent queue (sessionId: string) data =
        async { 
            let sender =
                if obj.ReferenceEquals(serviceBusSenders, null) then 
                    // This should never happen.
                    failwith "ServiceBus sender object not set"
                else
                    serviceBusSenders.[queue]

            let message = ServiceBus.Message(Raft.Message.RaftEvent.serializeToBytes data, SessionId = sessionId.ToString())
            do! sender.SendAsync(message) |> Async.AwaitTask
        }

    let setApiErrorStatusCode apiError code =
        let result =  JsonResult(apiError)
        result.StatusCode <- Nullable(code)
        result


