# Telemetry

We collect anonymous telemetry metrics to help us improve the service. You can find all the data types that are collected in the
`telemetry.fs` file. 

## Opting out

To opt-out the easiest way is to run the deployment with the metricsOptIn field in the defaults.json file set to false. 

You can also manually opt out by clearing the value from the setting `RAFT_METRICS_APP_INSIGHTS_KEY` in the apiservice and the orchestrator function app.
Do not delete the setting, simply clear the value.