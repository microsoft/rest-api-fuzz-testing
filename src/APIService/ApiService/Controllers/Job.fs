// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Raft.Controllers

open System
open Microsoft.Azure.Cosmos.Table
open Raft.StorageEntities
open Raft.Telemetry
open Raft.Utilities
open Raft
open Raft.Controllers
open Microsoft.AspNetCore.Mvc
open FSharp.Control.Tasks.V2
open Raft.Controllers.AppInsights
open Microsoft.ApplicationInsights
open System.Runtime.InteropServices
open Microsoft.AspNetCore.Authorization
open Raft.Errors
open Microsoft.Extensions.Logging
open Microsoft.AspNetCore.Http
open Raft.Telemetry.TelemetryApiService

[<Authorize>]
[<Route("[controller]")>]
[<Produces("application/json")>]
[<ApiController>]
type jobsController(telemetryClient : TelemetryClient, logger : ILogger<jobsController>) =
    inherit ControllerBase()
    let ModuleName = "Job-"
    let tags = ["Service", "WebhookService"]
    let log = Log telemetryClient 

    let availableRegions =
        [
            let e = Microsoft.Azure.Management.ResourceManager.Fluent.Core.Region.Values.GetEnumerator()
            while e.MoveNext() do
                yield e.Current.Name
        ]
        |> Set.ofList

    // Validation function to validate the job request
    // If something does not validate, we want to throw an exception with a a standard error type
    // that is informative for the customer.
    let validateAndPatchPayload (requestPayload: DTOs.JobDefinition) =
        let inline isNotSet (o : ^T) = isNull (box o)
        let inline isSet (o : ^T) = o |> isNotSet |> not

        if Array.isEmpty requestPayload.TestTasks.Tasks then
            raiseApiError ({ 
                Error =
                    { 
                        Code = ApiErrorCode.ParseError
                        Message = sprintf "Every job definition must define at least 1 task: %A" requestPayload
                        Target = "validateAndPatchPayload"
                        Details = Array.empty
                        InnerError = {Message = ""}
                    }
                })

        let outputFolders = requestPayload.TestTasks.Tasks |> Array.map (fun t -> t.OutputFolder) |> Array.sort

        let validateApiSpecification (apiSpecification:string) =
            // Validate the swagger URL.
            if String.IsNullOrWhiteSpace(apiSpecification) then
                    raiseApiError({
                        Error = {
                            Code = ApiErrorCode.ParseError
                            Message = "Api Specification is ether null or empty"
                            Target = "validateAndPatchPayload"
                            Details = Array.empty
                            InnerError = {Message = ""}
                        }
                    })

        if isSet requestPayload.TestTasks.TargetConfiguration && isSet requestPayload.TestTasks.TargetConfiguration.ApiSpecifications then
            requestPayload.TestTasks.TargetConfiguration.ApiSpecifications |> Array.iter validateApiSpecification

        if isSet requestPayload.TestTargets && isSet requestPayload.TestTargets.Services then
            requestPayload.TestTargets.Services
            |> Array.iter(fun tt ->
                if String.IsNullOrWhiteSpace tt.Shell then
                    if isSet tt.PostRun || isSet tt.Idle || isSet tt.Run then
                        raiseApiError({
                            Error = {
                                Code = ApiErrorCode.ParseError
                                Message = "Shell value must be set if Run, PostRun, or Idle are defined in the test target"
                                Target = "validateAndPatchPayload"
                                Details = Array.empty
                                InnerError = {Message = ""}
                            }
                        })
            )

        let requestPayload =
            match isSet requestPayload.Resources, isSet requestPayload.TestTargets with
            | false, false ->
                {
                    requestPayload with
                        Resources = { Cores = 1; MemoryGBs = 1 }
                }
            | true, false -> requestPayload
            | false, true ->
                if isNotSet requestPayload.TestTargets.Resources then
                    {
                        requestPayload with
                            Resources = {Cores = 2; MemoryGBs = 2}
                            TestTargets = 
                                {
                                    requestPayload.TestTargets with
                                        Resources = {Cores = 1; MemoryGBs = 1}
                                }
                    }
                else
                    raiseApiError({
                        Error = {
                            Code = ApiErrorCode.ParseError
                            Message = "Please set global job resources (since test target resources are set)"
                            Target = "validateAndPatchPayload"
                            Details = Array.empty
                            InnerError = {Message = ""}
                        }
                    })
            | true, true ->
                if isNotSet requestPayload.TestTargets.Resources then
                    raiseApiError({
                        Error = {
                            Code = ApiErrorCode.ParseError
                            Message = "Please set test target job resources (since global resources are set)"
                            Target = "validateAndPatchPayload"
                            Details = Array.empty
                            InnerError = {Message = ""}
                        }
                    })
                else
                    if requestPayload.Resources.Cores <= requestPayload.TestTargets.Resources.Cores then
                        raiseApiError({
                            Error = {
                                Code = ApiErrorCode.ParseError
                                Message = "Number of globally allocated cores has to be greater than test targets cores"
                                Target = "validateAndPatchPayload"
                                Details = Array.empty
                                InnerError = {Message = ""}
                            }
                        })
                    if requestPayload.Resources.MemoryGBs <= requestPayload.TestTargets.Resources.MemoryGBs then
                        raiseApiError({
                            Error = {
                                Code = ApiErrorCode.ParseError
                                Message = "Globally allocated memory has to be greater than test targets allocated memory"
                                Target = "validateAndPatchPayload"
                                Details = Array.empty
                                InnerError = {Message = ""}
                            }
                        })
                    requestPayload

        if requestPayload.Resources.Cores < 1 then
            raiseApiError({
                Error = {
                    Code = ApiErrorCode.ParseError
                    Message = "Number of cores must be a positive integer."
                    Target = "validateAndPatchPayload"
                    Details = Array.empty
                    InnerError = {Message = ""}
                }
            })

        if requestPayload.Resources.MemoryGBs < 1 then
            raiseApiError({
                Error = {
                    Code = ApiErrorCode.ParseError
                    Message = "Memory in GBs allocated for the job must be a positive integer."
                    Target = "validateAndPatchPayload"
                    Details = Array.empty
                    InnerError = {Message = ""}
                }
            })

        let requestPayload =
            {requestPayload with
                TestTasks =
                    {requestPayload.TestTasks with
                        Tasks =
                            requestPayload.TestTasks.Tasks
                            |> Array.map (fun t ->
                                if isNotSet t.TargetConfiguration then
                                    { t with
                                        TargetConfiguration = requestPayload.TestTasks.TargetConfiguration
                                    }
                                else
                                    t
                            )
                            |> Array.map(fun t ->
                                if not t.Duration.HasValue then
                                    { t with Duration = requestPayload.Duration }
                                else
                                    t
                            )
                    }
                }

        let taskAuthentication =
            requestPayload.TestTasks.Tasks
            |> Array.filter(fun t -> isSet t.AuthenticationMethod)
            |> Array.map (fun t -> t.AuthenticationMethod)

        if requestPayload.TestTasks.Tasks.Length > requestPayload.Resources.MemoryGBs * 10 then
            raiseApiError ({ 
                Error =
                    { 
                        Code = ApiErrorCode.ParseError
                        Message = sprintf "Number of tasks must be less or equal to (MemoryGBs*10)"
                        Target = "validateAndPatchPayload"
                        Details = Array.empty
                        InnerError = {Message = ""}
                    }
                })

        taskAuthentication
        |> Array.iter(fun auth ->
            let enabled = 
                [ 
                    not <| String.IsNullOrWhiteSpace auth.TxtToken
                    not <| String.IsNullOrWhiteSpace auth.CommandLine
                    not <| String.IsNullOrWhiteSpace auth.MSAL
                ]
                |> List.map (fun b -> if b then 1 else 0)

            if List.sum enabled = 0 then
                raiseApiError ({ 
                                Error =
                                    { 
                                        Code = ApiErrorCode.ParseError
                                        Message = sprintf "Authentication method is defined but the method itself is not set"
                                        Target = "validateAndPatchPayload"
                                        Details = Array.empty
                                        InnerError = {Message = ""}
                                    }
                                })

            if List.sum enabled > 1 then
                raiseApiError ({ 
                                Error =
                                    { 
                                        Code = ApiErrorCode.ParseError
                                        Message = sprintf "Only one type of Authentication method is allowed per task"
                                        Target = "validateAndPatchPayload"
                                        Details = Array.empty
                                        InnerError = {Message = ""}
                                    }
                                })
        )

        let duplicateFolders =
            outputFolders 
            |> Array.pairwise 
            |> Array.filter (fun (f1, f2) -> f1.ToLowerInvariant() = f2.ToLowerInvariant())
            |> Array.map fst

        if not <| Array.isEmpty duplicateFolders then
            raiseApiError ({
                Error =
                    {
                        Code = ApiErrorCode.InvalidJob
                        Message = sprintf "Output folders must be unique. Found duplicate folders: %A" duplicateFolders
                        Target = "validateAndPatchPayload"
                        Details = Array.empty
                        InnerError = {Message = ""}
                    }
            })

        requestPayload.TestTasks.Tasks
        |> Array.iter(fun t ->
            if not <| Utilities.toolsSchemas.ContainsKey t.ToolName then
                raiseApiError({
                    Error = {
                        Code = ApiErrorCode.ParseError
                        Message = sprintf "Tool %s is not supported" t.ToolName
                        Target = "validateAndPatchPayload"
                        Details = Array.empty
                        InnerError = {Message = ""}
                    }
                })

            if isSet t.TargetConfiguration && isSet t.TargetConfiguration.ApiSpecifications then
                t.TargetConfiguration.ApiSpecifications |> Array.iter validateApiSpecification
        )

        if isSet requestPayload.ReadOnlyFileShareMounts then
            requestPayload.ReadOnlyFileShareMounts
            |> Array.iter(fun fs ->
                if String.IsNullOrWhiteSpace fs.FileShareName || String.IsNullOrWhiteSpace fs.MountPath then
                    raiseApiError({
                        Error = {
                            Code = ApiErrorCode.ParseError
                            Message = "ReadOnlyFileShareMounts has to have non-null and non-empty FileShareName and MountPath"
                            Target = "validateAndPatchPayload"
                            Details = Array.empty
                            InnerError = {Message = ""}
                        }
                    })
            )

        if isSet requestPayload.ReadWriteFileShareMounts then
            requestPayload.ReadWriteFileShareMounts
            |> Array.iter(fun fs ->
                if String.IsNullOrWhiteSpace fs.FileShareName || String.IsNullOrWhiteSpace fs.MountPath then
                    raiseApiError({
                        Error = {
                            Code = ApiErrorCode.ParseError
                            Message = "ReadWriteFileShareMounts has to have non-null and non-empty FileShareName and MountPath"
                            Target = "validateAndPatchPayload"
                            Details = Array.empty
                            InnerError = {Message = ""}
                        }
                    })
            )

        //U+0000 to U+001F
        //U+007F to U+009F
        // '/' '\', '#', '?'
        if not <| String.IsNullOrWhiteSpace requestPayload.NamePrefix && requestPayload.NamePrefix
           |> Seq.exists (fun c -> (Char.IsControl c) || c = '/' || c = '\\' || c = '#' || c = '?' || c = '\t' || c = '\n' || c = '\r' ) then
            raiseApiError({
                Error = {
                    Code = ApiErrorCode.ParseError
                    Message = "Name prefix contains disallowed character(s). Any of control characters or / \\ # ? \\t \\n \\r"
                    Target = "validateAndPatchPayload"
                    Details = Array.empty
                    InnerError = {Message = ""}
                }
            })

        if isSet requestPayload.Webhook then
            if String.IsNullOrWhiteSpace requestPayload.Webhook.Name then
                raiseApiError({
                    Error = {
                        Code = ApiErrorCode.ParseError
                        Message = "Webhook name must be a non-empty string"
                        Target = "validateAndPatchPayload"
                        Details = Array.empty
                        InnerError = {Message = ""}
                    }
                })

        requestPayload

    let jobStatusEntities(jobId) =
        task {
            let query = TableQuery<JobStatusEntity>().Where(TableQuery.GenerateFilterCondition(Constants.PartitionKey, QueryComparisons.Equal, jobId))
            let! results = Utilities.raftStorage.GetJobStatusEntities query

            match results |> Seq.tryFind (fun (r:JobStatusEntity) -> r.PartitionKey = r.RowKey) with
            | None -> return None
            | Some r -> return Some (r, results)
        }

    let convertToJobStatus(overallJobStatus: JobStatusEntity) (results: JobStatusEntity seq) =
        results
        |> Seq.map (fun jobStatusEntity -> (Raft.Message.RaftEvent.deserializeEvent jobStatusEntity.JobStatus): Message.RaftEvent.RaftJobEvent<DTOs.JobStatus>)
        |> Seq.map (fun jobStatus -> 
            if jobStatus.Message.AgentName = jobStatus.Message.JobId then
                { jobStatus.Message with ResultsUrl = overallJobStatus.ResultsUrl }
            else
                jobStatus.Message
        )


    [<HttpPost>]
    /// <summary>
    /// Submit a job definition.
    /// </summary>
    /// <param name="region">
    ///     Run the job definition in a specified region. If not set - run in the same region as the service
    ///     https://docs.microsoft.com/en-us/azure/container-instances/container-instances-region-availability
    /// </param>
    /// <param name="body">The new job definition to run</param>
    /// <response code="200">Returns the newly created jobId</response>
    /// <response code="400">If there was an error in the request</response>  
    [<ProducesResponseType(typeof<Raft.Job.CreateJobResponse>, StatusCodes.Status200OK)>]
    [<ProducesResponseType(typeof<ApiError>, StatusCodes.Status400BadRequest)>]
    member this.Post([<FromQuery>] region : string, [<FromBody>] body : DTOs.JobDefinition ) =
        task {
            try
                let method = ModuleName + "Post"
                if (not <| String.IsNullOrWhiteSpace region) && (not <| availableRegions.Contains(region.ToLowerInvariant())) then
                    log.Error (sprintf "Invalid region set: %s" region) []
                    raiseApiError ({ 
                                    Error =
                                        { 
                                            Code = ApiErrorCode.InternalError
                                            Message = sprintf "Region %s is not valid. Valid regions are: %s" region (String.Join("; ", availableRegions))
                                            Target = method
                                            Details = Array.empty
                                            InnerError = {Message = ""}
                                        }
                                    })

                let stopWatch = System.Diagnostics.Stopwatch()
                do stopWatch.Start()

                let createQueue = Raft.Message.ServiceBus.Queue.create

                let validatedPayload = validateAndPatchPayload body

                let jobId = sprintf "%s%O" (if String.IsNullOrWhiteSpace body.NamePrefix then String.Empty else body.NamePrefix.Trim()) (Guid.NewGuid())
                log.Info "Creating new job ID" ["method", method; "JobId", jobId; "region", region; "body", sprintf "%A" validatedPayload]

                let validatedBody : DTOs.CreateJobRequest = { JobId = jobId; JobDefinition = validatedPayload; IsIdlingRun = false; Region = region}

                if not (isNull (box validatedBody.JobDefinition.Webhook)) then
                    let tags = ["method", method; "JobId", jobId; "region", region; "webhook", validatedBody.JobDefinition.Webhook.Name]
                    log.Info "Configuring webhook" tags

                    let entity = JobWebhookEntity(validatedBody.JobId, validatedBody.JobDefinition.Webhook.Name) :> TableEntity
                    log.Info "Inserting webhook entity" tags
                    match! Utilities.raftStorage.InsertEntity Raft.StorageEntities.JobWebHookTableName entity with
                    | Result.Ok() -> 
                        log.Info "Successfully inserted webhook entity" tags
                    | Result.Error (statusCode) ->
                        let errorMessage = sprintf "Insert into %s failed. HttpStatusCode is %d. JobId is %O" Raft.StorageEntities.JobWebHookTableName statusCode validatedBody.JobId
                        log.Error errorMessage tags
                        raiseApiError ({ 
                                        Error =
                                            { 
                                                Code = ApiErrorCode.InternalError
                                                Message = "Insert into table failed"
                                                Target = method
                                                Details = Array.empty
                                                InnerError = {Message = errorMessage}
                                            }
                                        })

                log.Info "Posting job creation request to the orchestrator queue" ["method", method; "JobId", jobId; "region", region]
                do! sendCommand createQueue  validatedBody.JobId 
                                            {
                                                Message = validatedBody
                                                MessagePostCount = 0
                                            }

                let serializedWebhook = Newtonsoft.Json.JsonConvert.SerializeObject(validatedBody.JobDefinition.Webhook)
                let entity = JobEntity(validatedBody.JobId, serializedWebhook)

                log.Info "Inserting job entity into job table" ["method", method; "JobId", jobId; "region", region]
                match! Utilities.raftStorage.InsertEntity Raft.StorageEntities.JobTableName entity with
                | Result.Ok() ->
                    log.Info "Successfully inserted job entity into job table" ["method", method; "JobId", jobId; "region", region]
                | Result.Error (statusCode) ->
                    let errorMessage = sprintf "Insert into %s failed. HttpStatusCode is %d. JobId is %O" Raft.StorageEntities.JobTableName statusCode validatedBody.JobId
                    log.Error errorMessage ["method", method; "JobId", jobId; "region", region]
                    raiseApiError ({ 
                                    Error =
                                        { 
                                            Code = ApiErrorCode.InternalError
                                            Message = "Insert into table failed"
                                            Target = method
                                            Details = Array.empty
                                            InnerError = {Message = errorMessage}
                                        }
                                    })

                stopWatch.Stop()
                Central.Telemetry.TrackMetric (TelemetryValues.ApiRequest(method, float stopWatch.ElapsedMilliseconds), "milliseconds", this :> ControllerBase)
                return JsonResult({JobId = validatedBody.JobId} : DTOs.JobResponse)
            with
            | Errors.ApiErrorException (apiError) as ex ->
                Central.Telemetry.TrackError (TelemetryValues.Exception ex)
                return setApiErrorStatusCode apiError Microsoft.AspNetCore.Http.StatusCodes.Status400BadRequest
        }


    //Move this to a separate controller ? something like ends with "/debug" ? 
    //Since this way of working with jobs is intended for fast iterations when doing initial setup of the job
    [<HttpPost("{jobId}")>]
    /// <summary>
    /// Repost a job definition to an existing job.
    /// </summary>
    /// <remarks>
    /// The existing job must have been created with IsIdling set to true.
    /// </remarks>
    /// <param name="jobId">The id which refers to an existing job</param>
    /// <param name="body">The new job definition to run</param>
    /// <response code="200">Returns the newly created jobId</response>
    /// <response code="400">If there was an error in the request</response>  
    [<ProducesResponseType(typeof<Raft.Job.CreateJobResponse>, StatusCodes.Status200OK)>]
    [<ProducesResponseType(typeof<ApiError>, StatusCodes.Status400BadRequest)>]
    member this.RePost(jobId: string, [<FromBody>] body : DTOs.JobDefinition ) =
        task {
            let stopWatch = System.Diagnostics.Stopwatch()
            do stopWatch.Start()

            let method = ModuleName + "RePost"

            try
                match! jobStatusEntities jobId with
                | None ->
                    raiseApiError ({ 
                                    Error =
                                        { 
                                            Code = ApiErrorCode.NotFound
                                            Message = "JobId was not found. The jobId must already exist and have been created with the IsIdling flag set to true."
                                            Target = method
                                            Details = Array.empty
                                            InnerError = {Message = sprintf "Job table search for job with ID %O failed" jobId}
                                        }
                                    })
                | Some (r, _) ->
                    let status : Message.RaftEvent.RaftJobEvent<DTOs.JobStatus> = Raft.Message.RaftEvent.deserializeEvent r.JobStatus
                    match status.Message.State with
                    | DTOs.JobState.Created | DTOs.JobState.ReStarted | DTOs.JobState.Running | DTOs.JobState.Error -> ()
                    | _ ->
                        raiseApiError ({ 
                                        Error =
                                            { 
                                                Code = ApiErrorCode.InvalidJob
                                                Message = "Job must be in active state to re-post"
                                                Target = method
                                                Details = Array.empty
                                                InnerError = {Message = sprintf "Job %O is in %A state" jobId status.Message.State}
                                            }
                                        })

                let createQueue = Raft.Message.ServiceBus.Queue.create
                let validatedPayload = validateAndPatchPayload body

                let repost : DTOs.CreateJobRequest = {JobId =  jobId; JobDefinition = validatedPayload; IsIdlingRun = true; Region = null}

                do! sendCommand createQueue repost.JobId {Message = repost; MessagePostCount = 0}

                stopWatch.Stop()
                Central.Telemetry.TrackMetric (TelemetryValues.ApiRequest(method, float stopWatch.ElapsedMilliseconds), "milliseconds", this :> ControllerBase)
                return JsonResult({JobId = repost.JobId} : DTOs.JobResponse)
            with
            | Errors.ApiErrorException (apiError) as ex ->
                Central.Telemetry.TrackError (TelemetryValues.Exception ex)
                match apiError.Error.Code with
                | ApiErrorCode.NotFound ->
                    return setApiErrorStatusCode apiError Microsoft.AspNetCore.Http.StatusCodes.Status404NotFound
                | _ ->
                    return setApiErrorStatusCode apiError Microsoft.AspNetCore.Http.StatusCodes.Status400BadRequest

        }


    /// <summary>
    /// Deletes a job
    /// </summary>
    /// <param name="jobId">The id which refers to an existing job</param>
    /// <response code="200">Returns the newly created jobId</response>
    /// <response code="404">If the job was not found</response>  
    [<ProducesResponseType(typeof<Raft.Job.CreateJobResponse>, StatusCodes.Status200OK)>]
    [<ProducesResponseType(typeof<ApiError>, StatusCodes.Status404NotFound)>]
    [<HttpDelete("{jobId}")>]
    member this.Delete (jobId : string)  =
        task {
            let stopWatch = System.Diagnostics.Stopwatch()
            do stopWatch.Start()

            let method = ModuleName + "Delete"

            try
                // Verify that we find a job with this jobId in the job status table.
                let query = TableQuery<JobStatusEntity>().Where(TableQuery.GenerateFilterCondition(Constants.PartitionKey, QueryComparisons.Equal, jobId.ToString()))
                let! results = raftStorage.GetJobStatusEntities query
                if Seq.isEmpty results then
                    return raiseApiError { Error = { Code = ApiErrorCode.NotFound
                                                     Message = sprintf "Job status for job id %O not found." jobId
                                                     Target = method
                                                     Details = [||]
                                                     InnerError = {Message = ""}
                                                   }} 
                else
                    let message : DTOs.DeleteJobRequest = {JobId = jobId}

                    do! sendCommand Raft.Message.ServiceBus.Queue.delete jobId {  Message = message; MessagePostCount = 0 }

                    stopWatch.Stop()
                    Central.Telemetry.TrackMetric (TelemetryValues.ApiRequest(method, float stopWatch.ElapsedMilliseconds), "milliseconds", this :> ControllerBase)
                    return JsonResult({JobId = jobId} : DTOs.JobResponse )
            with 
            | Errors.ApiErrorException (apiError) as ex ->
                Central.Telemetry.TrackError (TelemetryValues.Exception ex)
                return setApiErrorStatusCode apiError Microsoft.AspNetCore.Http.StatusCodes.Status404NotFound
      }

    [<HttpGet("{jobId}")>]
    /// <summary>
    /// Returns status for the specified job.
    /// </summary>
    /// <response code="200">Returns the job status data</response>
    /// <response code="404">If the job was not found</response>  
    [<ProducesResponseType(typeof<seq<DTOs.JobStatus>>, StatusCodes.Status200OK)>]
    [<ProducesResponseType(typeof<ApiError>, StatusCodes.Status404NotFound)>]
    member this.GetJobStatus (jobId : string)  =
        task {
            let stopWatch = System.Diagnostics.Stopwatch()
            do stopWatch.Start()

            let method = ModuleName + "GetJobStatus"
            let! results = jobStatusEntities jobId
            match results with
            | None ->
                printfn "Results sequence is discarded since overall job status is not yet set after executing table query for job: %A" jobId
                return setApiErrorStatusCode { Error = { Code = ApiErrorCode.NotFound
                                                         Message = sprintf "Job status for job id %O not found." jobId
                                                         Target = method
                                                         Details = [||]
                                                         InnerError = {Message = ""}
                                                }} Microsoft.AspNetCore.Http.StatusCodes.Status404NotFound
            | Some (r, results) ->
                let decodedMessages = convertToJobStatus r results
                stopWatch.Stop()
                Central.Telemetry.TrackMetric (TelemetryValues.ApiRequest(method, float stopWatch.ElapsedMilliseconds), "milliseconds", this :> ControllerBase)
                return JsonResult(decodedMessages)
        }


    [<HttpGet>]
    /// <summary>
    /// Returns in an array status for all jobs over the last 24 hours.
    /// </summary>
    /// <remarks>
    /// The default timespan is over the last 24 hours. Use the query string timeSpanFilter="TimeSpan" to specify a different interval.
    /// Use a time format that can be parsed as a TimeSpan data type.
    /// If no data is found the result will be an empty array.
    /// </remarks>
    /// <param name="timeSpanFilter">A string which is interpreted as a TimeSpan</param>
    /// <response code="200">Returns the job status data</response>
    [<ProducesResponseType(typeof<seq<DTOs.JobStatus>>, StatusCodes.Status200OK)>]
    [<ProducesResponseType(typeof<ApiError>, StatusCodes.Status404NotFound)>]
    member this.List([<Optional;FromQuery>] timeSpanFilter:Nullable<TimeSpan>)  =
        task {
            let stopWatch = System.Diagnostics.Stopwatch()
            do stopWatch.Start()

            let method = ModuleName + "List"

            let defaultTimeSpan = TimeSpan.FromDays(1.0)
            let query = 
                if timeSpanFilter.HasValue then
                    TableQuery<JobStatusEntity>().Where(
                        TableQuery.GenerateFilterConditionForDate(
                            Constants.Timestamp, QueryComparisons.GreaterThanOrEqual,
                            DateTimeOffset.Now.Subtract(timeSpanFilter.Value)))
                else  
                    TableQuery<JobStatusEntity>().Where(
                        TableQuery.GenerateFilterConditionForDate(
                            Constants.Timestamp, QueryComparisons.GreaterThanOrEqual,
                            DateTimeOffset.Now.Subtract(defaultTimeSpan)))

            let! result = Utilities.raftStorage.GetJobStatusEntities query

            let statuses : DTOs.JobStatus seq = result
                                                |> Seq.map(fun (s: JobStatusEntity) ->
                                                    try
                                                        Some (Raft.Message.RaftEvent.deserializeEvent s.JobStatus)
                                                    with
                                                    | ex ->
                                                        log.Error (sprintf "Failed to deserialize: %A due to %A" s.JobStatus ex) []
                                                        None
                                                    )
                                                |> Seq.choose id
                                                |> Seq.map (fun jobStatus -> jobStatus.Message)

            stopWatch.Stop()
            Central.Telemetry.TrackMetric (TelemetryValues.ApiRequest(method, float stopWatch.ElapsedMilliseconds), "milliseconds", this :> ControllerBase)
            return JsonResult(statuses)

        }

      
