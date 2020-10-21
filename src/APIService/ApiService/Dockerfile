FROM mcr.microsoft.com/dotnet/core/runtime-deps:3.1-alpine
COPY . /raft/apiservice
RUN ls -al /raft/apiservice
RUN chmod +x /raft/apiservice/apiservice
CMD ["/raft/apiservice/apiservice"]