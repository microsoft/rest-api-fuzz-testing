// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

module Raft.StorageEntities

open Microsoft.Azure.Cosmos.Table

let JobStatusTableName = "JobStatus"
type JobStatusEntity(jobId, agentName, jobStatus, jobState) = 
    inherit TableEntity(partitionKey=jobId, rowKey=agentName)
    new() = JobStatusEntity(null, null, null, null)
    member val JobStatus : string = jobStatus with get, set
    member val JobState : string = jobState with get, set


let JobTableName = "Job" 
type JobEntity(jobId, webhook) = 
    inherit TableEntity(partitionKey=jobId, rowKey=jobId)
    new() = JobEntity(null, null)
    member val Webhook : string = webhook with get, set


let JobWebHookTableName = "JobWebhook"
type JobWebhookEntity(jobId, webhookName) = 
    inherit TableEntity(partitionKey=jobId, rowKey=webhookName)
    new() = JobWebhookEntity(null, null)
