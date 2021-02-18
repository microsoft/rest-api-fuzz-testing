# How to deploy a RAFT instance, Step by Step

The following guide should get you up and running with an instance of RAFT.

There are two main ways you can approach setting up RAFT.
- Download all requirements to your workstation and then use the RAFT CLI in a command window.
- Use the Azure Portal Cloud Shell. This requires no changes to your workstation.

<br/>

## Step 1: Install the RAFT Command Line Interface

Let's start out by getting the RAFT command line interface (CLI from now on)
up and running.   It functions just the same on Windows and Linux clients.

### If you are using your workstation

- [Install Python](https://www.python.org/downloads/) if
you don't have it installed already; RAFT requires at least **version 3.6**.
- [Install the Azure CLI](https://docs.microsoft.com/en-us/cli/azure/install-azure-cli)
if you haven't already; RAFT requires at least **version 2.15**.

### If you are using the Cloud Shell

If you are going to use the Cloud Shell it is assumed that you have already acquired an Azure subscription
from https://azure.com/free or you have an existing subscription.

Access the [Cloud Shell](https://docs.microsoft.com/en-us/azure/cloud-shell/overview) from your 
browser by clicking on the Cloud Shell icon.</br>
![](images/cloud-shell-icon.jpg)

Or access it directly from your browser at https://shell.azure.com.
When using the shell for the first time it will create a storage account. This is normal and is needed to 
persist data from one session to another.

In the cloud shell the path to Python version 3.6 `/opt/az/bin/python3`

### Common install instructions

You will need the RAFT CLI files. You can do this in a number of ways:
- Download the RAFT CLI from a specific release</br>
  For example:</br>
    `wget https://github.com/microsoft/rest-api-fuzz-testing/releases/download/LATEST_VERSION_OF_CLI/raft-cli.zip`
    where LATEST_VERSION_OF_CLI is the latest released version of CLI
  </br>
  Then run unzip `unzip cli.zip`
- Clone the repo
- Copy the sources


Once you have the python CLI files, you will need to install a few dependencies using Python's
[pip package installer](https://pypi.org/project/pip/) from the root
of the RAFT CLI folder.

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

The RAFT CLI is now functional.

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

You will need an Azure subscription to host the RAFT service.  If you
don't already have access to an Azure subscription, please follow
[these instructions](https://docs.microsoft.com/en-us/dynamics-nav/how-to--sign-up-for-a-microsoft-azure-subscription)
to sign up for one, or sign up for a free subscription at https://azure.com/free.

You must be an [owner](https://docs.microsoft.com/en-us/azure/role-based-access-control/built-in-roles)
on the subscription to deploy RAFT, though once it's deployed you only need
[contributor](https://docs.microsoft.com/en-us/azure/role-based-access-control/built-in-roles) rights to use it.

RAFT uses [container instances](https://azure.microsoft.com/en-us/services/container-instances/)
to host running jobs; by default, Azure subscriptions are limited to 100 container instances.  If your
subscription is already using container instances, or you anticipate running
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
| `metricsOptIn`* | Yes | Whether you want the service to send us anonymized usage data; we use this to improve the service and respond to errors and other problems proactively (Note: to change you choice, just update the field and redeploy) Default: true|
| `isDevelop` | No | Is this deployment for developing the RAFT service? Default: false |
| `isPrivateRegistry` | No | When developing for the RAFT service, indicates a private registry is used to find images. Default: false |
| `useAppInsights` | Yes | Create AppInsights resource as part of your deployment and use it to write all service logs. Default: true |
| `registry` | Yes | Registry which stores service images. Default: mcr.microsoft.com |

### *Telemetry
*By default, we collect anonymous usage data from your RAFT instance, which helps
us understand how users use RAFT and the problems they experience, which in turn,
helps us improve the quality of the offering over time.  Specifically, We do **not**
collect any data about the targets and results of tools you might run.  The data
fields we collect are defined in the `src/Contracts/Telemetry.fs` source file.   To opt-out of
sending this data to Microsoft, simply set the `metricsOptIn` field in the `defaults.json`
file set to false.  You may also manually opt out by clearing the value from the setting
`RAFT_METRICS_APP_INSIGHTS_KEY` in the apiservice and the orchestrator function app.
(Do not delete the setting; simply clear the value.)

#### RESTler telemetry
It is also easy to opt-out of sending anonymous usage telemetry metrics for RESTler. 
Under the cli/raft-tools/tools/RESTler folder edit the **config.json** file.
Set the RESTLER_TELEMETRY_OPTOUT environment variable to "1".

	"environmentVariables" : {
		"RESTLER_TELEMETRY_OPTOUT" : "1"	
	}

If you make these changes after the very first time you deploy, you will need to re-run the command

```python
    python raft.py service upload-tools
```

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
    See the documentation on container instance region availability at
    https://docs.microsoft.com/en-us/azure/container-instances/container-instances-region-availability
    to pick the optimal region for your deployment.
    All jobs will be deployed by default in the same
    region as your service deployment

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

Using a text editor of your choice, update the `defaults.json` file with
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

Three tools are deployed by default: [RESTler](https://github.com/microsoft/restler), [ZAP](https://www.zaproxy.org/) and [Dredd](https://github.com/apiaryio/dredd).

You can see how they are configured by looking at the configuration files under the `cli/raft-tools/tools` folder.

See an explanation of the `config.json` file in [How to onboard a tool](how-to-onboard-a-tool.md).
