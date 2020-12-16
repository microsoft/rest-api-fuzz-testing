// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Raft.Job

module Authentication =
    type TokenRefresh =
        | CommandLine of string
        | MSAL of string
        | TxtToken of string

type ApiSpecification = string

type GpuConfig =
    {
        Sku : string //available SKUs v100, k80, P100
        Cores : int
    }


type Resources =
    {
        Cores : int
        MemoryGBs : int

        GPU : GpuConfig option
    }

type Command =
    {
        ShellArguments : string array option
        ExpectedRunDuration: System.TimeSpan option
    }

type ServiceDefinition =
    //accessible within conatiner group at localhost:port
    {
        Container : string
        ExpectedDurationUntilReady: System.TimeSpan
        Ports : int array option

        IsIdling : bool option
        
        Shell : string option
        Run : Command option
        Idle : Command option
        PostRun : Command option
        
        OutputFolder : string option

        EnvironmentVariables : Map<string, string> option
        KeyVaultSecrets : string array option
    }

type TestTarget =
    {
        Resources : Resources
        Services : ServiceDefinition array
    }

type TargetConfiguration =
    {
        Host : string option
        Port : int option
        IP : string option

        /// where to get service under test API specifications definition from
        /// Could be file path or url
        ApiSpecifications : (ApiSpecification array) option
    }

type RaftTask =
    {
        ToolName: string

        TargetConfiguration: TargetConfiguration option

        IsIdling: bool
        /// Output folder name to store agent generated output
        OutputFolder : string

        /// Duration of the job; if not set, then job runs till completion
        Duration: System.TimeSpan option

        //list of names of secrets in Keyvault that payload allowed to access
        KeyVaultSecrets : string array option

        AuthenticationMethod : Authentication.TokenRefresh option

        ToolConfiguration : Newtonsoft.Json.Linq.JObject
    }

type TestTasks =
    {
        TargetConfiguration : TargetConfiguration option
        Tasks : RaftTask array
    }

type FileShareMount =
    {
        FileShareName : string //any fileShare name from the service storage account
        MountPath : string //example "/my-job-config"
    }

type Webhook =
    {
        Name : string
        Metadata : Map<string, string> option
    }


type JobDefinition =
    {
        /// prefix for jobId
        NamePrefix : string option

        /// root file share to use to write job results
        /// instead of creating new file share per each job run
        RootFileShare : string option

        Resources : Resources


        // !!NOTE!!: according to Azure Container spec, we can have up to 60 elements
        // of TestTargets and Tasks combined
        TestTargets : TestTarget option
        TestTasks : TestTasks

        /// Duration of the job; if not set, then job runs till completion
        Duration: System.TimeSpan option

        /// The string to use in overriding the Host for each request
        Host : string option

        /// Name of the webhook
        Webhook : Webhook option

        // File shares to mount from RAFT deployment storage account.
        // Use case: RESTler compiles swagger as a job run
        // To use the output from the compile step - mount the file share produced
        // by compile step, and use grammar from that step to run fuzz step
        // !!NOTE!!: according to Azure spec we can have up-to 19 elements in this list (since we reserve 1 to mount as working directory)
        ReadOnlyFileShareMounts : (FileShareMount array) option

        ReadWriteFileShareMounts : (FileShareMount array) option
    }


type CreateJobRequest =
    {
        IsIdlingRun : bool

        Region : string option

        JobId : string

        JobDefinition: JobDefinition
    }

type DeleteJobRequest =
    {
        JobId : string
    }

// We need some way of designating that these types are returned to the customer
// public? external? ReturnedJobId? 
// We have types we consume and types we return to the customer. What is the best
// naming convention?
type CreateJobResponse = 
    {
        JobId : string
    }


