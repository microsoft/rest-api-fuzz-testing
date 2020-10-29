// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

//Copied from RESTler repo (need to keep in sync)
//https://github.com/microsoft/restler-fuzzer/blob/main/src/compiler/Restler.Compiler/Telemetry.fs

module Restler.Telemetry

//Instrumentation key is from app insights resource in Azure Portal
let [<Literal>] InstrumentationKey = "6a4d265f-98cd-432f-bfb9-18ced4cd43a9"

type TelemetryClient(machineId: string, instrumentationKey: string) =
    let client =
        let c = Microsoft.ApplicationInsights.TelemetryClient(
                    new Microsoft.ApplicationInsights.Extensibility.TelemetryConfiguration(instrumentationKey))
        // Make sure to not send any instance names or IP addresses
        // These must be non-empty strings to override sending the values obtained by the framework
        c.Context.Cloud.RoleName <- "none"
        c.Context.Cloud.RoleInstance <- "RAFT"
        c.Context.Location.Ip <- "0.0.0.0"
        c

    member __.RestlerStarted(version, task, executionId, featureList) =
        client.TrackEvent("restler started",
            dict ([
                "machineId", sprintf "%s" machineId
                "version", version
                "task", task
                "executionId", sprintf "%A" executionId
            ]@featureList))

    member __.RestlerFinished(version, task, executionId, status,
                              specCoverageCounts,
                              bugBucketCounts) =
        client.TrackEvent("restler finished",
            dict ([
                "machineId", sprintf "%A" machineId
                "version", version
                "task", task
                "executionId", sprintf "%A" executionId
                "status", sprintf "%s" status
            ]@bugBucketCounts@specCoverageCounts))

    member __.ResultsAnalyzerFinished(version, task, executionId, status) =
        client.TrackEvent("results analyzer finished",
            dict ([
                "machineId", sprintf "%A" machineId
                "version", version
                "task", task
                "executionId", sprintf "%A" executionId
                "status", sprintf "%s" status
            ]))

    interface System.IDisposable with
        member __.Dispose() =
            client.Flush()


let getDataFromTestingSummary (testingSummary: Raft.RESTlerTypes.Logs.TestingSummary option) =
    let bugBucketCounts, specCoverageCounts =
        // Read the bug buckets file
        match testingSummary with
        | None ->
            [], []
        | Some testingSummary ->
            let bugBuckets = testingSummary.bug_buckets
                                |> Seq.map (fun kvp -> kvp.Key, sprintf "%A" kvp.Value)
                                |> Seq.toList

            let coveredRequests, totalRequests =
                let finalCoverageValues =
                    testingSummary.final_spec_coverage.Split("/")
                                        |> Array.map (fun x -> x.Trim())
                System.Int32.Parse(finalCoverageValues.[0]),
                System.Int32.Parse(finalCoverageValues.[1])
            let totalMainDriverRequestsSent =
                match testingSummary.total_requests_sent.TryGetValue("main_driver") with
                | (true, v ) -> v
                | (false, _ ) -> 0

            let requestStats =
                [
                    "total_executed_requests_main_driver", totalMainDriverRequestsSent
                    "covered_spec_requests", coveredRequests
                    "total_spec_requests", totalRequests
                ]
                |> List.map (fun (x,y) -> x, sprintf "%A" y)
            (requestStats, bugBuckets)
    {|
        bugBucketCounts = bugBucketCounts
        specCoverageCounts = specCoverageCounts
    |}
