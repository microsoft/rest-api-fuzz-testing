'use strict';

const fs = require('fs');
const path = require('path'); 
const url = require('url');
const { exec } = require('child_process');

const appInsights = require('applicationinsights');
const serviceBus = require('@azure/service-bus');

const toolDirectory = process.env.RAFT_TOOL_RUN_DIRECTORY;
const workDirectory = process.env.RAFT_WORK_DIRECTORY;

const rawdata = fs.readFileSync(workDirectory + '/task-config.json');
const config = JSON.parse(rawdata);

function jsonGet(jo, keys) {
    var d = jo;
    for (const k of keys) {
        d = d[Object.keys(d).find(key => k.toLowerCase() === key.toLowerCase())];
        if (d == null) {
            break;
        }
    }
    return d;
}

class RaftUtils {
    constructor(toolName) {

        this.running_local = process.env['RAFT_LOCAL'];

        if (this.running_local) {
            this.serviceBus = null;
            this.sbSender = null;
            this.telemetryClient = null;
        } else {
            this.telemetryClient = new appInsights.TelemetryClient(process.env['RAFT_APP_INSIGHTS_KEY']);
            this.serviceBus = new serviceBus.ServiceBusClient(process.env.RAFT_SB_OUT_SAS);
            this.sbSender = this.serviceBus.createSender(this.serviceBus._connectionContext.config.entityPath);
        }

        this.jobId = process.env['RAFT_JOB_ID'];
        this.agentName = process.env['RAFT_CONTAINER_NAME'];
        this.traceProperties =
            {
                jobId : this.jobId,
                taskIndex : process.env['RAFT_TAKS_INDEX'],
                containerName : this.agentName
            };
        this.toolName = toolName;
    }

    reportBug(bugDetails) {
        if (this.running_local) {
            return new Promise(function(resolve, reject) {resolve();});
        } else {
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
    }

    reportStatus(state, details) {
        if (this.running_local) {
            return new Promise(function(resolve, reject) {resolve();});
        } else {
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
        if (!this.running_local) {
            this.telemetryClient.trackTrace({message: traceMessage, properties: this.traceProperties});
        }
    }

    logException(exception) {
        if (!this.running_local) {
            this.telemetryClient.trackException({exception: exception, properties: this.traceProperties});
        }
    }

    flush(){
        if (!this.running_local) {
            this.telemetryClient.flush();
            this.serviceBus.close();
        }
    }
}
function getAuthHeader(callback) {
    const authenticationMethod = jsonGet(config, ['authenticationMethod']);
    if (!authenticationMethod)  {
        callback(null, null);
    } else {
        const authMethods = Object.keys(authenticationMethod);
        if (authMethods.length == 0) {
            callback(null, null); 
        } else if (authMethods.length > 1) {
            callback(new Error("More than one authentication method is specified: " + authenticationMethod), null);
        } else {
            const authMethod = authMethods[0];
            console.log("Authentication Method: " + authMethod);
            switch (authMethod.toLowerCase()) {
                case 'msal':
                    const msalDirectory = toolDirectory + "/../../auth/node-js/msal";
                    exec("npm install " + msalDirectory, (error, _) => {
                            if (error) {
                                callback(error);
                            } else {
                                const raftMsal = require(msalDirectory + '/msal_token.js')
                                raftMsal.tokenFromEnvVariable(authenticationMethod[authMethod], (error, result) => {
                                        callback(error, result);
                                    }
                                );
                            }
                        }
                    );
                    break;
                case 'txttoken':
                    callback(null, process.env['RAFT_' + authenticationMethod[authMethod]] || process.env[authenticationMethod[authMethod]] );
                    break;
                case 'commandline':
                    exec(authenticationMethod[authMethod], (error, result) => {
                            callback(error, result);
                        }
                    );
                    break;
                default:
                    callback(new Error("Unhandled authentication method: " + authenticationMethod), null);
                    break;
            }
        }
    }
}

function installCertificates(callback) {
    const certificates = jsonGet(config, ['targetConfiguration', 'certificates'])

    if (!certificates) {
        callback(null, null);
    } else {
        fs.readdir(certificates, function(err, files) {
            if (err) {
                callback(err, null);
            }
            else {
                files.forEach(function(file) {
                    console.log("File: " + file);
                    if (path.extname(file) === '.crt') {
                        const copySrc = certificates + "/" + file;
                        const copyDest = "/usr/local/share/ca-certificates/" + file;
                        console.log("CopySrc: " + copySrc + " CopyDest: " + copyDest);
                        fs.copyFileSync(copySrc, copyDest);
                    }
                });
                console.log("Updating certificates");
                exec("update-ca-certificates --fresh", (error, _) => {
                        if (error) {
                            console.log("Failed to update certificates: " + error);
                            callback(error, null);
                        } else {
                            callback(null, null);
                        }
                    }
                );
            }
        });
    }
}

exports.installCertificates = installCertificates;
exports.workDirectory = workDirectory;
exports.config = config;
exports.RaftUtils = RaftUtils;
exports.getAuthHeader = getAuthHeader;
exports.jsonGet = jsonGet;