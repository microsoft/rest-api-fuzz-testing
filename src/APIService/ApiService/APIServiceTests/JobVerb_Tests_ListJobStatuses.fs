// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace JobControllerTests

open Xunit
open System.IO
open Raft.StorageEntities
open System
open Raft.TestFixtures
open Raft.Controllers

[<CollectionAttribute("unit")>]
type ListJobStatusesTests() =

    [<Fact>]
    member this.``LIST /jobs/restler succeeds with zero status objects`` () =
        async {
            Raft.Utilities.raftStorage <- Fixtures.createFakeRaftStorage None

            let jobController = jobsController(Fixtures.createFakeTelemetryClient, Fixtures.createFakeLogger<jobsController>)
            jobController.ControllerContext.HttpContext <- Fixtures.createFakeContext()

            let! results = jobController.List Unchecked.defaultof<_> |> Async.AwaitTask
            let seqResults = results.Value :?> seq<DTOs.JobStatus>
            Assert.True(Seq.isEmpty seqResults)
        }

    [<Fact>]
    member this.``LIST /jobs/restler succeeds`` () =
        async {
            let jobStatusJson = File.ReadAllText("job-status.json")
            let entity = JobStatusEntity(Guid.Parse("29211868-8178-4e81-9b8d-d52025b4c2d4").ToString(), "testAgent", jobStatusJson)        
            Raft.Utilities.raftStorage <- Fixtures.createFakeRaftStorage (Some entity)

            let jobController = jobsController(Fixtures.createFakeTelemetryClient, Fixtures.createFakeLogger<jobsController>)
            jobController.ControllerContext.HttpContext <- Fixtures.createFakeContext()

            let! results = jobController.List (Nullable<TimeSpan>(TimeSpan.FromHours(4.0))) |> Async.AwaitTask
            let seqResults = results.Value :?> seq<DTOs.JobStatus>
            Assert.True(Seq.length seqResults = 1)
        }

