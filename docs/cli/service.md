# Service Commands:

## service [deploy | restart | info] 

The **deploy** parameter has the following options

**--sku option**</br>

* The allowed sku values are: 'B1', 'B2', 'B3', 'D1', 'F1', 'FREE','I1', 'I2', 'I3', 'P1V2','P2V2','P3V2','PC2', 'PC3', 'PC4', 'S1', 'S2', 'S3', 'SHARED'
* These correspond to the App Service Plan sizes. The default is B2. Note that is a linux app service plan.
  
**--skip-sp-deployment**</br>
* When using the Azure DevOps pipeline to re-deploy the service during code development,
this parameter can be used to skip the service principal deployment.
The assumption here is that the service principal has already been deployed.
In this scenario, use of the --secret parameter is required.

The **restart** parameter will restart the api service and the orchestrator. </br>
When the services restart if there is a new version of the software it will be downloaded.
  
The **info** parameter will return version information about the service and the last time it was restarted.</br>
Example: `{'version': '1.0.0.0', 'serviceStartTime': '2020-08-04T21:05:53+00:00'}`

