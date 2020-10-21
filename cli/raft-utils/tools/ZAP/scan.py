import os
import sys
import json
import shutil
import time
import io
import logging
from logging import StreamHandler

from applicationinsights import TelemetryClient
from azure.servicebus import TopicClient, Message
from contextlib import redirect_stdout

class RaftUtils():
    def __init__(self):
        work_directory = os.environ['RAFT_WORK_DIRECTORY']
        with open(os.path.join(work_directory, 'task-config.json'), 'r') as task_config:
            self.config = json.load(task_config)

        connection_str = os.environ['RAFT_SB_OUT_SAS']
        self.topic_client = TopicClient.from_connection_string(connection_str)

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
        msg = Message(str.encode(json.dumps(m)))
        self.topic_client.send(msg)

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

    def get_swagger_target(self):
        swagger = self.config.get("swaggerLocation")
        if swagger and swagger.get("url"):
            return swagger["url"]
        elif swagger.get("filePath"):
            return swagger["filePath"]


zap_dir = '/zap'
sys.path.append(zap_dir)
zap = __import__("zap-api-scan")

utils = RaftUtils()

class StatusReporter(StreamHandler):
    def __init__(self):
        StreamHandler.__init__(self)
        self.last_txt = None

    def emit(self, record):
        txt = self.format(record)
        if txt != self.last_txt:
            self.last_txt = txt
            progress='Active Scan progress %:'
            i = txt.find(progress)
            if i != -1:
                utils.report_status_running([txt[i :]])

def run_zap(token):
    target = utils.get_swagger_target()
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
        utils.report_status_running()

        status_reporter = StatusReporter()
        logger = logging.getLogger()
        logger.addHandler(status_reporter)
        zap.main(['-t', target, '-f', 'openapi', '-J', 'report.json', '-r', 'report.html', '-w', 'report.md', '-x', 'report.xml', '-d'] + zap_auth_config)

    except SystemExit as e:
        r = e.code

    utils.log_trace(f"ZAP exited with exit code: {r}")
    shutil.copy('/zap/zap.out', '/zap/wrk/zap.out')

    utils.report_status_completed()
    if r < 2:
        return 0
    else:
        return r

def run(token):
    try:
        utils.report_status_created()
        return run_zap(token)
    except Exception as ex:
        utils.log_exception()
        utils.report_status_error([f"{ex}"])
        raise
    finally:
        utils.flush() 


if __name__ == "__main__":
    if len(sys.argv) == 2:
        run(sys.argv[1])
    else:
        run(None)

