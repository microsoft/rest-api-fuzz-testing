import json
import os
import subprocess
import sys
import time
import io
import importlib
import shutil
import glob
import requests

from urllib.parse import urlparse
from contextlib import redirect_stdout

class RaftJsonDict(dict):
    def __init__(self):
        super(RaftJsonDict, self).__init__()

    def __getitem__(self, key):
        for k in self.keys():
            if k.lower() == key.lower():
                key = k
                break
        return super(RaftJsonDict, self).__getitem__(key)

    def pop(self, key):
        for k in self.keys():
            if k.lower() == key.lower():
                key = k
                break
        return super(RaftJsonDict, self).pop(key)

    def get(self, key):
        for k in self.keys():
            if k.lower() == key.lower():
                key = k
                break
        return super(RaftJsonDict, self).get(key)

    @staticmethod
    def raft_json_object_hook(x):
        r = RaftJsonDict()
        for k in x:
            r[k] = x[k]
        return r

def install_certificates():
    work_directory = os.environ['RAFT_WORK_DIRECTORY']
    run_directory = os.environ['RAFT_TOOL_RUN_DIRECTORY']
    with open(os.path.join(work_directory, "task-config.json"), 'r') as task_config:
        config = json.load(task_config, object_hook=RaftJsonDict.raft_json_object_hook)
        if config.get("targetConfiguration") and config.get("targetConfiguration").get("certificates"):
            certificates = config["targetConfiguration"]["certificates"]
            files = glob.iglob(os.path.join(certificates, "*.crt"))
            for f in files:
                if os.path.isfile(f):
                    print(f"Copying file {f}")
                    shutil.copy(f, "/usr/local/share/ca-certificates/")
            subprocess.check_call(["update-ca-certificates", "--fresh"])


def auth_token():
    auth_url = os.environ['RAFT_AGENT_UTILITIES_URL'] 
    work_directory = os.environ['RAFT_WORK_DIRECTORY']
    run_directory = os.environ['RAFT_TOOL_RUN_DIRECTORY']
    with open(os.path.join(work_directory, "task-config.json"), 'r') as task_config:
        config = json.load(task_config, object_hook=RaftJsonDict.raft_json_object_hook)
        auth_config = config.get("authenticationMethod")
        if auth_config:
            auth_type, auth_key = auth_config.popitem()
            response = requests.get(auth_url + '/auth' + '/' + auth_type + '/' + auth_key)
            if response.ok:
                content = json.loads(response.text)
                return content['token']
            else:
                raise Exception(response.text)
        else:
            return None


def task_config():
    work_directory = os.environ['RAFT_WORK_DIRECTORY']
    with open(os.path.join(work_directory, 'task-config.json'), 'r') as task_config:
        return json.load(task_config, object_hook=RaftJsonDict.raft_json_object_hook)

class RaftUtils():
    def __init__(self, tool_name):
        self.config = task_config()
        self.report_status_url = os.environ['RAFT_AGENT_UTILITIES_URL']
        self.job_id = os.environ['RAFT_JOB_ID']
        self.container_name = os.environ['RAFT_CONTAINER_NAME']
        self.tool_name = tool_name

        self.telemetry_properties = {
            "jobId" : self.job_id,
            "taskIndex" : os.environ['RAFT_TASK_INDEX'],
            "containerName" : self.container_name
        }

    def wait_for_agent_utilities(self):
        try:
            r = requests.get(f'{self.report_status_url}/readiness/ready')
            if r.ok:
                return
            else:
                time.sleep(1)
                self.wait_for_agent_utilities()
        except:
            time.sleep(10)
            return self.wait_for_agent_utilities()

    def report_bug(self, bugDetails):
        m = {
            'tool' : self.tool_name,
            'jobId' : self.job_id,
            'agentName' : self.container_name,
            'bugDetails' : bugDetails
        }
        requests.post(f'{self.report_status_url}/messaging/event/bugFound', json=m)

    def report_status(self, state, details):
        m = {
            'tool' : self.tool_name,
            'jobId' : self.job_id,
            'agentName': self.container_name,
            'details': details,
            'utcEventTime' : time.strftime('%Y-%m-%d %H:%M:%S', time.gmtime()),
            'state' : state
        }
        requests.post(f'{self.report_status_url}/messaging/event/jobStatus', json=m)

    def report_status_created(self, details=None):
        self.report_status('Created', details)

    def report_status_running(self, details=None):
        self.report_status('Running', details)

    def report_status_error(self, details=None):
        self.report_status('Error', details)

    def report_status_completed(self, details=None):
        self.report_status('Completed', details)

    def log_trace(self, trace):
        t = {
            'message' : trace,
            'severity' : 'Information',
            'tags' : self.telemetry_properties
        }
        requests.post(f'{self.report_status_url}/messaging/trace', json=t)

    def log_exception(self, ex):
        t = {
            'message' : f'{ex}',
            'severity' : 'Error',
            'tags' : self.telemetry_properties
        }
        requests.post(f'{self.report_status_url}/messaging/trace', json=t)

    def flush(self):
        requests.post(f'{self.report_status_url}/messaging/flush')
