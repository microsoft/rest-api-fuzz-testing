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

    // Validation function to validate the job request
    // If something does not validate, we want to throw an exception with a a standard error type
    // that is informative for the customer.
    let validateAndPatchPayload (requestPayload: DTOs.JobDefinition) =
        if Array.isEmpty requestPayload.Tasks then
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

        let outputFolders = requestPayload.Tasks |> Array.map (fun t -> t.OutputFolder) |> Array.sort

        let requestPayload =
            {requestPayload with
                Tasks =
                    requestPayload.Tasks
                    |> Array.map (fun t ->
                        if isNull (box t.SwaggerLocation) then
                            { t with SwaggerLocation = requestPayload.SwaggerLocation }
                        else 
                            t
                    )
                    |> Array.map(fun t ->
                        if String.IsNullOrWhiteSpace t.Host then
                            { t with Host = requestPayload.Host }
                        else
                            t
                    )
                    |> Array.map(fun t ->
                        if not t.Duration.HasValue then
                            { t with Duration = requestPayload.Duration }
                        else
                            t
                    )

                Resources =
                    if isNull (box requestPayload.Resources) then
                        {
                            Cores = 1
                            MemoryGBs = 1
                        }
                    else
                        requestPayload.Resources
            }


        let taskAuthentication =
            requestPayload.Tasks
            |> Array.filter(fun t -> not <| isNull (box t.AuthenticationMethod))
            |> Array.map (fun t -> t.AuthenticationMethod)

        if requestPayload.Tasks.Length > requestPayload.Resources.MemoryGBs * 10 then
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

        requestPayload.Tasks
        |> Array.iter(fun t ->
            let location = t.SwaggerLocation
            if not <| String.IsNullOrWhiteSpace(location.URL) then
                match Uri.TryCreate(location.URL, UriKind.Absolute) with
                | true, _ -> ()
                | false, _ -> raiseApiError({
                    Error = {
                        Code = ApiErrorCode.ParseError
                        Message = "Invalid Swagger Uri in a task. The Uri must be a valid, absolute Uri."
                        Target = "validateAndPatchPayload"
                        Details = Array.empty
                        InnerError = {Message = ""}
                    }
                })

            if not <| String.IsNullOrWhiteSpace location.URL &&
                not <| String.IsNullOrWhiteSpace location.FilePath then
                raiseApiError({
                    Error = {
                        Code = ApiErrorCode.ParseError
                        Message = "Only one Swagger Location value is allowed in tasks: Path or URL but not both"
                        Target = "validateAndPatchPayload"
                        Details = Array.empty
                        InnerError = {Message = ""}
                    }
                })
        )
        if not <| isNull requestPayload.ReadOnlyFileShareMounts then
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

        if not <| isNull requestPayload.ReadWriteFileShareMounts then
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

        if not <| isNull(box requestPayload.Webhook) then
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

        // Validate the swagger URL.
        if not <| String.IsNullOrWhiteSpace(requestPayload.SwaggerLocation.URL) then
            match Uri.TryCreate(requestPayload.SwaggerLocation.URL, UriKind.Absolute) with
            | true, _ -> ()
            | false, _ -> raiseApiError({
                Error = {
                    Code = ApiErrorCode.ParseError
                    Message = "Invalid Swagger Uri. The Uri must be a valid, absolute Uri."
                    Target = "validateAndPatchPayload"
                    Details = Array.empty
                    InnerError = {Message = ""}
                }
            })

        if not <| String.IsNullOrWhiteSpace(requestPayload.SwaggerLocation.URL) &&
           not <| String.IsNullOrWhiteSpace(requestPayload.SwaggerLocation.FilePath) then
            raiseApiError({
                Error = {
                    Code = ApiErrorCode.ParseError
                    Message = "Only one Swagger Location value is allowed Path or URL but not both"
                    Target = "validateAndPatchPayload"
                    Details = Array.empty
                    InnerError = {Message = ""}
                }
            })

        requestPayload

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
            let stopWatch = System.Diagnostics.Stopwatch()
            do stopWatch.Start()

            let method = ModuleName + "Post"
            try
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
                return JsonResult(DTOs.CreateJobResponse(JobId = validatedBody.JobId))
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
                match! Utilities.raftStorage.TableEntryExists Raft.StorageEntities.JobTableName jobId jobId with
                | Result.Ok() -> ()
                | Result.Error err -> 
                    raiseApiError ({ 
                                    Error =
                                        { 
                                            Code = ApiErrorCode.NotFound
                                            Message = "JobId was not found. The jobId must already exist and have been created with the IsIdling flag set to true."
                                            Target = method
                                            Details = Array.empty
                                            InnerError = {Message = sprintf "Job table search for %O failed with HttpStatus %d" jobId err}
                                        }
                                    })


                let createQueue = Raft.Message.ServiceBus.Queue.create
                let validatedPayload = validateAndPatchPayload body

                let repost : DTOs.CreateJobRequest = {JobId =  jobId; JobDefinition = validatedPayload; IsIdlingRun = true; Region = null}

                do! sendCommand createQueue repost.JobId {Message = repost; MessagePostCount = 0}

                stopWatch.Stop()
                Central.Telemetry.TrackMetric (TelemetryValues.ApiRequest(method, float stopWatch.ElapsedMilliseconds), "milliseconds", this :> ControllerBase)
                return JsonResult(DTOs.CreateJobResponse(JobId = repost.JobId))
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
                    let message = DTOs.DeleteJobRequest(JobId = jobId)

                    do! sendCommand Raft.Message.ServiceBus.Queue.delete jobId {  Message = message; MessagePostCount = 0 }

                    stopWatch.Stop()
                    Central.Telemetry.TrackMetric (TelemetryValues.ApiRequest(method, float stopWatch.ElapsedMilliseconds), "milliseconds", this :> ControllerBase)
                    return JsonResult(DTOs.CreateJobResponse (JobId = jobId) )
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

            let query = TableQuery<JobStatusEntity>().Where(TableQuery.GenerateFilterCondition(Constants.PartitionKey, QueryComparisons.Equal, jobId))
            let! results = Utilities.raftStorage.GetJobStatusEntities query
            if results |> Seq.tryFind (fun (r:JobStatusEntity) -> r.PartitionKey = r.RowKey) = None then
                printfn "Results sequence is discarded since overall job status is not yet set after executing table query for job: %A" jobId
                return setApiErrorStatusCode { Error = { Code = ApiErrorCode.NotFound
                                                         Message = sprintf "Job status for job id %O not found." jobId
                                                         Target = method
                                                         Details = [||]
                                                         InnerError = {Message = ""}
                                                }} Microsoft.AspNetCore.Http.StatusCodes.Status404NotFound
            else
                let decodedMessages = 
                    results
                    |> Seq.map (fun jobStatusEntity -> Raft.Message.RaftEvent.deserializeEvent jobStatusEntity.JobStatus)
                    |> Seq.map (fun jobStatus -> DTOs.JobStatus.FromInternal jobStatus.Message)

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
                                                |> Seq.map(fun (s: JobStatusEntity) -> Raft.Message.RaftEvent.deserializeEvent s.JobStatus)
                                                |> Seq.map (fun jobStatus -> DTOs.JobStatus.FromInternal jobStatus.Message)

            stopWatch.Stop()
            Central.Telemetry.TrackMetric (TelemetryValues.ApiRequest(method, float stopWatch.ElapsedMilliseconds), "milliseconds", this :> ControllerBase)
            return JsonResult(statuses)
        }

      
