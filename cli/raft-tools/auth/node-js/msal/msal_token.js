'use strict';
var msal = require('@azure/msal-node');

function get_token(client_id, tenant_id, secret, scopes, authority_uri, callback, audience) {
    let authority;
    if (authority_uri) {
        authority = authority_uri + '/' + tenant_id;
    }
    else {
        authority = "https://login.microsoftonline.com/" + tenant_id;
    }

    if (!scopes) {
        if (!audience) {
            scopes = [client_id + "/.default"];
        }
        else {
            scopes = [audience + "/.default"];
        }
    }

    const msalConfig = {
        auth: {
            clientId: client_id,
            authority : authority,
            clientSecret:secret
        } , 
        system: {
            loggerOptions: {
                loggerCallback(loglevel, message, containsPii) {
                    console.log(message);
                },
                piiLoggingEnabled: false,
                //logLevel: msal.LogLevel.Verbose,
                logLevel: msal.LogLevel.Warning
            }
        }
    };

    const accessTokenRequest = {
        scopes: scopes
    }
    const msalInstance = new msal.ConfidentialClientApplication(msalConfig);

    msalInstance.acquireTokenByClientCredential(accessTokenRequest).then(function(accessTokenResponse) {
        callback(null, accessTokenResponse.tokenType + ' ' + accessTokenResponse.accessToken);
    }).catch(function (error) {
        console.error(error);
        callback(error);
    })
}
exports.tokenFromEnvVariable = function (env_variable_name, callback) {
    let auth = JSON.parse(process.env["RAFT_" + env_variable_name] || process.env[env_variable_name]);
    if (auth) {
        console.log("Getting MSAL token");
        get_token(auth['client'], auth['tenant'], auth['secret'], auth['scopes'], auth['authorityUri'], callback, auth['audience']);
    }
    else {
        callback(new Error("Authentication parameters are not set in environment variable " + env_variable_name));
    }
}