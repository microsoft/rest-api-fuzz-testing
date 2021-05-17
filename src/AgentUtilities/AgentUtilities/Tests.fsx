#r "System.Net.Http"

let url = "https://localhost:49166"

let httpClient = new System.Net.Http.HttpClient(BaseAddress = System.Uri(url))

let post (path: string) (json: string) =
    let content = new System.Net.Http.StringContent(json)
    content.Headers.ContentType <- System.Net.Http.Headers.MediaTypeHeaderValue.Parse("application/json")
    httpClient.PostAsync(path, content) |> Async.AwaitTask |> Async.RunSynchronously


post "/messaging/flush" ""


post "/messaging/trackTrace" """{"message" : "TestMessage", "severity" : "Information", "tags" : {"tag1":"abc", "tag2" : "def"}}"""

post "/messaging/event/jobStatus" """{"Tool" : "Test", "JobId" : "123", "AgentName" : "test", "State" : "Running", "ResultsUrl" : "http://www.blah.com", "UtcEventTime" : "4/22/2021 7:57:01 PM"}"""

