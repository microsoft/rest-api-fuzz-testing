// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Raft.Controllers

//https://docs.microsoft.com/en-us/dotnet/csharp/codedoc
module DTOs =
    open System
    open System.Collections.Generic
    open System.ComponentModel.DataAnnotations

    /// <summary>
    /// Method of authenticating with the service under test
    /// </summary>
    [<CLIMutable>]
    type AuthenticationMethod =
        {
            /// <summary>
            /// KeyVault Secret name containing MSAL authentication information in following format:
            /// { "tenant" : "&lt;tenantId&gt;", "client" : "&lt;clientid&gt;", "secret" : "&lt;secret&gt;"}
            /// optional values that could be passed as part of the JSON: scopes and authorityUri
            /// </summary>
            MSAL: string

            /// <summary>
            /// Command line to execute that acquires authorization token and prints it to standard output
            /// </summary>
            CommandLine: string

            /// <summary>
            /// KeyVault Secret name containing plain text token
            /// </summary>
            TxtToken: string
    }

    [<CLIMutable>]
    type SwaggerLocation =
        {
            URL: string
            FilePath: string
        }

    /// <summary>
    /// RAFT task to run.
    /// </summary>
    [<CLIMutable>]
    type RaftTask =
        {
            /// <summary>
            /// Tool defined by folder name located in cli/raft-utils/tools/{ToolName}
            /// </summary>
            [<Required>]
            ToolName: string

            /// <summary>
            /// Output folder name to store agent generated output
            /// Must not contain: /\*?:|&lt;&gt;"
            /// </summary>
            [<Required>]
            OutputFolder : string

            /// <summary>
            /// Override swagger specification location
            /// </summary>
            SwaggerLocation : SwaggerLocation

            /// <summary>
            /// Override the Host for each request.
            /// </summary>
            Host : string

            /// <summary>
            /// If true - do not run the task. Idle container to allow user to connect to it.
            /// </summary>
            IsIdling: bool

            /// <summary>
            /// Duration of the task; if not set, then job level duration is used.
            /// For RESTler jobs - time limit is only useful for Fuzz task
            /// </summary>
            Duration: Nullable<TimeSpan>

            /// <summary>
            /// Authentication method configuration
            /// </summary>
            AuthenticationMethod: AuthenticationMethod

            /// <summary>
            /// List of names of secrets in Keyvault used in configuring 
            /// authentication credentials
            /// Key Vault secret name must start with alphabetic character and
            /// followed by a string of alphanumeric characters (for example 'MyName123').
            /// Secret name can be upto 127 characters long
            /// </summary>
            KeyVaultSecrets : string array

            ToolConfiguration : Newtonsoft.Json.Linq.JObject
        }
    /// <summary>
    /// Mount file share from RAFT storage account to container running a payload.
    /// </summary>
    [<CLIMutable>]
    type FileShareMount =
        {
            /// <summary>
            /// Any fileShare name from the RAFT storage account
            /// </summary>
            FileShareName : string

            /// <summary>
            /// Directory name under which file share is mounted on the container. For example "/my-job-config"
            /// </summary>
            MountPath : string
        }


    /// <summary>
    /// Hardware resources to allocate for the job
    /// </summary>
    [<CLIMutable>]
    type Resources =
        {
            /// <summary>
            /// Number of cores to allocate for the job.
            /// Default is 1 core.
            /// see: https://docs.microsoft.com/en-us/azure/container-instances/container-instances-region-availability
            /// </summary>
            Cores : int

            /// <summary>
            /// Memory to allocate for the job
            /// Default is 1 GB.
            /// see: https://docs.microsoft.com/en-us/azure/container-instances/container-instances-region-availability
            /// </summary>
            MemoryGBs : int
        }

    /// <summary>
    /// Webhook definition
    /// </summary>
    [<CLIMutable>]
    type Webhook =
        {
            /// <summary>
            /// Webhook name to associate with the job
            /// </summary>
            Name : string

            /// <summary>
            /// Arbitrary key/value pairs that will be returned in webhooks
            /// </summary>
            Metadata : Dictionary<string, string>
        }

    /// <summary>
    /// RAFT job run definition
    /// </summary>
    [<CLIMutable>]
    type JobDefinition =
        {
            /// <summary>
            /// Swagger specification location for the job run
            /// </summary>
            [<Required>]
            SwaggerLocation : SwaggerLocation

            /// <summary> 
            /// String used as a prefix added to service generated job ID.
            /// Prefix can contain only lowercase letters, numbers, and hyphens, and must begin with a letter or a number. 
            /// Prefix cannot contain two consecutive hyphens.
            /// Can be up to 27 characters long
            /// </summary>
            NamePrefix : string

            /// <summary>
            /// Hardware resources to allocate for the job
            /// </summary>
            Resources : Resources

            /// <summary>
            /// RAFT Task definitions
            /// </summary>
            Tasks : RaftTask array

            /// <summary>
            /// Duration of the job; if not set, then job runs till completion (or forever).
            /// For RESTler jobs - time limit is only useful for Fuzz task
            /// </summary>
            Duration: Nullable<TimeSpan>

            /// <summary>
            /// Override the Host for each request.
            /// </summary>
            Host : string

            /// <summary>
            /// Webhook to trigger when running this job
            /// </summary>
            Webhook : Webhook

            /// <summary>
            /// If set, then place all job run results into this file share
            /// </summary>
            RootFileShare : string

            /// <summary>
            /// File shares to mount from RAFT deployment storage account as read-only directories.
            /// </summary>
            ReadOnlyFileShareMounts : FileShareMount array

            /// <summary>
            /// File shares to mount from RAFT deployment storage account as read-write directories.
            /// </summary>
            ReadWriteFileShareMounts : FileShareMount array
        }

    [<CLIMutable>]
    type CreateJobRequest =
        {
            JobId : string

            JobDefinition: JobDefinition

            IsIdlingRun : bool

            Region : string
        }


    type DeleteJobRequest() =
        member val JobId : string = null with get, set


    // We need some way of designating that these types are returned to the customer
    // public? external? ReturnedJobId? 
    // We have types we consume and types we return to the customer. What is the best
    // naming convention?
    type CreateJobResponse() = 
        member val JobId : string = null with get, set

    type JobState =
        | Creating = 0
        | Created = 1
        | Running = 2
        | Completed = 3
        | ManuallyStopped = 4
        | Error = 5
        | TimedOut = 6

    let JobStateFromInternal (s : Raft.JobEvents.JobState) =
        match s with
        | Raft.JobEvents.JobState.Creating -> JobState.Creating
        | Raft.JobEvents.JobState.Created -> JobState.Created
        | Raft.JobEvents.JobState.Running -> JobState.Running
        | Raft.JobEvents.JobState.Completed -> JobState.Completed
        | Raft.JobEvents.JobState.ManuallyStopped -> JobState.ManuallyStopped
        | Raft.JobEvents.JobState.Error -> JobState.Error
        | Raft.JobEvents.JobState.TimedOut -> JobState.TimedOut

    type RunSummary() =
        member val TotalRequestCount : int = 0 with get, set
        member val ResponseCodeCounts : Dictionary<int, int> = Dictionary() with get, set
        member val TotalBugBucketsCount : int = 0 with get, set

        static member FromInternal (s: Raft.JobEvents.RunSummary) =
            RunSummary(
                TotalRequestCount = s.TotalRequestCount, 
                ResponseCodeCounts = Dictionary(s.ResponseCodeCounts), 
                TotalBugBucketsCount = s.TotalBugBucketsCount)

    type JobStatus () =
        member val Tool : string = "NotSet" with get, set

        member val JobId : string = null with get, set

        member val State : JobState = JobState.Creating with get, set

        member val Metrics : RunSummary = Unchecked.defaultof<_> with get, set

        member val UtcEventTime : DateTime = DateTime.MinValue with get, set

        member val Details : (string array) = [||] with get, set

        member val AgentName : string = null with get, set

        static member FromInternal (j : Raft.JobEvents.JobStatus) =
            JobStatus (
                Tool = j.Tool,

                JobId = j.JobId,
                State = JobStateFromInternal j.State,
                Metrics = (
                    match j.Metrics with
                    | None -> Unchecked.defaultof<_>
                    | Some m -> RunSummary.FromInternal m),

                UtcEventTime = j.UtcEventTime,
                Details = (match j.Details with Some d -> (d |> Seq.toArray) | None -> [||]),
                AgentName = j.AgentName
            )
