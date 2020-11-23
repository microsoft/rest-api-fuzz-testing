// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace JobControllerTests

open Xunit
open System.IO
open Raft.StorageEntities
open System
open Raft.TestFixtures
open Raft.Controllers
open Raft.Errors

[<CollectionAttribute("unit")>]
type GetJobStatusTests() =


    [<Fact>]
    member this.``GET /jobs/restler fails on missing jobId`` () =
        async {
            Raft.Utilities.raftStorage <- Fixtures.createFakeRaftStorage None

            let jobController = jobsController(Fixtures.createFakeTelemetryClient, Fixtures.createFakeLogger<jobsController>)
            jobController.ControllerContext.HttpContext <- Fixtures.createFakeContext()

            let! failedResult = jobController.GetJobStatus "missingJobId" |> Async.AwaitTask
            let errorResult = failedResult.Value :?> ApiError
            Assert.True(errorResult.Error.Code = ApiErrorCode.NotFound)
            Assert.True(errorResult.Error.Message.Contains("missingJobId"))
        }

    [<Fact>]
    member this.``GET /jobs/restler succeeds`` () =
        async {
            let jobStatusJson = File.ReadAllText("test-job-status.json")
            let entity = JobStatusEntity(Guid.Parse("29211868-8178-4e81-9b8d-d52025b4c2d4").ToString(), "29211868-8178-4e81-9b8d-d52025b4c2d4", jobStatusJson, "Created")
            Raft.Utilities.raftStorage <- Fixtures.createFakeRaftStorage (Some entity)

            let jobController = jobsController(Fixtures.createFakeTelemetryClient, Fixtures.createFakeLogger<jobsController>)
            jobController.ControllerContext.HttpContext <- Fixtures.createFakeContext()

            let! result = jobController.GetJobStatus "29211868-8178-4e81-9b8d-d52025b4c2d4" |> Async.AwaitTask
            let jobStatusSeq = result.Value :?> seq<DTOs.JobStatus>
            let jobStatus = Seq.head jobStatusSeq
            Assert.True(jobStatus.AgentName = "1")
            Assert.True(jobStatus.Tool = "RESTler")
            Assert.True(jobStatus.State = DTOs.JobState.Created)
            let testTimeUtc = DateTime.Parse("2020-05-01T00:36:56.7525482Z").ToUniversalTime()
            Assert.True(DateTime.Compare(jobStatus.UtcEventTime, testTimeUtc) = 0)
        }


