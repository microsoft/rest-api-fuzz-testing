# Azure Resources
Raft is a service that is built on [Azure](https://azure.microsoft.com/en-us/). All you need to get started is an Azure [subscription](https://azure.microsoft.com/en-us/free/).

The deployment script will create all the resources that you need for the service.  Use the [Azure portal](https://portal.azure.com) to view the created resources.

The resources that are used by the service are:
* Application Insights (for logging)
* App Service (for the front-end API)
* App Service Plan (VM's used for App Service and Azure functions)
* Event Grid Domain (for webhooks)
* Key vault (for secret management)
* Funtion app (for service orchestration)
* Service Bus (for messaging between components of the service)
* Storage accounts (one for the service use and one for all your data)