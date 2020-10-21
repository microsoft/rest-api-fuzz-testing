// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Raft.Controllers.AppInsights


open Microsoft.ApplicationInsights
open Microsoft.AspNetCore.Http

type Log (client: TelemetryClient) =
    let convertTagsToProperties tags =
        tags |> Map.ofList|> Map.toSeq |> dict
    member this.Info message tags = client.TrackTrace(message, DataContracts.SeverityLevel.Information, convertTagsToProperties tags)
    member this.Warning message tags = client.TrackTrace(message, DataContracts.SeverityLevel.Warning, convertTagsToProperties tags)
    member this.Error message tags = client.TrackTrace(message, DataContracts.SeverityLevel.Error, convertTagsToProperties tags)
    member this.Event name tags = client.TrackEvent(name, convertTagsToProperties tags)
    member this.Metric (name:string) value = 
        let metric = client.GetMetric(name)
        metric.TrackValue(value)
    member this.Exception ex tags = client.TrackException(ex, convertTagsToProperties tags)

