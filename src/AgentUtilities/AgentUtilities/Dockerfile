#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:5.0 AS base
WORKDIR /app
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:5.0 AS build
WORKDIR /src
COPY ["AgentUtilities/AgentUtilities.fsproj", "AgentUtilities/"]
RUN dotnet restore "AgentUtilities/AgentUtilities.fsproj"
COPY . .
WORKDIR "/src/AgentUtilities"
RUN dotnet build "AgentUtilities.fsproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "AgentUtilities.fsproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "AgentUtilities.dll"]