// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Raft

open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Diagnostics
open Raft.Errors
open System.Net
open Raft.Telemetry

module ErrorHandling = 
    let inline someExceptionOfType< ^t when ^t :> System.Exception> (e:System.Exception) =
        match e with
        | :? 't as ex -> Some ex
        | _ -> None

    let handleExceptions (context:HttpContext) =
        let settings = Newtonsoft.Json.JsonSerializerSettings()
        settings.ContractResolver <- new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver()
        settings.Converters.Add(Newtonsoft.Json.Converters.StringEnumConverter())
        settings.Converters.Add(Microsoft.FSharpLu.Json.CompactUnionJsonConverter())
        settings.Formatting <- Newtonsoft.Json.Formatting.Indented
        settings.NullValueHandling <- Newtonsoft.Json.NullValueHandling.Ignore
        settings.DateTimeZoneHandling <- Newtonsoft.Json.DateTimeZoneHandling.Utc
        let serialize data = Newtonsoft.Json.JsonConvert.SerializeObject(data, settings)
                                                      
        let exceptionHandlerFeature = context.Features.Get<IExceptionHandlerFeature>()
        if not <| isNull exceptionHandlerFeature then
            Central.Telemetry.TrackError (TelemetryValues.Exception exceptionHandlerFeature.Error)
            match exceptionHandlerFeature.Error with
            | ApiErrorException apiError ->
                context.Response.ContentType <- "application/json"
                context.Response.StatusCode <- int HttpStatusCode.InternalServerError
                context.Response.WriteAsync(serialize apiError)

            | ex ->
                let apiError =
                    {
                        ApiError.Error =
                            {
                                Code = ApiErrorCode.InternalError
                                Message = sprintf "Internal Server Error."
                                Target = ""
                                Details = [||]
                                InnerError = {Message = sprintf "%s" ex.Message}
                            }
                    }
                context.Response.ContentType <- "application/json"
                context.Response.StatusCode <- int HttpStatusCode.InternalServerError
                context.Response.WriteAsync(serialize apiError)
        else
            System.Threading.Tasks.Task.FromResult(0) :> System.Threading.Tasks.Task