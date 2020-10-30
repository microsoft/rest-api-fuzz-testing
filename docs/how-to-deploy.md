# How to deploy a RAFT instance, Step by Step

The following guide should get you up and running with an instance of RAFT.

<br/>

### The first option is to setup all the dependencies on your workstation and use the RAFT CLI from there. The second option is to use the Azure Portal Shell. When using the portal's shell, you will only need to upload the CLI package as all required dependencies are already installed.

## Step 1: Enable the RAFT Command Line Interface (CLI)

Let's start out by getting the RAFT command line interface (CLI from now on)
up and running.   It functions just the same on Windows and Linux clients.

These two steps are required if you've decided to run the CLI from your workstation:
- First, you'll need to [install Python](https://www.python.org/downloads/) if
you don't have it installed already; RAFT requires at least **version 3.6**.

- Next, you'll need to [install the Azure CLI](https://docs.microsoft.com/en-us/cli/azure/install-azure-cli)
if you haven't already; RAFT requires at least **version 2.12**.

If you've decided to use the Azure Portal Shell, keep in mind that the path to Python is `/opt/az/bin/python3`

- Now download the RAFT CLI, either just the binaries or the source tree if you intend to build them from source:

    - Get the RAFT CLI from [releases](https://github.com/microsoft/rest-api-fuzz-testing/releases)
    - Clone the repo at https://github.com/microsoft/raft

- At this point, you're able to run a the one-time prep script using Python's
[pip package installer](https://pypi.org/project/pip/) as follows from the root
of the RAFT CLI folder:

```javascript
$ pip install -r .\requirements.txt
```

If you have not included the Python scripts in your PATH on Windows, then you will
have to run this from your local Python folder (wherever it may be) and provide the
full path to the `requirements.txt` file:

```javascript
C:\Users\[user]\AppData\Local\Programs\Python\Python39\Scripts> pip.exe install -r d:\repo\raft\cli\requirements.txt
```

<br/>

- At this point, the RAFT CLI should be functional:

```javascript
D:\REPO\raft\cli>py raft.py --help
usage: raft.py [-h] [--defaults-context-path DEFAULTS_CONTEXT_PATH] [--defaults-context-json DEFAULTS_CONTEXT_JSON] [--secret SECRET] [--skip-sp-deployment] {cli,service,job,webhook} ...

RAFT CLI

positional arguments:
  {cli,service,job,webhook}

optional arguments:
  -h, --help            show this help message and exit
  --defaults-context-path DEFAULTS_CONTEXT_PATH
                        Path to the defaults.json
  --defaults-context-json DEFAULTS_CONTEXT_JSON
                        JSON blob containing service configuration
  --secret SECRET
  --skip-sp-deployment
```

<br/>

## Step 2: Azure Subscription Prep

First, you will need an Azure subscription to host the RAFT service.  If you
don't already have access to an Azure subscription, please follow
[these instructions](https://docs.microsoft.com/en-us/dynamics-nav/how-to--sign-up-for-a-microsoft-azure-subscription)
to sign up for one.

Second, you must be an [owner](https://docs.microsoft.com/en-us/azure/role-based-access-control/built-in-roles)
on the target subscription to deploy RAFT, though once it's deployed you only need
[contributor](https://docs.microsoft.com/en-us/azure/role-based-access-control/built-in-roles) rights to use it.

Last, RAFT uses [container instances](https://azure.microsoft.com/en-us/services/container-instances/)
to host running jobs; by default, Azure subscriptions are limited to 100.  If your
target subscription is already using container instances, or you anticipate running
more than 100 simultaneous jobs, you should reach out to Azure support and request
they increase your [quota](https://docs.microsoft.com/en-us/azure/azure-resource-manager/management/azure-subscription-service-limits)
of this object type.

<br/>

## Step 3: Choose Configuration Options

To deploy RAFT, you will need to settle on a few configuration options in advance.
Note that only four of these are required.

| Option | Required? | Description |
|--------|-------------|--------|
| `subscription` | Yes | The subscription ID (GUID) of the subscription to which to deploy an instance of the RAFT service |
| `deploymentName` | Yes | The name of your deployment; we will use this as the base name for all objects we create to instantiate the RAFT service |
| `region` | Yes | The [region identifier](https://azure.microsoft.com/en-us/global-infrastructure/geographies/) that is closest to your location, or that's necessary for any compliance purposes |
| `metricsOptIn`* | Yes | Whether you want the service to send us anonymized usage data; we use this to improve the service and respond to errors and other problems proactively (Note: to change you choice, just update the field and redeploy) |
| `isDevelop` | No | Is this deployment for developing the RAFT service?    Setting this value to true will generate yaml variables for use in your build pipelines |
| `useAppInsights` | No | deploy AppInsights and use it to write all service logs |
| `registry`** | No | registry which stores service images. Default: mcr.microsoft.com |

*By default, we collect anonymous usage data from your RAFT instance, which helps
us understand how users use RAFT and the problems they experience, which in turn
helps us improve the quality of the offering over time.  Specifically, We do **not**
collect any data about the targets and results of tools you might run.  The data
fields we collect are defined in the `src/Contracts/Telemetry.fs` source file.   To opt-out of
sending this data to Microsoft, simply set the `metricsOptIn` field in the `defaults.json`
file set to false.  You may also manually opt out by clearing the value from the setting
`RAFT_METRICS_APP_INSIGHTS_KEY` in the apiservice and the orchestrator function app.
(Do not delete the setting; simply clear the value.)
<br/>

## Step 4: Run the Deployment Script

The first time you execute the `py raft.py service deploy` command,  you'll see the following.

```javascript
D:\REPO\raft\cli>py raft.py service deploy

subscription - The Azure Subscription ID to which RAFT is deployed

deploymentName - RAFT deployment name
    deployment name requirements:
        - only letters or numbers
        - at most 20 characters long
        - no capital letters
        - no dashes

region - Region to deploy RAFT (e.g. westus2)
    See https://azure.microsoft.com/en-us/global-infrastructure/regions/
    for a list of regions

isDevelop - Is this deployment for developing the RAFT service?
    Setting this value to true will generate yaml variables for use in your
    build pipelines.

metricsOptIn - allow Microsoft collect anonymized metrics from the deployment.

useAppInsights - deploy AppInsights and use it to write all service logs

registry - registry which stores service images.
    Default: mcr.microsoft.com
-------------------------
To apply any changes made to the defaults.json file,
please run 'raft.py service deploy'
-------------------------
```

<br/>

Using a text editor of your choice, please update the `defaults.json` file with
the values you determined in Step 3, and then re-run:

```javascript
D:\REPO\raft\cli>py raft.py service deploy
```

Most deployments complete in about 15 minutes.

<br/>

## Step 5: Validate your RAFT Instance

Once your deployment has completed, you can check to see if the service is running by
executing the CLI command:

```javascript
D:\REPO\raft\cli>py raft.py service info
```

This will return information about the service including the version of the service
installed and the date and time the service was last started.

There are a number of samples in the sample folder. These are setup so that you can run
the python scripts directly to exercise the service. Take some time to look at the log files and
get familiar with the output.

Two tools are deployed by default: [RESTler](https://github.com/microsoft/restler) and [ZAP](https://www.zaproxy.org/).

You can see their configuration under the `cli/raft-tools/tools` folder.

See an explanation of the `config.json` file in [How a job executes](how-it-works/how-a-job-executes.md).