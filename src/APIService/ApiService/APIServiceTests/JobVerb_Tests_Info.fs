// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
namespace InfoControllerTests

open Xunit
open Raft.Controllers
open Microsoft.AspNetCore.Mvc
open Raft.TestFixtures

[<CollectionAttribute("unit")>]
type infoControllerTests() =

    [<Fact>]
    member this.``GET / should returns the correct response`` () =
        
        let infoController = new infoController(Fixtures.createFakeConfiguration "")
        infoController.ControllerContext.HttpContext <- Fixtures.createFakeContext()
        let result = infoController.Get()
        Assert.IsType(EmptyResult().GetType(), result)

    [<Fact>]
    member this.``GET /info should returns the correct response`` () =
        let testDateTime = System.DateTimeOffset.Parse("7/27/2020 1:57:15 PM")

        let infoController = new infoController(Fixtures.createFakeConfiguration (testDateTime.ToString()))
        infoController.ControllerContext.HttpContext <- Fixtures.createFakeContext()

        let result = (infoController.GetInfo().Value) :?> infoType
        Assert.True(result.ServiceStartTime = testDateTime)
