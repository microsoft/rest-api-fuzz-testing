// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Raft.Telemetry

open System
open Microsoft.ApplicationInsights
open System.Reflection
open Microsoft.ApplicationInsights.Metrics

// The installation is a hash of the subscription Id and the deployment name. 
type InstallationHash = string

// The version here is the version of the api
type Version = string

// the name that implements a route api
type RoutePath = string

// Type of REST method used
type HTTPMethod = string

// in the event of an exception, capture the stack trace.
type FailureStackTrace = string
type MethodName = string
type HttpStatus = int

// Telemetry collected about the RESTler tool
// This data is used to improve the service. The information is anonymous.

type TelemetryValues =
    | ContainerGroupAverageCPUUsage of float * DateTime
    | ContainerGroupAverageRAMUsageMB of float * DateTime
    | JobFuzzingDurationRequest of float                        // Requested fuzzing duration 
    | AverageNetworkBytesTransmittedPerSecond of float * DateTime          // per job - I think these per/job metrics are only interesting on a job basis.
    | AverageNetworkBytesReceivedPerSecond of float * DateTime             // per job
    | ApiRequest of MethodName * float
    | Exception of exn
    | GCRun of float                                           // time took to run GC in seconds
    
    | Created of string * DateTime //tool name *  eventTimeStamp
    | Deleted of string * DateTime //tool name * eventTimeStamp
    | Completed of string *  DateTime //tool name * bugs found * eventTimeStamp
    | Error of string * DateTime // tool name * eventTimeStamp
    | TimedOut of string * DateTime //tool name * eventTimeStamp
    | BugsFound of int * string * DateTime //number of bugs * tool name
    | StatusCount of HttpStatus * int * string * DateTime // http status code * count * tool name * eventTimeStamp

    | Tasks of (string * int) array * int // (payload name * how many payloads with the name) array * total payloads overall

