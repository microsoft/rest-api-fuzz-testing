// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace AgentUtilities.Controllers

open Microsoft.AspNetCore.Mvc
open Microsoft.Extensions.Logging

[<ApiController>]
[<Route("[controller]")>]
[<Produces("application/json")>]
type ReadinessController (logger : ILogger<ReadinessController>) =
    inherit ControllerBase()

    [<HttpGet("ready")>]
    member this.Get() =
        async {
            return this.Ok()
        } |> Async.StartAsTask
