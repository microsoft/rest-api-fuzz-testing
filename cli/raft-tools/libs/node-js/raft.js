'use strict';

const http = require('http');
const querystring = require('querystring');
const fs = require('fs');
const path = require('path'); 
const { exec } = require('child_process');

const workDirectory = process.env.RAFT_WORK_DIRECTORY;
const agentUtilitiesUrl = process.env.RAFT_AGENT_UTILITIES_URL;
console.log(`AgentUtilitiesURL: ${agentUtilitiesUrl}`);

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

const timer = ms => new Promise(res => setTimeout(res, ms));
function waitForEndpoint(url, callback) {
    http.get(url,
        function(res) {
            if (res.statusCode !== 200) {
                timer(1000).then( _ => waitForEndpoint(url, callback));
            } else {
                callback(null, null);
            }
        }
    ).on("error", function(err) {
            console.log(`Failed to establish connection to ${url} due to ${err}. Trying again...`);
            timer(1000).then( _ => waitForEndpoint(url, callback));
        }
    )
}



class RaftUtils {
    constructor(toolName) {
        this.jobId = process.env['RAFT_JOB_ID'];
        this.agentName = process.env['RAFT_CONTAINER_NAME'];
        this.traceProperties =
            {
                jobId : this.jobId,
                taskIndex : process.env['RAFT_TAKS_INDEX'],
                containerName : this.agentName
            };
        this.toolName = toolName;

        this.agentUtilitiesUrl = new URL(agentUtilitiesUrl);
    }

    post(path, data) {
        var postData = JSON.stringify(data);
        const opts = {
            host : this.agentUtilitiesUrl.hostname,
            port : this.agentUtilitiesUrl.port,
            path : path,
            method : 'POST',
            headers : {
                'Content-Type': 'application/json',
                'Content-Length' : Buffer.byteLength(postData)
            }
        };

        return new Promise(function(resolve, reject) {
                var post = http.request(opts, res => {
                    res.on('data', d => {
                        let rawData = '';
                        res.on('data', (chunk) => { rawData += chunk; });
                        res.on('end', () => {
                            try {
                                const parsedData = JSON.parse(rawData);
                                resolve(parsedData['token']);
                            } catch (e) {
                                reject(e);
                            }
                        }).on('error', (e) => {
                            reject(e);
                        });
                    });
                });

                post.on('error', e => {
                    console.error(`Posting ${postData} to ${path} failed with ${e}`);
                    reject(e);
                });

                post.write(postData);
                post.end();
            }
        );
    }

    waitForAgentUtilities(callback) {
        const readinessUrl = agentUtilitiesUrl + '/readiness/ready';
        return waitForEndpoint(readinessUrl, callback);
    }

    postEvent (eventName, eventData) {
        return this.post(('/messaging/event/' + eventName), eventData);
    }

    reportBug(bugDetails) {
        var bugData = {
            tool : this.toolName,
            jobId : this.jobId,
            agentName : this.agentName,
            bugDetails : bugDetails
        };
        return this.postEvent('bugFound', bugData);
    }

    reportStatus(state, details) {
        var jobStatusData = {
            tool: this.toolName,
            jobId : this.jobId,
            agentName : this.agentName,
            details : details,
            utcEventTime : (new Date()).toUTCString(),
            state : state
        };
        return this.postEvent('jobStatus', jobStatusData);
    }

    reportStatusCreated(details){
        return this.reportStatus('Created', details);
    }

    reportStatusRunning(details){
        return this.reportStatus('Running', details);
    }

    reportStatusCompleted(details){
        return this.reportStatus('Completed', details);
    }

    reportStatusError(details){
        return this.reportStatus('Error', details);
    }

    logTrace(traceMessage) {
        return this.post('/messaging/trace', {message: traceMessage, tags : this.traceProperties, severity : 'Information'});
    }

    logException(exception) {
        return this.post('/messaging/trace', {message: exception, tags : this.traceProperties, severity : 'Error'});
    }

    flush(){
        return this.post('/messaging/flush');
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
            const authUrl = agentUtilitiesUrl + `/auth/${authMethod}/${authenticationMethod[authMethod]}`
            http.get(authUrl,
                function(res) {
                    if (res.statusCode !== 200) {
                        callback(new Error(`Request to ${authUrl} failed with status code ${res.statusCode}`), null);
                    } else {
                        let rawData = '';
                        res.on('data', (chunk) => { rawData += chunk; });
                        res.on('end', () => {
                            try {
                                const parsedData = JSON.parse(rawData);
                                callback(null, parsedData['token']);
                            } catch (e) {
                                callback(e, null);
                            }
                        }).on('error', (e) => {
                            callback(e, null);
                        });
                    }
                }
            );
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