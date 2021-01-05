import sys
import os
import logging
from logging import StreamHandler
import shutil

run_directory = os.environ['RAFT_TOOL_RUN_DIRECTORY']
raft_libs_dir = os.path.join(run_directory, '..', '..', 'libs', 'python3')
sys.path.append(raft_libs_dir)
import raft

zap_dir = '/zap'
sys.path.append(zap_dir)

raftUtils = raft.RaftUtils("ZAP")

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

zap = __import__("zap-api-scan")

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
        raftUtils.log_trace("Starting ZAP")
        details = {"targetIndex": target_index, "numberOfTargets" : targets_total, "target": target}
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
        details["Scan progress"] = "Active scan progress %: 100"
        raftUtils.report_status_running(details)

    except SystemExit as e:
        r = e.code

    raftUtils.log_trace(f"ZAP exited with exit code: {r}")
    shutil.copy('/zap/zap.out', f'/zap/wrk/{target_index}-zap.out')

    if r <= 2:
        r = 0
        
    if target_index + 1 == targets_total:
        raftUtils.report_status_completed(details)
    raftUtils.sb_client.close()
    return r

def run(target_index, targets_total, host, target, token):
    try:
        raftUtils.report_status_created()
        return run_zap(target_index, targets_total, host, target, token)
    except Exception as ex:
        raftUtils.log_exception()
        raftUtils.report_status_error({"Error" : f"{ex}"})
        raise
    finally:
        raftUtils.flush() 


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
