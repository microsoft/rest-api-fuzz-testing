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
    constructor() {
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
    }

    reportBug(bugDetails) {
        console.log('Sending bug found event: ' + bugDetails.name);
        this.sbSender.sendMessages(
            {
                body: {
                    eventType : 'BugFound',
                    message : {
                        tool : "Dredd",
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
                        tool: 'Dredd',
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

const raftUtils = new RaftUtils();
raftUtils.reportStatusCreated();

function getAuthHeader(callback) {
    if (!config.authenticationMethod) {
        raftUtils.logTrace("No authentication");
        callback(null, null);
    } else {
        const authMethods = Object.keys(config.authenticationMethod)
        if (authMethods.length == 0) {
            raftUtils.logTrace("No authentication");
            callback(null, null); 
        } else if (authMethods.length > 1) {
            callback(new Error("More than one authentication method is specified: " + config.authenticationMethod), null);
        } else {
            const authMethod = authMethods[0];
            switch (authMethod.toLowerCase()) {
                case 'msal':
                    raftUtils.logTrace("Authentication set to MSAL");
                    const msalDirectory = toolDirectory + "/../../auth/node-js/msal";
                    exec("npm install " + msalDirectory, (error, _) => {
                            if (error) {
                                callback(error);
                            } else {
                                const raft_msal = require(msalDirectory + '/msal_token.js')
                                raft_msal.tokenFromEnvVariable(config.authenticationMethod[authMethod], (error, result) => {
                                        callback(error, result);
                                    }
                                );
                            }
                        }
                    );
                    break;
                case 'txttoken':
                    raftUtils.logTrace("Authentication set to txtToken");
                    callback(null, process.env['RAFT_' + config.authenticationmethod.txttoken] || process.env[config.authenticationmethod.txttoken] );
                    break;
                case 'commandline':
                    raftUtils.logTrace("Authentication set to commandLine");
                    exec(config.authenticationmethod.commandline, (error, result) => {
                            callback(erorr, result);
                        }
                    );
                    break;
                default:
                    raftUtils.logTrace("Unhandled authentication method: " + config.authenticationMethod);
                    callback(new Error("Unhandled authentication method: " + config.authenticationMethod), null);
                    break;
            }
        }
    }
}

getAuthHeader((error, result) => {
    if (error) {
        console.error(error);
        raftUtils.logException(error);
        raftUtils.reportStatusError(error).then( () => {
            raftUtils.flush();
        });
    } else {

        let headers = [];
        if (result) {
            headers = ["Authorization: " + result];
        }

        var Dredd = require('dredd');
        config.targetConfiguration.apiSpecifications.forEach(swagger => {

            let host = config.targetConfiguration.host;
            if (config.targetConfiguration.port) {
                host = host + ":" + config.targetConfiguration.port;
            }
            const EventEmitter = require('events');
            let eventEmitter = new EventEmitter();

            let configuration = {
                init: false,
                blueprint: swagger,
                endpoint: host, // your URL to API endpoint the tests will run against
                path: [],         // Required Array if Strings; filepaths to API description documents, can use glob wildcards
                'dry-run': false, // Boolean, do not run any real HTTP transaction
                names: false,     // Boolean, Print Transaction names and finish, similar to dry-run
                loglevel: 'debug', // String, logging level (debug, warning, error, silent)
                only: [],         // Array of Strings, run only transaction that match these names
                header: headers,       // Array of Strings, these strings are then added as headers (key:value) to every transaction
                user: null,       // String, Basic Auth credentials in the form username:password
                hookfiles: [],    // Array of Strings, filepaths to files containing hooks (can use glob wildcards)
                reporter: ['markdown', 'html'], // Array of possible reporters, see folder lib/reporters
                output: [workDirectory + '/report.md', workDirectory + '/report.html'],       // Array of Strings, filepaths to files used for output of file-based reporters
                'inline-errors': false, // Boolean, If failures/errors are display immediately in Dredd run
                require: null,    // String, When using nodejs hooks, require the given module before executing hooks
                color: true,
                emitter: eventEmitter, // listen to test progress, your own instance of EventEmitter
                path:[swagger]
            }
            var dredd = new Dredd(configuration);

            //This is very ugly hack to address:
            //https://github.com/apiaryio/dredd/issues/1873
            dredd.prepareAPIdescriptions1 = dredd.prepareAPIdescriptions;
            dredd.prepareAPIdescriptions = function(callback) {
                dredd.prepareAPIdescriptions1((error, apiDescriptions) => {
                    if (apiDescriptions) {
                        apiDescriptions
                            .map((apiDescription) => apiDescription.annotations.map((annotation) => {
                                if (annotation.type === 'error') {
                                    let bugDetails = {...annotation.origin};
                                    bugDetails['name'] = annotation.name;
                                    bugDetails['message'] = annotation.message;
                                    raftUtils.reportBug(bugDetails);
                                    annotation.type = 'warning';
                                }
                                annotation;        
                            })
                        );
                    }
                    callback(error, apiDescriptions);
                });
            };

            eventEmitter.on('start', (e, callback) => {
                    e.map((event) => {
                        raftUtils.reportStatusRunning(
                            {
                                location : event.location,
                                numberOfRequests : event.transactions.length
                            }
                        );
                    })
                    callback();
                }
            );

            eventEmitter.on('end', (callback) => {
                    //raftUtils.reportStatusRunning(runDetails);
                    callback();
                }
            );

            eventEmitter.on('test pass', (test) => {
                //raftUtils.reportStatusRunning(runDetails);
            });

            eventEmitter.on('test fail', (test) => {
                let bugDetails = {...test.origin};
                bugDetails['name'] = test.title;
                bugDetails['message'] = test.message;
                raftUtils.reportBug(bugDetails);
                //raftUtils.reportStatusRunning(runDetails);
            });

            dredd.run(function (err, stats) {
                // err is present if anything went wrong
                // otherwise stats is an object with useful statistics
                if (err) {
                    console.error(err);
                    raftUtils.logException(err);
                    raftUtils.reportStatusError(err).then( () => {
                        raftUtils.flush();
                    });
                } else {
                    console.log(stats);
                    raftUtils.reportStatusCompleted(stats).then( () => {
                        raftUtils.flush();
                    });
                }
            }); 
        });
    }
})