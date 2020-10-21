# IMPORTANT NOTE: List of all function-docker images can be found here
# https://hub.docker.com/_/microsoft-azure-functions-base
# https://mcr.microsoft.com/v2/azure-functions/base/tags/list

# To enable ssh & remote debugging on app service change the base image to the one below
# FROM mcr.microsoft.com/azure-functions/dotnet:3.0-appservice 
FROM mcr.microsoft.com/azure-functions/dotnet:3.0

ENV AzureWebJobsScriptRoot=/home/site/wwwroot \
    AzureFunctionsJobHost__Logging__Console__IsEnabled=true 

COPY . /home/site/wwwroot
