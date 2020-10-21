// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Raft.Controllers

open Microsoft.AspNetCore.Mvc
open Raft.Telemetry
open System.Reflection
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Configuration
open Raft.Telemetry.TelemetryApiService

/// <summary>
/// Information about the system.
/// </summary>
type infoType =
    {
        /// <summary>
        /// Version of the binary running on the server
        /// </summary>
        Version : System.Version

        /// <summary>
        /// When the server started running. DateTimeOffset in UTC .
        /// </summary>
        ServiceStartTime : System.DateTimeOffset
    }

[<Route("/")>]
[<ApiController>]
[<Produces("application/json")>]
type infoController (configuration : IConfiguration) =
    inherit ControllerBase()
    let ModuleName = "Info-"

    [<HttpGet>]
    /// <summary>
    /// Test to see if service is up
    /// </summary>
    /// <remarks>
    /// This is an unauthenticated method which returns no data.
    /// </remarks>
    /// <response code="200">Returns success if the service is running.</response>
    [<ProducesResponseType(StatusCodes.Status200OK)>]
    member this.Get() =
        let stopWatch = System.Diagnostics.Stopwatch()
        stopWatch.Start()

        let method = ModuleName + "Get"
        stopWatch.Stop()
        Central.Telemetry.TrackMetric (TelemetryValues.ApiRequest (method, float stopWatch.ElapsedMilliseconds), "milliseconds", this :> ControllerBase)
        EmptyResult()

    [<HttpGet("info")>]
    /// <summary>
    /// Returns information about the system.
    /// </summary>
    /// <remarks>
    /// This is an unauthenticated method.
    /// Sample response:
    /// {
    ///   "version": "1.0.0.0",
    ///   "serviceStartTime": "2020-07-02T15:28:57.0093727+00:00"
    /// }
    /// </remarks>
    /// <response code="200">Returns success.</response>
    [<ProducesResponseType(StatusCodes.Status200OK)>]
    member this.GetInfo() =
        let stopWatch = System.Diagnostics.Stopwatch()
        stopWatch.Start()

        let method = ModuleName + "GetInfo"
        Central.Telemetry.TrackMetric (TelemetryValues.ApiRequest(method, float stopWatch.ElapsedMilliseconds), "milliseconds", this :> ControllerBase)

        let info = { 
                        Version = Assembly.GetEntryAssembly().GetName().Version
                        ServiceStartTime = System.DateTimeOffset.Parse(configuration.GetValue("RestartTime"))
                   }
        JsonResult(info)
