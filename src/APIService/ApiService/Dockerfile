FROM mcr.microsoft.com/dotnet/runtime-deps:5.0-alpine
COPY . /raft/apiservice
RUN ls -al /raft/apiservice
RUN chmod +x /raft/apiservice/apiservice
CMD ["/raft/apiservice/apiservice"]