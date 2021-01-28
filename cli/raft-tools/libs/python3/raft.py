import json
import os
import subprocess
import sys
import time
import io
import importlib
import shutil
import glob

from urllib.parse import urlparse
from contextlib import redirect_stdout

def install_certificates():
    work_directory = os.environ['RAFT_WORK_DIRECTORY']
    run_directory = os.environ['RAFT_TOOL_RUN_DIRECTORY']
    with open(os.path.join(work_directory, "task-config.json"), 'r') as task_config:
        config = json.load(task_config)
        if config.get("targetConfiguration") and config.get("targetConfiguration").get("certificates"):
            certificates = config["targetConfiguration"]["certificates"]
            files = glob.iglob(os.path.join(certificates, "*.crt"))
            for f in files:
                if os.path.isfile(f):
                    print(f"Copying file {f}")
                    shutil.copy(f, "/usr/local/share/ca-certificates/")
            subprocess.check_call(["update-ca-certificates", "--fresh"])


def auth_token(init):
    work_directory = os.environ['RAFT_WORK_DIRECTORY']
    run_directory = os.environ['RAFT_TOOL_RUN_DIRECTORY']
    with open(os.path.join(work_directory, "task-config.json"), 'r') as task_config:
        config = json.load(task_config)
        auth_config = config.get("authenticationMethod")
        if auth_config:
            if auth_config.get("txtToken"): 
                if init:
                    return None
                else:
                    token = os.environ.get(f"RAFT_{auth_config['txtToken']}") or os.environ.get(auth_config["txtToken"])
                    return token
            elif auth_config.get("commandLine"):
                if init:
                    return None
                else:
                    subprocess.getoutput(auth_config.get("commandLine"))
            elif auth_config.get("msal"):
                msal_dir = os.path.join(run_directory, "..", "..", "auth", "python3", "msal")

                if init:
                    print("Installing MSAL requirements")
                    subprocess.check_call([sys.executable, "-m", "pip", "install", "-r", os.path.join(msal_dir, "requirements.txt")])
                else:
                    print("Retrieving MSAL token")
                    sys.path.append(msal_dir)
                    authentication_environment_variable = auth_config["msal"]
                    import msal_token
                    token = msal_token.token_from_env_variable( authentication_environment_variable )
                    if token:
                        print("Retrieved MSAL token")
                        return token
                    else:
                        print("Failed to retrieve MSAL token")
                        return None
            else:
                print(f'Unhandled authentication configuration {auth_config}')
    return None

def task_config():
    work_directory = os.environ['RAFT_WORK_DIRECTORY']
    with open(os.path.join(work_directory, 'task-config.json'), 'r') as task_config:
        return json.load(task_config)

class RaftUtils():
    def __init__(self, tool_name):
        from applicationinsights import TelemetryClient
        from azure.servicebus import ServiceBusClient, ServiceBusMessage
        self.config = task_config()

        connection_str = os.environ['RAFT_SB_OUT_SAS']

        self.sb_client = ServiceBusClient.from_connection_string(connection_str)
        self.topic_client = self.sb_client.get_topic_sender(self.sb_client._entity_name)
        
        self.telemetry_client = TelemetryClient(instrumentation_key=os.environ['RAFT_APP_INSIGHTS_KEY'])

        self.job_id = os.environ['RAFT_JOB_ID']
        self.container_name = os.environ['RAFT_CONTAINER_NAME']
        self.tool_name = tool_name

        self.telemetry_properties = {
            "jobId" : self.job_id,
            "taskIndex" : os.environ['RAFT_TASK_INDEX'],
            "containerName" : self.container_name
        }

        self.newSbMessage = ServiceBusMessage

    def report_bug(self, bugDetails):
        m = {
            'eventType' : 'BugFound',
            'message' : {
                'tool' : self.tool_name,
                'jobId' : self.job_id,
                'agentName' : self.container_name,
                'bugDetails' : bugDetails
            }
        }
        msg = self.newSbMessage(str.encode(json.dumps(m)))
        self.topic_client.send_messages([msg])

    def report_status(self, state, details):
        m = {
            'eventType' : 'JobStatus',
            'message': {
                'tool' : self.tool_name,
                'jobId' : self.job_id,
                'agentName': self.container_name,
                'details': details,
                'utcEventTime' : time.strftime('%Y-%m-%d %H:%M:%S', time.gmtime()),
                'state' : state
            }
        }
        msg = self.newSbMessage(str.encode(json.dumps(m)))
        self.topic_client.send_messages([msg])

    def report_status_created(self, details=None):
        self.report_status('Created', details)

    def report_status_running(self, details=None):
        self.report_status('Running', details)

    def report_status_error(self, details=None):
        self.report_status('Error', details)

    def report_status_completed(self, details=None):
        self.report_status('Completed', details)

    def log_trace(self, trace):
        self.telemetry_client.track_trace(trace, properties=self.telemetry_properties)

    def log_exception(self):
        self.telemetry_client.track_exception(properties=self.telemetry_properties)

    def flush(self):
        self.telemetry_client.flush()
        self.sb_client.close()