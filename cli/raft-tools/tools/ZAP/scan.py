import os
import sys
import json
import shutil
import time
import io
import logging
from logging import StreamHandler
import importlib

from applicationinsights import TelemetryClient
from azure.servicebus import ServiceBusClient, ServiceBusMessage
from contextlib import redirect_stdout

class RaftUtils():
    def __init__(self):
        work_directory = os.environ['RAFT_WORK_DIRECTORY']
        with open(os.path.join(work_directory, 'task-config.json'), 'r') as task_config:
            self.config = json.load(task_config)

        connection_str = os.environ['RAFT_SB_OUT_SAS']

        self.sb_client = ServiceBusClient.from_connection_string(connection_str)
        self.topic_client = self.sb_client.get_topic_sender(self.sb_client._entity_name)
        
        self.telemetry_client = TelemetryClient(instrumentation_key=os.environ['RAFT_APP_INSIGHTS_KEY'])

        self.job_id = os.environ['RAFT_JOB_ID']
        self.container_name = os.environ['RAFT_CONTAINER_NAME']

        self.telemetry_properties = {
            "jobId" : self.job_id,
            "taskIndex" : os.environ['RAFT_TASK_INDEX'],
            "containerName" : self.container_name
        }

    def report_status(self, state, details):
        m = {
            'eventType' : 'JobStatus',
            'message': {
                'tool' : 'ZAP',
                'jobId' : self.job_id,
                'agentName': self.container_name,
                'details': details,
                'utcEventTime' : time.strftime('%Y-%m-%d %H:%M:%S', time.gmtime()),
                'state' : state
            }
        }
        msg = ServiceBusMessage(str.encode(json.dumps(m)))
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

zap_dir = '/zap'
sys.path.append(zap_dir)

utils = RaftUtils()

class StatusReporter(StreamHandler):
    def __init__(self, details):
        StreamHandler.__init__(self)
        self.last_txt = None
        self.details = details

    def emit(self, record):
        txt = self.format(record)
        if txt != self.last_txt:
            self.last_txt = txt
            progress='Active Scan progress %:'
            i = txt.find(progress)
            if i != -1:
                self.details["Scan progress"] = txt[i :]
                utils.report_status_running(self.details)

zap = __import__("zap-api-scan")

def run_zap(target_index, targets_total, target, token):
    if token:
        utils.log_trace('Authentication token is set')
        auth = ('-config replacer.full_list(0).description=auth1'
                ' -config replacer.full_list(0).enabled=true'
                ' -config replacer.full_list(0).matchtype=REQ_HEADER'
                ' -config replacer.full_list(0).matchstr=Authorization'
                ' -config replacer.full_list(0).regex=false'
                f' -config replacer.full_list(0).replacement="{token}"')
        zap_auth_config = ['-z', auth]
    else:
        utils.log_trace('Authentication token is not set')
        zap_auth_config = []

    os.chdir(zap_dir)
    r = 0
    try:
        print('Removing zap.out if exists')
        os.remove('/zap/zap.out')
    except:
        pass

    try:
        utils.log_trace("Starting ZAP")
        details = {"targetIndex": target_index, "numberOfTargets" : targets_total, "target": target}
        utils.report_status_running(details)
        status_reporter = StatusReporter(details)
        logger = logging.getLogger()
        logger.addHandler(status_reporter)
        zap.main(['-t', target, '-f', 'openapi', '-J', f'{target_index}-report.json', '-r', f'{target_index}-report.html', '-w', f'{target_index}-report.md', '-x', f'{target_index}-report.xml', '-d'] + zap_auth_config)
        details["Scan progress"] = "Active scan progress %: 100"
        utils.report_status_running(details)

    except SystemExit as e:
        r = e.code

    utils.log_trace(f"ZAP exited with exit code: {r}")
    shutil.copy('/zap/zap.out', f'/zap/wrk/{target_index}-zap.out')

    if r <= 2:
        r = 0
        
    if target_index + 1 == targets_total:
        utils.report_status_completed()
    utils.sb_client.close()
    return r

def run(target_index, targets_total, target, token):
    try:
        utils.report_status_created()
        return run_zap(target_index, targets_total, target, token)
    except Exception as ex:
        utils.log_exception()
        utils.report_status_error({"Error" : f"{ex}"})
        raise
    finally:
        utils.flush() 


if __name__ == "__main__":
    target_index = int(sys.argv[1])
    targets_total = int(sys.argv[2])
    target = sys.argv[3]

    if len(sys.argv) == 5:
        run(target_index, targets_total, target, sys.argv[4])
    else:
        run(target_index, targets_total, target, None)

