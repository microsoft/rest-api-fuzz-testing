FROM mcr.microsoft.com/dotnet/aspnet:5.0 AS base
WORKDIR /app

FROM base AS final

RUN apt-get update
RUN apt-get install apt-utils -y -q
RUN apt-get install python3 -y -q
RUN apt-get install python3-pip -y -q
RUN pip3 install msal==1.11.0
#RUN apt-get install npm -y -q
#RUN npm install @azure/msal-node@1.0.2
#RUN npm install msal@1.4.9

ENV ASPNETCORE_URLS=http://*:8085
COPY AgentUtilities /app
ENTRYPOINT ["dotnet", "/app/AgentUtilities.dll"]