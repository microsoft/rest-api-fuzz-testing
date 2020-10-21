# IMPORTANT NOTE: List of all function-docker images can be found here
# https://hub.docker.com/_/microsoft-azure-functions-dotnet
# See FULL TAG LISTING at the bottom of the page for list of available images and versions

FROM mcr.microsoft.com/azure-functions/dotnet:3.0

ENV AzureWebJobsScriptRoot=/home/site/wwwroot \
    AzureFunctionsJobHost__Logging__Console__IsEnabled=true 

COPY . /home/site/wwwroot
