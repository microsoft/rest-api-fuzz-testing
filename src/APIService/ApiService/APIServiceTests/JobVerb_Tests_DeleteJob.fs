// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace JobControllerTests

open Xunit
open System.IO
open Raft.StorageEntities
open Raft.Controllers
open Raft.Errors
open Raft.TestFixtures

[<CollectionAttribute("unit")>]
type DeleteJobTests() =

    [<Fact>]
    member this.``DELETE /jobs/restler fails on missing jobId`` () =
        async {
            Raft.Utilities.raftStorage <- Fixtures.createFakeRaftStorage None

            let jobController = new jobsController(Fixtures.createFakeTelemetryClient, Fixtures.createFakeLogger<jobsController>)
            jobController.ControllerContext.HttpContext <- Fixtures.createFakeContext()

            let! failedResult = jobController.Delete("thisJobDoesNotExist") |> Async.AwaitTask 
            let errorResult = failedResult.Value :?> ApiError
            Assert.True(errorResult.Error.Code = ApiErrorCode.NotFound)
            Assert.True(errorResult.Error.Message.Contains("thisJobDoesNotExist"))
        }

    [<Fact>]
    member this.``DELETE /jobs/restler succeeds`` () =
        async {
            let fakeMessageSender = Fixtures.createFakeMessageSender Raft.Message.ServiceBus.Queue.delete

            let jobStatusJson = File.ReadAllText("job-status.json")
            let entity = JobStatusEntity(System.Guid.Parse("29211868-8178-4e81-9b8d-d52025b4c2d4").ToString(), "testAgent", jobStatusJson)

            Raft.Utilities.raftStorage <- Fixtures.createFakeRaftStorage (Some entity)

            let jobController = new jobsController(Fixtures.createFakeTelemetryClient, Fixtures.createFakeLogger<jobsController>)
            jobController.ControllerContext.HttpContext <- Fixtures.createFakeContext()

            let! deleteResult = jobController.Delete "29211868-8178-4e81-9b8d-d52025b4c2d4" |> Async.AwaitTask
            let result = deleteResult.Value :?> DTOs.JobResponse

            let parseResult, jobGuid = System.Guid.TryParse(result.JobId)
            Assert.True(parseResult)
            Assert.True(jobGuid.ToString() = "29211868-8178-4e81-9b8d-d52025b4c2d4" )
        }


