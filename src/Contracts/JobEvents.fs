// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

module Raft.JobEvents

open System

type JobState =
    | Creating
    | Created
    | Running
    | Completed
    | ManuallyStopped
    | Error
    | TimedOut

/// Returns True if s1 state has higher precedence than s2
/// False otherwise
let ( ??> ) (s1: JobState) (s2: JobState) =
    match s1, s2 with
    | JobState.Creating, _ -> false
    
    | JobState.Created, JobState.Creating -> true
    | JobState.Created, _ -> false
    
    | JobState.Running, (JobState.Creating | JobState.Created) -> true
    | JobState.Running, _ -> false

    | JobState.ManuallyStopped, (JobState.Creating | JobState.Created | JobState.Running | JobState.Completed | JobState.Error) -> true
    | JobState.ManuallyStopped, _ -> false

    | JobState.Completed , (JobState.Error | JobState.Completed) -> false
    | (JobState.Completed | JobState.TimedOut | JobState.Error), _ -> true

type RunSummary =
    {
        TotalRequestCount: int
        ResponseCodeCounts: Map<int, int>
        TotalBugBucketsCount: int
    }

    static member Empty =
        {
            TotalBugBucketsCount = 0
            TotalRequestCount = 0
            ResponseCodeCounts = Map.empty
        }


module Events =
    
    type JobEventTypes =
        | JobStatus
        | BugFound


type JobStatus =
    {
        Tool: string
        JobId : string
        State: JobState
        Metrics: RunSummary option
        UtcEventTime: DateTime
        Details: Map<string, string> option
        Metadata : Map<string, string> option
        AgentName: string
    }

    static member EventType = Events.JobEventTypes.JobStatus.ToString()

type BugFound =
    {
        Tool : string
        JobId: string
        AgentName : string
        Metadata : Map<string, string> option
        BugDetails : Map<string, string> option
    }

    static member EventType = Events.JobEventTypes.BugFound.ToString()