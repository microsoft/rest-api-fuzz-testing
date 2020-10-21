// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Raft.Webhooks

open System
open System.Collections.Generic

    
type JobStateWebHook =
    {
        State : string
    }
type BugFoundWebHook = 
    {
        JobId : string
    }

// This is the type that is used to create webhooks and to report
// webhooks. 
type WebHook =
    {
        WebhookName : string
        Event : string
        TargetUrl : Uri
    }


type WebHookEnvelope<'T> =
    {
        Topic: string
        Id: string
        EventType: string
        Subject: string
        EventTime: string
        Data: 'T
        DataVersion : string
    }

type SentEvent =
    {
        Event : string
        Status : string
    }

    