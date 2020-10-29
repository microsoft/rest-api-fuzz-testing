# RAFT Command-Line Interface (CLI) Reference

The `raft` command line utility, written in Python, wraps each of the commands
in the service's [REST API](sdk/swagger.md).  Any functionality provided by the
CLI can also be performed programmatically or using an HTTP client like
[PostMan](https://www.postman.com/) or [curl](https://linuxhandbook.com/curl-command-examples/).

The Raft CLI is both a command line interface which parses commands and executes them,
and an SDK which you can import and script the underlying functions yourself.

In our CLI syntax, values without two dashes "--" are positional parameters.
When they are separated by "|" as in [`red`|`blue`|`yellow`], please select one value.

<br/>

#### Aliasing

If you plan to execute RAFT commands from the command line, we recommend that you
take advantage of [aliasing](https://en.wikipedia.org/wiki/Alias_%28command%29) to
make things easier.

For Linux, you may create an alias with the following command.  Note that this assumes
you've cloned the RAFT repo to `/home/git/rest-api-fuzz-testing` on your local machine.
Please adjust this command as necessary:

```bash
$ alias raft='python /home/git/rest-api-fuzz-testing/cli/raft.py --defaults-context-path /home/git/rest-api-fuzz-testing/cli/defaults.json $*'
```

For PowerShell, you may create an alias with the following command.  Note that this assumes
you've cloned the RAFT repo to `D:\git\rest-api-fuzz-testing` on your local machine.  Please
adjust this command as necessary:

```powershell
> function InvokeRaftCLI { python d:\git\rest-api-fuzz-testing\cli\raft.py --defaults-context-path d:\git\rest-api-fuzz-testing\cli\defaults.json $args}
> set-alias raft InvokeRaftCLI
```

<br/>

#### Authentication

There are two ways to authenticate to a RAFT service:  interactively or by call.

For interactive authentication, all you need to do is execute a command from an
unauthenticated state; this will cause the system to prompt you to authenticate.
All subsequent commands are authenticated until you call the `logout` command.

For unattended scripting scenarios, you must include an API key in each call via
the `-secret` argument.  This can be obtained either by using the Azure Portal or the `az` cli. 

In the portal, navigate to your deployment's App Registration under the Azure Active 
Directory resource. The app registration will be named **[deploymentName]-raft**. 
Using the Certificates & secrets tab, create a new client secret.

To create a secret using the `az` cli, see the documentation for [az ad sp credential](https://docs.microsoft.com/en-us/cli/azure/ad/sp/credential?view=azure-cli-latest#az_ad_sp_credential_reset)

##### Usage

```
$ raft  [-secret SECRET_VALUE]
        [--skip-sp-deployment]
        command subcommand ...
```

##### Authentication arguments

- `-secret SECRET_VALUE`    Used for scripting, where each individual call must be authenticated.

##### Optional arguments

- `--skip-sp-deployment`    is used, new secret generation is not executed.
                                However, the deployment will overwrite configuration settings for the APIService and the Orchestrator.
                                These services need to know the service principal secret.
                                Use this parameter to pass the secret to the deployment process.

##### Available commands

      cli logout          Removes authentication token

      job create          Creates a new job
      job status          Returns the status of the given job
      job list            Returns the status of all jobs started in the last _n_ hours
      job update          Deploys a job definition to an existing, idling job
      job delete          Deletes a running job
      job results         Returns a url to where the job's results are stored
      
      service deploy      Creates an instance of the RAFT service in Azure
      service restart     Stops and starts the RAFT API service and orchestrator
      service info        Returns the instance version and uptime

      webhook events      Returns the set of events for which a webhook may be created
      webhook create      Creates a webhook to response to a particular event
      webhook test        Issues a test call to a webhook receiver
      webhook list        Returns the definition of a given webhook
      webhook delete      Deletes a webhook for a specific event

<br/>
<br/>

## cli logout

The `cli logout` command removes the cashed authentication token, requiring you to
re-authenticate prior to calling the next command.

##### Usage

```bash
$ raft cli logout
```

##### Example

```bash
$ raft cli logout
```

<br/>

## job create

The `job create` creates a job based on the definition provided in the job definition JSON file

##### Usage

```bash
$ raft job create
           --file JOB_DEFINITION_FILEPATH
           [--duration TIMESPAN]
           [--poll POLL_INTERVAL]
           [--metadata KEY_VALUE_PAIRS]
           [--substitute SUBSTITUTION_JSON]
```

##### Required arguments

- `--file JOB_DEFINITION_FILEPATH` File path to the job definition JSON file

##### Optional arguments

- `--duration TIME_SPAN` specifies the job's maximum runtime using a [TimeSpan](https://docs.microsoft.com/en-us/dotnet/standard/base-types/standard-timespan-format-strings) string, and overrides the value from the job definition file, if present
- `--poll POLL_INTERVAL` takes a value which is used as the job status polling interval in seconds.  Polling terminates once the job has completed.
- `--metadata KEY_VALUE_PAIRS` Arbitrary key/value pairs that are passed to a job and are appended to each webhook's metadata.  This is typically used to track things like branch names, change authors, bug numbers, etc., in your jobs.  So for example, if you've configured a logic app to create work items on each `BugFound` webhook, this data could be included in the bug.
- `--substitute SUBSTITUTION_JSON` Indicates the new job definition is based on an existing job but with some changes; takes a JSON dictionary of values to change and must be used with `--file`

##### Returns

- The `jobId` GUID in JSON format; if you've defined the `namePrefix` field in the job definition, the GUID will be prepended with that value

##### Examples

```bash
$ raft job create --file c:\jobs\jobDefinition.json --duration "1.06:15:00"
{"jobId": "0a0fd91f-8592-4c9d-97dd-01c9c3c44159"}

$ raft job create --file c:\jobs\newJobDefinition.json --file c:\jobs\existingJobDefinition.json --substitute '{"find1":"replace1", "find2":"replace2"}'
{"jobId": "924fda6b-3e48-43d7-81c5-9540c0a502a2"}

$ raft job create --file /home/jobs/jobDefinition.json
           --metadata '{"BuildNumber":"1234", "Author":"John"}'
           --poll 300
{"jobId": "1f5f2d69-cb1f-4d11-8327-ff6676a1891d"}
```

<br/>

## job status

The `job status` command returns the status of the given job.

##### Usage

```bash
$ raft job status
           --job-id JOB_ID
```

##### Required arguments

- `--job-id JOB_ID` Identifier of the job in question

##### Example

```bash
$ raft job status --job-id ffc4a296-f85d-4122-b49b-8074b88c9755
[
    {
        "tool": "RESTler",
        "jobId": "9ef309ab-df0a-4a98-a930-be6df04da47e",
        "state": "Completed",
        "metrics": {
            "totalRequestCount": 905,
            "responseCodeCounts": {
                "200": 99,
                "400": 683,
                "404": 82,
                "500": 41
            },
            "totalBugBucketsCount": 8
        },
        "utcEventTime": "2020-10-12T17:18:09.2568235Z",
        "details": [],
        "agentName": "0-restler-fuzz-1"
    },
    {
        "tool": "RESTler",
        "jobId": "9ef309ab-df0a-4a98-a930-be6df04da47e",
        "state": "Completed",
        "metrics": {
            "totalRequestCount": 899,
            "responseCodeCounts": {
                "200": 79,
                "400": 674,
                "404": 106,
                "500": 40
            },
            "totalBugBucketsCount": 8
        },
        "utcEventTime": "2020-10-12T17:18:13.9189848Z",
        "details": [],
        "agentName": "1-restler-3"
    },
    {
        "tool": "",
        "jobId": "9ef309ab-df0a-4a98-a930-be6df04da47e",
        "state": "Completed",
        "utcEventTime": "2020-10-12T17:19:00.5900998Z",
        "details": [
            "CPU Average 19.409091",
            "Network total bytes received: 944423",
            "Network total bytes sent: 728473"
        ],
        "agentName": "9ef309ab-df0a-4a98-a930-be6df04da47e"
    }
]
```

<br/>

## job list

The `job list` command returns the status of all jobs created in the last 24 hours
or other optional interval

##### Usage

```bash
$ raft job list
           [--look-back-hours INTERVAL]
```

##### Optional arguments

- `--look-back-hours INTERVAL` filters the jobs to those created in the last `INTERVAL` hours

##### Example

```bash
$ raft job list --look-back-hours 8
[
    {
        "tool": "",
        "jobId": "02c7b598c-bce2-4a19-ab2d-5ee7fc7a0028",
        "state": "Error",
        "utcEventTime": "2020-10-12T16:59:52.504944Z",
        "details": [
            "Failed to get configuration for unsupported tool: \"fuzzstring\""
        ],
        "agentName": "02c7b598c-bce2-4a19-ab2d-5ee7fc7a0028"
    },
    {
        "tool": "",
        "jobId": "039b42d54-84a4-4413-9ab0-cd1351284d37",
        "state": "Error",
        "utcEventTime": "2020-10-12T17:11:19.0013263Z",
        "details": [
            "Failed to get configuration for unsupported tool: \"fuzzstring\""
        ],
        "agentName": "039b42d54-84a4-4413-9ab0-cd1351284d37"
    },
    {
        "tool": "",
        "jobId": "06bdc1817-4512-47d5-8fce-348ff4ffcb06",
        "state": "Error",
        "utcEventTime": "2020-10-12T17:09:14.0202546Z",
        "details": [
            "Failed to get configuration for unsupported tool: \"fuzzstring\""
        ],
        "agentName": "06bdc1817-4512-47d5-8fce-348ff4ffcb06"
    },
    {
        "tool": "",
        "jobId": "091379c3e-d146-4f5d-9357-32b566af03f3",
        "state": "Error",
        "utcEventTime": "2020-10-12T16:52:30.9707085Z",
        "details": [
            "Failed to get configuration for unsupported tool: \"fuzzstring\""
        ],
        "agentName": "091379c3e-d146-4f5d-9357-32b566af03f3"
    },
    {
        "tool": "RESTler",
        "jobId": "61a50b73-02cf-4b07-9208-fab9dc7f08ad",
        "state": "Completed",
        "metrics": {
            "totalRequestCount": 900,
            "responseCodeCounts": {
                "200": 65,
                "400": 690,
                "404": 105,
                "500": 40
            },
            "totalBugBucketsCount": 8
        },
        "utcEventTime": "2020-10-12T17:06:37.9648198Z",
        "details": [],
        "agentName": "0-restler-test-fuzz-lean-1"
    },
    {
        "tool": "RESTler",
        "jobId": "61a50b73-02cf-4b07-9208-fab9dc7f08ad",
        "state": "Completed",
        "metrics": {
            "totalRequestCount": 16,
            "responseCodeCounts": {
                "200": 7,
                "400": 1,
                "404": 4,
                "500": 4
            },
            "totalBugBucketsCount": 2
        },
        "utcEventTime": "2020-10-12T16:56:07.621813Z",
        "details": [],
        "agentName": "1-restler-test-2"
    },
    {
        "tool": "",
        "jobId": "61a50b73-02cf-4b07-9208-fab9dc7f08ad",
        "state": "Completed",
        "utcEventTime": "2020-10-12T17:07:00.7120105Z",
        "details": [
            "CPU Average 16.989583",
            "Network total bytes received: 603156",
            "Network total bytes sent: 410004"
        ],
        "agentName": "61a50b73-02cf-4b07-9208-fab9dc7f08ad"
    },
    {
        "tool": "RESTler",
        "jobId": "9ef309ab-df0a-4a98-a930-be6df04da47e",
        "state": "Completed",
        "metrics": {
            "totalRequestCount": 905,
            "responseCodeCounts": {
                "200": 99,
                "400": 683,
                "404": 82,
                "500": 41
            },
            "totalBugBucketsCount": 8
        },
        "utcEventTime": "2020-10-12T17:18:09.2568235Z",
        "details": [],
        "agentName": "0-restler-fuzz-1"
    },
    {
        "tool": "RESTler",
        "jobId": "9ef309ab-df0a-4a98-a930-be6df04da47e",
        "state": "Completed",
        "metrics": {
            "totalRequestCount": 899,
            "responseCodeCounts": {
                "200": 79,
                "400": 674,
                "404": 106,
                "500": 40
            },
            "totalBugBucketsCount": 8
        },
        "utcEventTime": "2020-10-12T17:18:13.9189848Z",
        "details": [],
        "agentName": "1-restler-3"
    }
]
```

<br/>

## job update

The `job update` command deploys a job definition to an existing job that
was created with the `isIdling` flag set to true, which tells the service
to not delete the container when the job has completed.  This lets you
quickly deploy a new job without waiting for container creation.

> [!NOTE]
> It is also possible to use `ssh` to log into the container if manual exploration of the container is needed.
> If the container is not running for some reason, the job will be created as normal.
> If the job container creation failed for some reason, the job will not be created. You can check the application insights log for failures.

##### Usage

```bash
$ raft job update
           --job-id JOB_ID 
           --file JOB_DEFINITION_PATH
           [--substitute SUBSTITUTION_JSON]
```

##### Required arguments

- `--job-id JOB_ID` Identifier of the job in question
- `--file JOB_DEFINITION_PATH` File path to the job definition JSON file

##### Optional arguments

- `--substitute SUBSTITUTION_JSON` Indicates the new job definition is based on an existing job but with some changes; takes a JSON dictionary of values to change

##### Example

```bash
$ raft job update --job-id 63d35d0d-37c7-4612-8f3d-4c0d726cf923 --file c:\raft\job\Definition.json
```

<br/>

## job delete

The `job delete` command deletes the indicated job. By default jobs are garbage collected
when they have completed their run.  However, if `isIdling` is set to true, manual job
deletion is required.

##### Usage

```bash
$ raft job delete
           --job-id JOB_ID
```

##### Required arguments

- `--job-id JOB_ID` Identifier of the job in question

##### Example

```bash
$ raft job delete --job-id 76d90e69-86ec-4f86-8870-f3d733f833e0
```

<br/>

## job results

The `job results` command returns the path to the storage account where the job results
are stored.  You may browse to this URL to see results using a browser.

##### Usage

```bash
$ raft job results
           --job-id JOB_ID
```

##### Required arguments

- `--job-id JOB_ID` Identifier of the job in question

##### Returns

The URL of the file share containing job results

##### Example

```bash
$ raft job results --job-id e6291123-8fca-4222-8a56-657a84482467
https://ms.portal.azure.com/#blade/Microsoft_Azure_FileStorage/FileShareMenuBlade/overview/storageAccountId/...
```

<br/>

## service deploy

The `service deploy` command creates an instance of the RAFT service in Azure

> [!NOTE]
> The first time you use this command, RAFT will create an empty Defaults.json file
> in the CLI directory on your local machine.  You will be prompted to provide the
> required parameters (your Azure subscription, deployment name, and region) as well
> as other optional details (e.g., telemetry opt-in/out, AppInsights config, etc.).
> Subsequent deployments will reuse the Defaults.json you created initially.
 
##### Usage

```javascript
$ raft service deploy
               [--sku SKU]
               [--skip-sp-deployment]
               [--secret SECRET]
```

##### Optional arguments

- `--sku [B1|B2|B3|D1|F1|I1|I2|I3|P1V2|P2V2|P3V2|PC2|PC3|PC4|S1|S2|S3]` 
indicates the App Service Plan size; the default is `B2`.  Note that these are Linux plans.
- `--skip-sp-deployment` suppresses the service principal deployment when using the Azure DevOps pipeline to re-deploy the service during code development.  Note that in this scenario, use of the `--secret` argument is required.
- `--secret SECRET` This is a secret that is generated as described in the Authentication section above. 

##### Examples

```javascript
$ raft service deploy --sku SHARED

$ raft service deploy --skip-sp-deployment --secret PYqL447A7j.VYURqIrvIE2ur_ITI984r
```

<br/>

## service restart

The `service restart` command restarts the API service and the orchestrator. If there
is a new version of RAFT available, it will be automatically be downloaded and installed.

##### Usage

```javascript
$ raft service restart
```

##### Returns

Progress of the restart or any errors that occur

##### Example

```javascript
$ raft service restart
Restarting test-raft-orchestrator
Restarting test-raft-apiservice
Waiting for service to start
Done
```

<br/>

## service info

The `service info` command returns the instance version and the last time it was restarted.

##### Usage

```javascript
$ raft service info
```

##### Returns

The instance version and last restart UTC timestamp in JSON format

##### Example

```javascript
$ raft service info
{'version': '1.0.0.0', 'serviceStartTime': '2020-08-04T21:05:53+00:00'}
```

<br/>

## webhook events

The `webhook events` command returns the set of events which will generate webhooks.

##### Usage

```bash
$ raft webhook events
```

##### Example

```bash
$ raft webhook events
BugFound
JobStatus
```

<br/>

## webhook create

The `webhook create` command creates a webhook, which are implemented in Azure as
[Event Domains](https://docs.microsoft.com/en-us/azure/event-grid/event-domains).
They are commonly used in conjunction with an orchestration service like
[Azure Logic Apps](https://azure.microsoft.com/en-us/services/logic-apps/), which
allows you to create event-driven business processes, such as notifying a Teams or
Slack channel that a job has begun, creating new work items in Azure DevOps or Jira
for each new bug found, and so on.

> [!NOTE]
> You may add one or more events into the same domain topic.

##### Usage

```bash
$ raft webhook create
               --name DOMAIN_TOPIC
               --event WEBHOOK_EVENT
               --url WEBHOOK_URL
```

##### Required arguments

- `--name DOMAIN_TOPIC`  The string that identifies the event set to which this event is added; used in the job description JSON file to designate which sets of events are enabled ("Domain Topic" is the Azure term in the Event Grid domain.)
- `--event WEBHOOK_EVENT`  One of the events returned by the `webhook events` command that can be included in a domain topic
- `--url WEBHOOK_URL`  The URL to receive the webhook when executed; Note that the receiver must implement [endpoint validation](https://docs.microsoft.com/en-us/azure/event-grid/webhook-event-delivery)

##### Returns

The webhook's domain topic, the event that was added to it, and the URL associated with that event. in JSON format.

##### Example

```bash
$ raft webhook create --name MyWebhook --event BugFound --url https://mysite.com/webhook-listen
{​​​​​​"webhookName": "MyWebhook", "event": "BugFound", "targetUrl": "https://mysite.com/webhook-listen"}​​​​​​

$ raft webhook create --name MyWebhook --event JobStatus --url https://mysite.com/webhook-listen
{​​​​​​"webhookName": "MyWebhook", "event": "JobStatus", "targetUrl": "https://mysite.com/webhook-listen"}​​​​​​
```

<br/>

## webhook test

The `webhook test` command fires the indicated webhook with random data intended
to test a webhook receiver

##### Usage

```javascript
$ raft webhook test
               --name DOMAIN_TOPIC
               --event WEBHOOK_EVENT
```

##### Required arguments

- `--name DOMAIN_TOPIC` The string that identifies the event set to which this event is added; used in the job description JSON file to designate which sets of events are enabled ("Domain Topic" is the Azure term in the Event Grid domain.)
- `--event WEBHOOK_EVENT` Which event in the domain topic you want to fire

##### Returns

Success or error message in JSON format

##### Example

```javascript
$ raft webhook test --name MyWebhook --event BugFound
{"event": "JobStatus", "status": "Sent"}
```

<br/>

## webhook list

The `webhook list` command returns the set of registered events matching the search criteria

##### Usage

```bash
$ raft webhook list
               --name DOMAIN_TOPIC
               [--event WEBHOOK_EVENT]
```

##### Required argument

- `--name DOMAIN_TOPIC`     The string that identifies the event set to which this event is added; used in the job description JSON file to designate which sets of events are enabled ("Domain Topic" is the Azure term in the Event Grid domain.)

##### Optional argument

- `--event WEBHOOK_EVENT`  Filter the returned list to events of this type

##### Returns

List of registered events that match the search criteria in JSON format, if any

##### Example

```bash
$ raft webhook list --name MyWebhook --event BugFound
[{​​​​"webhookName": "MyWebhook", "event": "JobStatus", "targetUrl": "https://prod-00.westus2.logic.azure.com/workflows/7d74395df5314fbfb9a64da946418c67/triggers/manual/paths/invoke"}​​​​​]

$ raft webhook list --name NoSuchName
[]
```

<br/>

## webhook delete

The `webhook delete` command removes the given event from the indicated domain topic

> [!NOTE]
> To remove the domain topic itself, just remove each of its registered events

##### Usage

```bash
$ raft webhook delete
               --name DOMAIN_TOPIC
               --event WEBHOOK_EVENT
```

##### Required arguments

- `--name DOMAIN_TOPIC`     The string that identifies the event set to which this event is added; used in the job description JSON file to designate which sets of events are enabled ("Domain Topic" is the Azure term in the Event Grid domain.)
- `--event WEBHOOK_EVENT`   The particular event to remove from the domain topic 

##### Example

```bash
$ raft webhook delete --name MyWebhook --event BugFound
```