'use strict';

const fs = require('fs');
const url = require('url');
const { exec } = require('child_process');

const appInsights = require('applicationinsights');
const serviceBus = require('@azure/service-bus');

const toolDirectory = process.env.RAFT_TOOL_RUN_DIRECTORY;
const workDirectory = process.env.RAFT_WORK_DIRECTORY;

const rawdata = fs.readFileSync(workDirectory + '/task-config.json');
const config = JSON.parse(rawdata);

class RaftUtils {
    constructor(toolName) {
        this.telemetryClient = new appInsights.TelemetryClient(process.env['RAFT_APP_INSIGHTS_KEY']);
        this.serviceBus = new serviceBus.ServiceBusClient(process.env.RAFT_SB_OUT_SAS);
        this.jobId = process.env['RAFT_JOB_ID'];
        this.agentName = process.env['RAFT_CONTAINER_NAME'];
        this.traceProperties =
            {
                jobId : this.jobId,
                taskIndex : process.env['RAFT_TAKS_INDEX'],
                containerName : this.agentName
            };
        this.sbSender = this.serviceBus.createSender(this.serviceBus._connectionContext.config.entityPath);
        this.toolName = toolName;
    }

    reportBug(bugDetails) {
        console.log('Sending bug found event: ' + bugDetails.name);
        this.sbSender.sendMessages(
            {
                body: {
                    eventType : 'BugFound',
                    message : {
                        tool : this.toolName,
                        jobId : this.jobId,
                        agentName : this.agentName,
                        bugDetails : bugDetails
                    }
                },
                sessionId : this.jobId 
            }
        );
    }

    reportStatus(state, details) {
        console.log('Sending job status event: ' + state);
        return this.sbSender.sendMessages(
            {
                body: {
                    eventType: 'JobStatus', 
                    message : {
                        tool: this.toolName,
                        jobId : this.jobId,
                        agentName : this.agentName,
                        details : details,
                        utcEventTime : (new Date()).toUTCString(),
                        state : state
                    }
                }, 
                sessionId : this.jobId 
            }); 
    }

    reportStatusCreated(details){
        this.reportStatus('Created', details);
    }

    reportStatusRunning(details){
        this.reportStatus('Running', details);
    }

    reportStatusCompleted(details){
        return this.reportStatus('Completed', details);
    }

    reportStatusError(details){
        return this.reportStatus('Error', details);
    }

    logTrace(traceMessage) {
        this.telemetryClient.trackTrace({message: traceMessage, properties: this.traceProperties});
    }

    logException(exception) {
        this.telemetryClient.trackException({exception: exception, properties: this.traceProperties});
    }

    flush(){
        this.telemetryClient.flush();
        this.serviceBus.close();
    }
}
function getAuthHeader(callback) {
    if (!config.authenticationMethod) {
        callback(null, null);
    } else {
        const authMethods = Object.keys(config.authenticationMethod)
        if (authMethods.length == 0) {
            callback(null, null); 
        } else if (authMethods.length > 1) {
            callback(new Error("More than one authentication method is specified: " + config.authenticationMethod), null);
        } else {
            const authMethod = authMethods[0];
            switch (authMethod.toLowerCase()) {
                case 'msal':
                    const msalDirectory = toolDirectory + "/../../auth/node-js/msal";
                    exec("npm install " + msalDirectory, (error, _) => {
                            if (error) {
                                callback(error);
                            } else {
                                const raftMsal = require(msalDirectory + '/msal_token.js')
                                raftMsal.tokenFromEnvVariable(config.authenticationMethod[authMethod], (error, result) => {
                                        callback(error, result);
                                    }
                                );
                            }
                        }
                    );
                    break;
                case 'txttoken':
                    callback(null, process.env['RAFT_' + config.authenticationmethod.txttoken] || process.env[config.authenticationmethod.txttoken] );
                    break;
                case 'commandline':
                    exec(config.authenticationmethod.commandline, (error, result) => {
                            callback(erorr, result);
                        }
                    );
                    break;
                default:
                    callback(new Error("Unhandled authentication method: " + config.authenticationMethod), null);
                    break;
            }
        }
    }
}

exports.workDirectory = workDirectory;
exports.config = config;
exports.RaftUtils = RaftUtils;
exports.getAuthHeader = getAuthHeader;