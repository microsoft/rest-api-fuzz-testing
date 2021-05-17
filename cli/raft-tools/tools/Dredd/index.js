'use strict';

const toolDirectory = process.env.RAFT_TOOL_RUN_DIRECTORY;
console.log(toolDirectory + "/../../libs/node-js/raft.js");

const raft = require(toolDirectory + "/../../libs/node-js/raft.js");
console.log(raft);

const raftUtils = new raft.RaftUtils("Dredd");

raftUtils.waitForAgentUtilities(_ => {
    console.log("Agent utilities are ready. Proceeding with the job run...");

    raftUtils.reportStatusCreated();

    raft.installCertificates((error, result) => {
        if (error) {
            console.error(error);
            raftUtils.logException(error);
            raftUtils.reportStatusError(error).then( () => {
                raftUtils.flush();
            });
        } else {
            raft.getAuthHeader((error, result) => {
                if (error) {
                    console.error(error);
                    raftUtils.logException(error);
                    raftUtils.reportStatusError(error).then( () => {
                        raftUtils.flush();
                    });
                } else {
                    var Dredd = require('dredd');
                    const EventEmitter = require('events');
                    let eventEmitter = new EventEmitter();

                    let headers = [];
                    if (result) {
                        headers = ["Authorization: " + result];
                    }

                    let configuration = {
                        init: false,
                        endpoint: raft.jsonGet(raft.config, ['targetConfiguration', 'endpoint']), // your URL to API endpoint the tests will run against
                        path: [],         // Required Array if Strings; filepaths to API description documents, can use glob wildcards
                        'dry-run': false, // Boolean, do not run any real HTTP transaction
                        names: false,     // Boolean, Print Transaction names and finish, similar to dry-run
                        loglevel: 'debug', // String, logging level (debug, warning, error, silent)
                        only: [],         // Array of Strings, run only transaction that match these names
                        header: headers,       // Array of Strings, these strings are then added as headers (key:value) to every transaction
                        user: null,       // String, Basic Auth credentials in the form username:password
                        hookfiles: [],    // Array of Strings, filepaths to files containing hooks (can use glob wildcards)
                        reporter: ['markdown', 'html'], // Array of possible reporters, see folder lib/reporters
                        output: [raft.workDirectory + '/report.md', raft.workDirectory + '/report.html'],       // Array of Strings, filepaths to files used for output of file-based reporters
                        'inline-errors': false, // Boolean, If failures/errors are display immediately in Dredd run
                        require: null,    // String, When using nodejs hooks, require the given module before executing hooks
                        color: true,
                        emitter: eventEmitter, // listen to test progress, your own instance of EventEmitter
                        path: raft.jsonGet(raft.config, ['targetConfiguration', 'apiSpecifications']),
                        sorted : false
                    }

                    const toolConfig = raft.jsonGet(raft.config, ['toolConfiguration']);
                    if (toolConfig) {
                        if (toolConfig.header) {
                            console.log("Adding extra headers to configuration");
                            configuration.header = configuration.header.concat(toolConfig.header);
                        }

                        if (toolConfig["dry-run"]) {
                            console.log("Dry-run is set"); 
                            configuration['dry-run'] = toolConfig["dry-run"];
                        }

                        if (toolConfig.only) {
                            console.log("Setting 'only' transaction names");
                            configuration.only = toolConfig.only;
                        }

                        if (toolConfig.hookfiles) {
                            console.log("Setting hook files");
                            configuration.hookfiles = toolConfig.hookfiles;
                        }

                        if (toolConfig.require) {
                            console.log("Setting node-js hooks require");
                            configuration.require = toolConfig.require;
                        }

                        if (toolConfig.sorted) {
                            console.log("Setting sorted configuration");
                            configuration.sorted = toolConfig.sorted;
                        }
                    }

                    console.log(raft.config);

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
                            callback();
                        }
                    );

                    eventEmitter.on('test pass', (test) => {
                    });

                    eventEmitter.on('test fail', (test) => {
                        let bugDetails = {...test.origin};
                        bugDetails['name'] = test.title;
                        bugDetails['message'] = test.message;
                        raftUtils.reportBug(bugDetails);
                    });

                    dredd.run(function (err, stats) {
                        // err is present if anything went wrong
                        // otherwise stats is an object with useful statistics
                        if (err) {
                            console.error(err);
                            raftUtils.logException(err);
                            raftUtils.reportStatusError(err.message).then( () => {
                                raftUtils.flush();
                            });
                        } else {
                            console.log(stats);
                            raftUtils.reportStatusCompleted(stats).then( () => {
                                raftUtils.flush();
                            });
                        }
                    }); 
                }
            })
        }
    })
})