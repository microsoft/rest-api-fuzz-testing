# Installing RAFT

* Get the RAFT CLI from [releases.](https://github.com/microsoft/raft/releases)

**Prerequisites:**

* An azure subscription</br>
  It is helpful if you are the **owner** on the azure subscription that you are deploying to. 

  During deployment, the script will create a service principle, 
this service principle needs to be given access to the newly created resource group. 
You must have owner permissions for the script to do the assignment. 

  If you are not the subscription owner you will need to have the owner manually assign the service principle to the resource group that is created. 
The service principle should be given `contributor` access to the resource group that was created.

## Container Instances

RAFT uses container instances and by default subscriptions are limited to 100 instances,
if you are an existing user of container instances on the selected subscription,
you may need to reach out to Azure support to ask for an increase of Linux container instances.

## Installation Process

* Run the command `python .\raft.py service deploy`<br>
  The first time you run this command you will be asked to fill in some values in a `defaults.json` file. This file is used to describe the context for the cli.

```
subscription - The Azure Subscription ID to which RAFT is deployed

deploymentName - RAFT deployment name
    deployment name requirements:
        - only letters or numbers
        - at most 24 characters long
        - no capital letters
        - no dashes

region - Region to deploy RAFT (e.g. westus2)
    See https://azure.microsoft.com/en-us/global-infrastructure/regions/
    for a list of regions

metricsOptIn - allow Microsoft collect anonymized metrics from the deployment.

useAppInsights - deploy AppInsights and use it to write all service logs

registry - registry which stores service images.

-------------------------
To apply any changes made to the defaults.json file,
please run 'raft.py service deploy'
-------------------------

```

The only values you **MUST** change are:

* subscription - this is the azure subscription you own where you will be installing the service
* deploymentName - this is used to name a resource group and services with "-raft" appended
* region - the region to use for the services. We recommend a region that is close to your location. Find the list of available regions [here](https://azure.microsoft.com/en-us/global-infrastructure/geographies/).
* metricsOptIn - by default we collect anonymous metrics to help us improve the service.
  To opt-out simply set this value to false. If you change your mind at some point, update this value and re-deploy the service.
  
Once the default.json file has been updated, re-run `python .\raft.py service deploy`. Most deployments complete in about 15 minutes.
