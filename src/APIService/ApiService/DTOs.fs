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
    type TargetConfiguration =
        {
            /// <summary>
            /// Override Endpoint for each request.
            /// </summary>
            Endpoint : System.Uri

            /// <summary>
            /// Paths to folders containing certificates to add to trusted certificate store. Certificates must have .crt extension.
            /// These certificates are used for SSL authentication.
            /// </summary>
            Certificates : string array

            /// <summary>
            /// List of OpenApi/swagger specifications locations for the job run. Can be URL or file path.
            /// </summary>
            ApiSpecifications : string array
        }

    /// <summary>
    /// RAFT task to run.
    /// </summary>
    [<CLIMutable>]
    type RaftTask =
        {
            /// <summary>
            /// Tool defined by folder name located in cli/raft-tools/tools/{ToolName}
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
            /// Override TargetConfiguration
            /// </summary>
            TargetConfiguration : TargetConfiguration

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

            /// <summary>
            /// Tool configuration. This configuration is defined by a swagger document in
            /// schema.json document located in the raft-tools/(folder named after the tool).
            /// </summary>
            ToolConfiguration : Newtonsoft.Json.Linq.JObject
        }

    [<CLIMutable>]
    type TestTasks =
        {
            TargetConfiguration: TargetConfiguration

            [<Required>]
            Tasks : RaftTask array
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

    [<CLIMutable>]
    type Command =
        {
            ShellArguments : string array
            ExpectedRunDuration: Nullable<TimeSpan>
        }

    [<CLIMutable>]
    type ServiceDefinition =
        {
            [<Required>]
            Container : string
            Ports : int array
            ExpectedDurationUntilReady: TimeSpan

            IsIdling : Nullable<bool>

            Run : Command
            Idle : Command
            PostRun : Command
            OutputFolder : string
            Shell : string

            EnvironmentVariables : IDictionary<string, string>
            KeyVaultSecrets : string array
        }

    [<CLIMutable>]
    type TestTargets =
        {
            Resources : Resources
            Services : ServiceDefinition array
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
            /// Test tasks definitions
            /// </summary>
            [<Required>]
            TestTasks : TestTasks

            /// <summary>
            /// Deploy Services under test packaged as Docker container to the same
            /// container grop as Tasks
            /// </summary>
            TestTargets : TestTargets

            /// <summary>
            /// Duration of the job; if not set, then job runs till completion (or forever).
            /// For RESTler jobs - time limit is only useful for Fuzz task
            /// </summary>
            Duration: Nullable<TimeSpan>

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

    [<CLIMutable>]
    type DeleteJobRequest = 
        {
            JobId : string
        }

    // We need some way of designating that these types are returned to the customer
    // public? external? ReturnedJobId? 
    // We have types we consume and types we return to the customer. What is the best
    // naming convention?
    [<CLIMutable>]
    type JobResponse =
        {
            JobId : string
        }
        
    type JobState =
        | Creating = 0
        | Created = 1
        | Running = 2
        | Completing = 3
        | Completed = 4
        | ManuallyStopped = 5
        | Error = 6
        | TimedOut = 7
        | ReStarted = 8
        | TaskCompleted = 9

    [<CLIMutable>]
    type RunSummary =
        {
            TotalRequestCount : int
            ResponseCodeCounts : Dictionary<int, int>
            TotalBugBucketsCount : int
        }

    [<CLIMutable>]
    type JobStatus =
        {
            Tool : string

            JobId : string

            State : JobState

            Metrics : RunSummary

            UtcEventTime : DateTime

            Details : IDictionary<string, string>

            AgentName : string 

            ResultsUrl : string

            Metadata : Dictionary<string, string>
        }
