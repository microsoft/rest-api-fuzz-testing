import sys
import os
import logging
from logging import StreamHandler
import shutil
import json

run_directory = os.environ['RAFT_TOOL_RUN_DIRECTORY']
raft_libs_dir = os.path.join(run_directory, '..', '..', 'libs', 'python3')
sys.path.append(raft_libs_dir)
import raft

zap_dir = '/zap'
sys.path.append(zap_dir)

raftUtils = raft.RaftUtils('ZAP')

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
                raftUtils.report_status_running(self.details)
            else:
                progress='Passive scanning complete'
                i = txt.find(progress)
                if i != -1:
                    self.details["Scan progress"] = "Active and Passive Scan progress %100"
                    raftUtils.report_status_running(self.details)

zap = __import__("zap-api-scan")

def post_bugs(target_index):
    if os.path.exists(f'/zap/wrk/{target_index}-report.json'):
        print(f'Using file {target_index}-report.json for reported bugs.')
        with open(f'/zap/wrk/{target_index}-report.json') as f:
            reportData = json.load(f)

        # Walk though the report, flattening the alert structure for bug reporting.
        # The only nested item is the instances array.
        for site in reportData['site']:
            print(str(len(site['alerts'])) + " bugs found.")
            for alert in site['alerts']:
                bugDetails = {}
                for item in alert:
                    if item == 'instances':
                        instanceList = alert['instances']
                        for instanceCount in range(0, len(instanceList)):
                            for instanceItem in instanceList[instanceCount]:
                                bugDetails.update({"Instance" + str(instanceCount) + "-" + instanceItem : instanceList[instanceCount][instanceItem]})
                    else:
                        bugDetails.update({item : alert[item]})
                raftUtils.report_bug(bugDetails)
    else:
        print(f'File {target_index}-report.json does NOT exist.')

def count_bugs(target_index):
    bugCount = 0
    if os.path.exists(f'/zap/wrk/{target_index}-report.json'):
        with open(f'/zap/wrk/{target_index}-report.json') as f:
            reportData = json.load(f)

        # Every alert is a bug
        for site in reportData['site']:
            bugCount = len(site['alerts'])

    return bugCount

def run_zap(target_index, targets_total, host, target, token):
    if token:
        raftUtils.log_trace('Authentication token is set')
        auth = ('-config replacer.full_list(0).description=auth1'
                ' -config replacer.full_list(0).enabled=true'
                ' -config replacer.full_list(0).matchtype=REQ_HEADER'
                ' -config replacer.full_list(0).matchstr=Authorization'
                ' -config replacer.full_list(0).regex=false'
                f' -config replacer.full_list(0).replacement="{token}"')
        zap_auth_config = ['-z', auth]
    else:
        raftUtils.log_trace('Authentication token is not set')
        zap_auth_config = []

    if host:
        host_config = ['-O', host]
        raftUtils.log_trace(f'OpenAPI host override is set to {host}')
    else:
        host_config = []
    os.chdir(zap_dir)
    r = 0
    try:
        print('Removing zap.out if exists')
        os.remove('/zap/zap.out')
    except:
        pass

    try:
        details = {"targetIndex": target_index, "numberOfTargets" : targets_total, "target": target, "totalBugCount": 0}
        print(f"Starting ZAP target: {target} host_config: {host_config}")

        if os.path.exists(target):
            shutil.copy(target, '/zap/wrk/swagger.json')
            target='swagger.json'

        raftUtils.log_trace(f"Starting ZAP")
        raftUtils.report_status_running(details)

        status_reporter = StatusReporter(details)
        logger = logging.getLogger()
        logger.addHandler(status_reporter)

        zap.main([ '-t', target,
                   '-f', 'openapi',
                   '-J', f'{target_index}-report.json',
                   '-r', f'{target_index}-report.html',
                   '-w', f'{target_index}-report.md',
                   '-x', f'{target_index}-report.xml',
                   '-d'] + zap_auth_config + host_config)

    except SystemExit as e:
        r = e.code

    raftUtils.log_trace(f"ZAP exited with exit code: {r}")
    shutil.copy('/zap/zap.out', f'/zap/wrk/{target_index}-zap.out')

    # Update the status with the total bug count.
    details["totalBugCount"] = count_bugs(target_index)
    raftUtils.report_status_running(details)

    post_bugs(target_index)

    if r <= 2:
        r = 0
        
    if target_index + 1 == targets_total:
        raftUtils.report_status_completed(details)

    return r

def run(target_index, targets_total, host, target, token):
    try:
        raftUtils.report_status_created()
        return run_zap(target_index, targets_total, host, target, token)
    except Exception as ex:
        raftUtils.log_exception(ex)
        raftUtils.report_status_error({"Error" : f"{ex}"})
        raise
    finally:
        raftUtils.flush()
        os.sys.stdout.flush()


if __name__ == "__main__":
    target_index = int(sys.argv[1])
    targets_total = int(sys.argv[2])

    host = None
    target = None
    token = None

    args = sys.argv
    i = 1
    for arg in args[i:]:
        if arg == '--target':
            target = args[i+1]

        if arg == '--token':
            token = args[i+1]

        if arg == '--host':
            host = args[i+1]
        i=i+1

    run(target_index, targets_total, host, target, token)
