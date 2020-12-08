// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

module Raft.StorageEntities

open Microsoft.Azure.Cosmos.Table

let JobStatusTableName = "JobStatus"
type JobStatusEntity(jobId, agentName, jobStatus, jobState, utcEventTime, resultsUrl) = 
    inherit TableEntity(partitionKey=jobId, rowKey=agentName)
    new() = JobStatusEntity(null, null, null, null, System.DateTime.MinValue, null)
    member val JobStatus : string = jobStatus with get, set
    member val JobState : string = jobState with get, set
    member val ResultsUrl : string = resultsUrl with get, set
    member val UtcEventTime : System.DateTime = utcEventTime with get, set


let JobTableName = "Job" 
type JobEntity(jobId, webhook) = 
    inherit TableEntity(partitionKey=jobId, rowKey=jobId)
    new() = JobEntity(null, null)
    member val Webhook : string = webhook with get, set


let JobWebHookTableName = "JobWebhook"
type JobWebhookEntity(jobId, webhookName) = 
    inherit TableEntity(partitionKey=jobId, rowKey=webhookName)
    new() = JobWebhookEntity(null, null)
