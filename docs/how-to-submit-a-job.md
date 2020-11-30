# How To Submit A Job, Step by Step

This page describes how to prepare, submit, observe, and get results from a
REST API Fuzz Testing (RAFT) job.

A RAFT **job** is the execution of one or more security tools against one or
more targets.  While most docker packaged security tools can be run in RAFT
against most targets, 
the service is designed to test REST APIs, typically from within a
CI/CD pipeline.  Jobs run in container groups, and execute either until the tool
completes or until the configurable duration has expired.

The following tutorial walks you through the exact process needed to submit a job.
This assumes that you've correctly [deployed](how-to-deploy.md) the RAFT service
to an Azure subscription.

<br/>

## Step One: Pick the Tool and Target

RAFT was built to streamline the execution of security tools against web sites and services.
RAFT comes with two registered tools:

- [RESTler](https://github.com/microsoft/restler-fuzzer), a stateful REST API fuzzer from [NSV at Microsoft Research](https://www.microsoft.com/en-us/research/group/new-security-ventures/)
- [ZAP](https://www.zaproxy.org/), a web scanner from the [OWASP Foundation](https://owasp.org/) that is
configured to test REST API's.

To register additional tools, please see please see the
[Onboarding a Tool](how-to-onboard-a-tool.md) page.

<br/>

## Step Two: Choose the Job Configuration Options

The **job definition** is the set of all configuration options that fully defines
how a job runs.   Initially, you'll create the job definition as a JSON file on your
local computer; the RAFT CLI will then submit its content as the body of the REST API
call to the `POST /jobs` endpoint that creates a new job in your RAFT service instance.

But before you can create this file, you'll need to choose which of the configuration
options are relevant to you, and values you'll configure for the options you've chosen.

We'll provide a quick summary here, but you should take the time to browse the
[job definition](schema/jobdefinition.md) schema to see the full set of options.

At a high level, a valid job definition must include the following:

- From one to 60 tasks; a **task** is the execution of a single docker container
- Either a global swagger file or a swagger file for each tool that requires one
- Either a global host definition or a host for each tool that requires one
- Either a global duration or a duration for each tool that requires one
- Either a global output folder or a folder for each tool
- Any other required runtime settings for each tool. Tool runtime settings are tool
specific and defined by the tool.

#### Is your web site authenticated?

It is very probable that the REST API or web site you want to test with RAFT will
require some sort of authentication.  

There are a number of authentication options available to you.

- **MSAL** which uses the MSAL library to authenticate. This method requires a tenantId, clientId, and secret.
- **CommandLine** This method defines a command line that executes and acquires an authorization token.
- **TxtToken** This method accepts a key vault secret name that contains a plain text token.

See the page on [authentication](schema/authentication.md) for details on how these authentication options
are configured and how to use them.

<br/>

## Step Three: Create a Job Definition File

Now that you've settled on the options you'll set and their respective values, it's time
to create the job definition JSON file you'll use to submit the job and put RAFT to work.

All Job Definition JSON files must define at least 1 entry in the `tasks` array.
The `swaggerLocations`, `host`, and `duration` fields can be defined globally
or locally, depending on whether you want these values to apply to all tasks or
to individual tasks.

Following is an example of a simple job definition file that provides a global
swagger location and host settings, along with two tasks:

```json
{
  "swaggerLocations": [{
    "URL" : "https://{sample.host}/swagger/v1/swagger.json"
  }],
  "host": "{sample.host}",
  "tasks": [
    {
      "toolName": "MyFirstTool",
      "outputFolder" : "my-tool-output-folder"
    },
    {
      "toolName": "MySecondTool",
      "duration": "02:10:00",
      "outputFolder" : "my-tool-output-folder"
    }
  ]
}
```

If this job definition file were submitted to RAFT, it would cause the following to
occur: 

- A container group would be created and be named using the JOBID

- A container associated with "MyFirstTool" would be created, taking the `swaggerLocations` and `host`
  parameters from the global position, and `toolName` and `outputFolder` parameters
  from the task definition.  Since there is no `duration` field, it will run until it
  completes. 

- A container named "MySecondTool" would be created, taking the `swaggerLocations` and `host`
  parameters from the global position, and `toolName`, `duration`, and `outputFolder`
  parameters from the task definition.  It will run until it completes, or until 2 hours 
  ten minutes have passed, whichever occurs first.

Note that containers (tasks) are launched in parallel within the container group,
and that the overall job is considered complete once all tasks complete or fail.

For more information on:

- how a job executes, please see the [How It Works](how-it-works.md) page
- how to adjust how each tool runs in its container, please see the [Onboarding a Tool](how-to-onboard-a-tool.md) page
- a variety of different ways to use RAFT, please see our [Samples](samples.md) page for a variety of job definion files

<br/>

#### Debug Execution Mode

By default, a given job proceeds as we've described: a container launches for each
defined task with the appropriate parameters, and run until the contained tool exits
or the duration expires.

Sometimes you'll want to have more control over the container execution, for debugging
a faulty job or observing the execution of a [new tool](how-to-onboard-a-tool.md), for
example.

If you set the `isIdling` job parameter to true, then the following occurs instead:

- container will launch
- container will execute "idle" command defined in config.json for the tool
- optionally, you may execute `python raft.py job update --file PATH_TO_JOB_DEFINITION_FILE 
--job-id JOBID` to launch tasks in that container. This will cause the "run" command in the tool definition's
1config.json` file to execute. You can update the job as often as you want while you are debugging.
- at some point, you must manually delete the container

This bears repeating:  if `isIdling` is set to true, the container is not automatically deleted after
its task completes.

<br/>

## Step Four:  Submit the job

Fire up the [RAFT CLI](cli-reference.md) and ensure it's configured to point to a valid service instance.

```python
$ python raft.py job create --file PATH_TO_JOB_DEFINITION_FILE
```

<br/>

## Step Five: Get the job results

The job results are normally written to a file share. You can monitor the results while the tool is running
or wait until the job has completed.
To configure this, find the tool's `config.json` file in the `cli/raft-tools/tools`

To see the state of the job use the command:

```python
python raft.py job status --job-id JOBID 
```

When asking for job status, use the JOBID that was returned when you created the job. 

To get the URL to the job results file share use the command:

```python
python raft.py job results --job-id JOBID
```

The URL that is returned can be used in your browser to access the file share where the results have
been written.

