// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Raft.Telemetry

open System.Diagnostics
open Microsoft.AspNetCore.Mvc
open System.Reflection
open Microsoft.ApplicationInsights.Metrics

module TelemetryApiService = 
    type Raft.Telemetry.Central.TelemetryImpl with
        member this.TrackMetric (telemetryValue:TelemetryValues, units, controller : ControllerBase) = 
            // The telemetry client value can be null during unit testing. We don't want to pollute the 
            // our metrics with unit tests.
            let version = Assembly.GetEntryAssembly().GetName().Version
            match this.TelemetryConfig with
            | Some (telemetry, site) ->
                match telemetryValue with
                | ApiRequest (methodName, duration) ->
                    let m = telemetry.GetMetric(MetricIdentifier("ApiRequest", methodName, "Units", "SiteHash", "ResponseCode", "Version"))

                    m.TrackValue(duration, units, site, controller.Response.StatusCode.ToString(), version.ToString()) |> ignore
                | v -> this.TrackMetric(v, units)
            | None -> ()