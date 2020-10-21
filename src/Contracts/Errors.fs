// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Raft

open System.Runtime.Serialization
open System.Net

module Errors =

    // The api guidelines https://github.com/Microsoft/api-guidelines/blob/vNext/Guidelines.md
    // recommends that a standard structure be used for returning errors. These types define that
    // standard structure.
    // The structure will be deserialized into json and returned in the error response body.

    // These error codes must remain stable. Adding/Subtracting forces a new version of the api.
    [<DataContract>]
    type ApiErrorCode =
        | [<EnumMember>] InvalidTag = 0
        | [<EnumMember>] QueryStringMissing = 1
        | [<EnumMember>] QueryStringInvalid = 2
        | [<EnumMember>] MissingHeader = 3
        | [<EnumMember>] InternalError = 4
        | [<EnumMember>] InvalidJob = 5
        | [<EnumMember>] NoJobsFound = 6
        | [<EnumMember>] ParseError = 7
        | [<EnumMember>] NotFound = 8


    /// Inner error can be used for cascading exception handlers or where there are field validations
    /// and multiple errors need to be returned.
    [<DataContract>]
    type InnerError =
        {
            /// Inner detailed message
            [<DataMember>]
            Message : string
       }

    [<DataContract>]
    type ApiErrors =
        {
            /// The main error encountered
            [<DataMember>]
            Code : ApiErrorCode

            /// A detail string that can be used for debugging
            [<DataMember>]
            Message : string

            /// Function name that generated the error
            [<DataMember(IsRequired = false)>]
            Target : string

            /// An array of details about specific errors that led to this reported error.
            [<DataMember(IsRequired = false)>]
            Details : ApiErrors[]

            /// An object containing more specific information than the current object about the error.
            [<DataMember(IsRequired = false)>]
            InnerError : InnerError

        }

    /// The guidelines specify that the top level structure has only this one member.
    [<DataContract>]
    type ApiError =
        {
            /// Main field for errors.
            [<DataMember>]
            Error : ApiErrors
        }

    /// Catch ApiErrors from the request
    exception ApiErrorException of ApiError with
        override this.Message =
            sprintf "%A" this

    let raiseApiError (apiError:ApiError) =
        ApiErrorException(apiError)
        |> raise