module Central =
    module private Namespaces =
        let [<Literal>] Jobs = "Jobs"
        let [<Literal>] Containers = "Containers"
        let [<Literal>] GarbageCollection = "GarbageCollection"
    module private Labels =
        let [<Literal>] SiteHash = "SiteHash"
        let [<Literal>] Version = "Version"
        let [<Literal>] TimeStamp = "TimeStamp"
        let [<Literal>] Units = "Units"
        let [<Literal>] Name = "Name"
        let [<Literal>] Total = "Total"
        let [<Literal>] ToolName = "ToolName"

    type TelemetryImpl(telemetryConfig: (TelemetryClient * string) option) =
        let convertTagsToProperties tags = tags |> dict
        let version = Assembly.GetCallingAssembly().GetName().Version

        member _.TelemetryConfig = telemetryConfig

        member _.ConvertTagsToProperties tags = convertTagsToProperties tags

        member _.TrackError (telemetryValue:TelemetryValues) = 
            // The telemetry client value can be null during unit testing. We don't want to pollute the 
            // our metrics with unit tests.
            match telemetryConfig with
            | Some (telemetry, siteHash) ->
                let properties = convertTagsToProperties [Labels.SiteHash, siteHash; Labels.Version, version.ToString()]
                match telemetryValue with
                | Exception ex -> 
                    telemetry.TrackException(ex, properties) 
                | _ -> () // Only exceptions are allowed to be tracked with this telemetry type
            | None -> ()

        member __.TrackMetric (telemetryValue:TelemetryValues, units: string) = 
            // The telemetry client value can be null during unit testing. We don't want to pollute the 
            // our metrics with unit tests.
            match telemetryConfig with
            | Some (telemetry, site) ->
                match telemetryValue with
                | ContainerGroupAverageCPUUsage (cpu, timeStamp) ->
                    let m = telemetry.GetMetric(MetricIdentifier(Namespaces.Containers, "CPUUsage", Labels.Units, Labels.SiteHash, Labels.TimeStamp, Labels.Version))
                    m.TrackValue(float cpu, units, site, timeStamp.ToString(), version.ToString()) |> ignore

                | ContainerGroupAverageRAMUsageMB (ram, timeStamp) ->
                    let m = telemetry.GetMetric(MetricIdentifier(Namespaces.Containers, "RamUsage", Labels.Units, Labels.SiteHash, Labels.TimeStamp, Labels.Version))
                    m.TrackValue(ram, units, site, timeStamp.ToString(), version.ToString()) |> ignore

                | JobFuzzingDurationRequest time -> 
                    let m = telemetry.GetMetric(MetricIdentifier(Namespaces.Jobs, "Duration", Labels.Units, Labels.SiteHash, Labels.Version))
                    m.TrackValue(time, units, site, version.ToString()) |> ignore

                | AverageNetworkBytesTransmittedPerSecond (bytes, timeStamp) ->
                    let m = telemetry.GetMetric(MetricIdentifier(Namespaces.Containers, "AverageNetworkBytesTransmittedPerSecond", Labels.Units, Labels.SiteHash, Labels.TimeStamp, Labels.Version))
                    m.TrackValue(bytes, units, site, timeStamp.ToString(), version.ToString()) |> ignore

                | AverageNetworkBytesReceivedPerSecond (bytes, timeStamp) -> 
                    let m = telemetry.GetMetric(MetricIdentifier(Namespaces.Containers, "AverageNetworkBytesReceivedPerSecond", Labels.Units, Labels.SiteHash, Labels.TimeStamp, Labels.Version))
                    m.TrackValue(bytes, units, site, timeStamp.ToString(), version.ToString()) |> ignore

                | GCRun(duration) ->
                    let m = telemetry.GetMetric(MetricIdentifier(Namespaces.GarbageCollection, "GCRun", Labels.Units, Labels.SiteHash, Labels.Version))
                    m.TrackValue(duration, units, site, version.ToString()) |> ignore

                | Created (name, timeStamp) ->
                    let m = telemetry.GetMetric(MetricIdentifier(Namespaces.Jobs, "Created", Labels.Units, Labels.Name, Labels.TimeStamp, Labels.SiteHash,  Labels.Version))
                    m.TrackValue(1.0, units, name, timeStamp.ToString(), site, version.ToString()) |> ignore

                | Deleted (name, timeStamp) ->
                    let m = telemetry.GetMetric(MetricIdentifier(Namespaces.Jobs, "Deleted", Labels.Units, Labels.Name, Labels.TimeStamp, Labels.SiteHash,  Labels.Version))
                    m.TrackValue(1.0, units, name, timeStamp.ToString(), site, version.ToString()) |> ignore

                | Tasks(tasks, total) ->
                    let mTotal = telemetry.GetMetric(MetricIdentifier(Namespaces.Jobs, "TotalPayloads", Labels.Units, Labels.SiteHash,  Labels.Version))
                    mTotal.TrackValue(float total, units, site, version.ToString()) |> ignore

                    let m = telemetry.GetMetric(MetricIdentifier(Namespaces.Jobs, "Payloads", Labels.Units, Labels.Name, Labels.Total, Labels.SiteHash,  Labels.Version))
                    tasks
                    |> Array.iter(fun (name, count) ->
                        m.TrackValue(float count, units, name, total.ToString(), site, version.ToString()) |> ignore
                    )

                    let mPercentage = telemetry.GetMetric(MetricIdentifier(Namespaces.Jobs, "Payloads %", Labels.Units, Labels.Name, Labels.Total, Labels.SiteHash,  Labels.Version))
                    tasks
                    |> Array.iter(fun (name, count) ->
                        mPercentage.TrackValue( 100.0 * (float count) / (float total), "%", name, total.ToString(), site, version.ToString()) |> ignore
                    )

                | Completed (name, timeStamp) ->
                    let m = telemetry.GetMetric(MetricIdentifier(Namespaces.Jobs, "Completed", Labels.Units, Labels.Name, Labels.TimeStamp, Labels.SiteHash,  Labels.Version))
                    m.TrackValue(1.0, units, name, timeStamp.ToString(), site, version.ToString()) |> ignore

                | BugsFound(bugsFound, toolName, timeStamp) ->
                    let m = telemetry.GetMetric(MetricIdentifier(Namespaces.Jobs, "BugsFound", Labels.Units, Labels.ToolName, Labels.TimeStamp, Labels.SiteHash,  Labels.Version))
                    m.TrackValue(float bugsFound, units, toolName, timeStamp.ToString(), site, version.ToString()) |> ignore

                | StatusCount(statusCode, count, toolName, timeStamp) ->
                    let m = telemetry.GetMetric(MetricIdentifier(Namespaces.Jobs, sprintf "%A" statusCode, Labels.Units, Labels.ToolName, Labels.TimeStamp, Labels.SiteHash,  Labels.Version))
                    m.TrackValue(float count, units, toolName, timeStamp.ToString(), site, version.ToString()) |> ignore

                | Error (name, timeStamp) ->
                    let m = telemetry.GetMetric(MetricIdentifier(Namespaces.Jobs, "Error", Labels.Units, Labels.Name, Labels.TimeStamp, Labels.SiteHash,  Labels.Version))
                    m.TrackValue(1.0, units, name, timeStamp.ToString(), site, version.ToString()) |> ignore

                | TimedOut(name, timeStamp) ->
                    let m = telemetry.GetMetric(MetricIdentifier(Namespaces.Jobs, "TimedOut", Labels.Units, Labels.Name, Labels.TimeStamp, Labels.SiteHash,  Labels.Version))
                    m.TrackValue(1.0, units, name, timeStamp.ToString(), site, version.ToString()) |> ignore

                | Exception _ ->
                    // This should not be logged with TrackMetric, just ignore.
                    ()
                | v ->
                    __.TrackError(Exception (exn(sprintf "Unhandled telemetry metric %A" v)))
            | None -> 
                ()

    let mutable Telemetry = TelemetryImpl(None)
    
    let Initialize client siteHash =
        Telemetry <- TelemetryImpl(Some(client, siteHash))
        true