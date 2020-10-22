// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
namespace InfoControllerTests

open Xunit
open FsCheck.Xunit
open Raft.Controllers
open Microsoft.AspNetCore.Mvc
open Raft.TestFixtures


module InfoControllerTests =
    [<Property>]
    let ``GET / should returns the correct response`` () =
        
        let infoController = new infoController(Fixtures.createFakeConfiguration "")
        infoController.ControllerContext.HttpContext <- Fixtures.createFakeContext()
        let result = infoController.Get()
        Assert.IsType(EmptyResult().GetType(), result)

    [<Property>]
    let ``GET /info should returns the correct response`` (testDateTime  : System.DateTimeOffset) =
        let infoController = new infoController(Fixtures.createFakeConfiguration (testDateTime.ToString()))
        infoController.ControllerContext.HttpContext <- Fixtures.createFakeContext()

        let result = (infoController.GetInfo().Value) :?> infoType
        
        (result.ServiceStartTime.ToString() = testDateTime.ToString())
