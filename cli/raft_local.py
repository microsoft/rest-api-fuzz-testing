# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.
import argparse
import textwrap
import os
import subprocess
import uuid
import json
import datetime
import time
import requests
import logging

from dateutil import parser as DateParser
from subprocess import PIPE
from raft_sdk.raft_common import  RaftJsonDict, get_version
from raft_sdk.raft_service import RaftJobConfig, print_status

from opencensus.ext.azure.log_exporter import AzureEventHandler

script_dir = os.path.dirname(os.path.abspath(__file__))
json_hook = RaftJsonDict.raft_json_object_hook
work_directory = os.path.join(script_dir, 'local')

class RaftLocalException(Exception):
    pass

class RaftLocalCliDockerException(Exception):
    def __init__(self, error_message, args):
        self.error_message = error_message
        self.az_args = args

    def __str__(self):
        return (f"args: {self.az_args}"
                f"{os.linesep}"
                f"std error: {self.error_message}")


def docker(args):
    '''
        Executes docker command

        Parameters:
            args: arguments to pass to docker

        Returns:
            Text from standard output
    '''
    r = subprocess.run("docker " + args, shell=True, stdout=PIPE, stderr=PIPE)
    stdout = r.stdout.decode()
    stderr = r.stderr.decode()

    if stderr:
        raise RaftLocalCliDockerException(stderr, args)
    else:
        return stdout


def init_tools(tools_path):
    '''
        Load tool configurations and create mount
        destination paths for the tools

        Parameters:
            tools_path: path to tools directory

        Returns:
            tools configurations list and list of tools paths
    '''
    tools = {}
    tool_paths = {}
    for root_folder_path, dirs, files in os.walk(tools_path):
        for file_name in files:
            if file_name == 'config.json':
                tool_folder = os.path.basename(root_folder_path)
                file_path = os.path.join(root_folder_path, file_name)
                with open(file_path) as j:
                    tools[tool_folder] = (
                        json.load(j, object_hook=RaftJsonDict.raft_json_object_hook))
                    tool_paths[tool_folder] = f"/raft-tools/tools/{tool_folder}"
    return tools, tool_paths

def init_local():
    if not os.path.exists(work_directory):
        os.mkdir(work_directory)

    secrets_path = os.path.join(work_directory, 'secrets')
    if not os.path.exists(secrets_path):
        os.mkdir(secrets_path)

    storage = os.path.join(work_directory, 'storage')
    if not os.path.exists(storage):
        os.mkdir(storage)

    events_sink = os.path.join(work_directory, 'events_sink')
    if  not os.path.exists(events_sink):
        os.mkdir(events_sink)

    return storage, secrets_path, events_sink

# convert string time-span to seconds
def time_span_to_seconds(time_span):
    if time_span:
        try:
            t = time.strptime(time_span, '%d.%H:%M:%S')
        except ValueError:
            t = time.strptime(time_span, '%H:%M:%S')
        seconds = (
                int(datetime.timedelta(
                        hours=t.tm_hour,
                        minutes=t.tm_min,
                        seconds=t.tm_sec).total_seconds()))
        return seconds
    else:
        return 0

def parse_utc_time(t):
    return DateParser.parse(t)

def trigger_webhook(url, data, metadata=None):
    for d in data:
        d['Subject'] = d['EventType']
        d['Id'] = f'{uuid.uuid4()}'
        d['Data'] = d.pop('message')
        if metadata:
            d['Data']['Metadata'] = metadata
        d['Data']['ResultsUrl'] = ''
        d['Topic'] = ''
        d['EventTime'] = f'{datetime.datetime.utcnow()}'
        d['DataVersion'] = '1.0'
        d['metadataVersion'] = '1'

    response = requests.post(url, json=data)
    return response
        

class RaftLocalCLI():
    def __init__(self, network='host', telemetry=True):
        # This will hole a cumulative count of the bugs found over the course of the job. 
        self.bugs = []
        self.status = []
        self.appinsights_instrumentation_key = '9d67f59d-4f44-475c-9363-d0ae7ea61e95'
        self.telemetry = telemetry

        self.network = network
        self.work_directory = work_directory
        self.tools, self.tool_paths =\
            init_tools(os.path.join(script_dir, 'raft-tools', 'tools'))

        self.container_utils, self.container_utils_paths =\
            init_tools(os.path.join(script_dir, 'raft-tools'))

        if not os.path.exists(self.work_directory):
            os.mkdir(self.work_directory)
        self.storage, self.secrets_path, self.events_sink =\
            init_local()

        self.source = "local"
        self.logger = logging.getLogger(__name__)
        logging.basicConfig(level=logging.INFO)
        if (telemetry):  
            ai_key = 'InstrumentationKey=' + self.appinsights_instrumentation_key
            handler = AzureEventHandler(connection_string=ai_key)
            handler.add_telemetry_processor(self.telemetry_processor)
            self.logger.addHandler(handler)
            
    # Remove identifying information
    def telemetry_processor(self, envelope):
        envelope.tags['ai.cloud.roleInstance'] = ''
        return True

    def common_custom_dimensions(self, units, count):
        return {
                'SiteHash' : str(uuid.getnode()), 
                'Source' : self.source,
                'TimeStamp' : datetime.datetime.utcnow().strftime("%d/%m/%Y %H:%M:%S"),
                'Units' : units,
                'Version' : get_version(),
                'Count' : count
                }

    # These properties are used for the Created and Completed events
    def log_telemetry(self, name, units, count):
        common = self.common_custom_dimensions(units, count)
        common.update({'Name' : name})
        return {'custom_dimensions' : common }

    # These properties are used for the BugsFound event
    def log_bugs_found_telemetry(self, toolname, units, count):
        common = self.common_custom_dimensions(units, count)
        common.update({'ToolName' : toolname})
        return {'custom_dimensions' : common }

    # Record how many bugs were found by each tool
    def log_bugs_per_tool(self):
        tools = {}
        
        for bug in self.bugs:
            # When running locally in raft-action the key is
            # Data instead of Message.
            if 'Message' in bug:
                key = 'Message'
            else:
                key = 'Data'
            toolname = bug[key]['Tool']
            if toolname in tools:
                tools[toolname] += 1
            else:
                tools[toolname] = 1

        for toolname in tools:
            self.logger.info("BugsFound", extra=self.log_bugs_found_telemetry('Task: ' + toolname, 'Bugs', tools[toolname]))

    def mount_read_write(self, source, target):
        return f'--mount type=bind,source="{source}",target="{target}" '

    def mount_read_only(self, source, target):
        return f'--mount type=bind,source="{source}",target="{target}",readonly '

    def env_variable(self, name, value):
        vv = (f"{value}").replace('"', '\\"')
        return f'--env {name}="{vv}" '

    def common_environment_variables(self, job_id, work_dir):
        env = ' '
        env += self.env_variable('RAFT_JOB_ID', job_id)
        env += self.env_variable('RAFT_CONTAINER_GROUP_NAME', job_id)
        env += self.env_variable('RAFT_WORK_DIRECTORY', work_dir)
        env += self.env_variable('RAFT_SITE_HASH', '0')
        if (self.telemetry):
            env += self.env_variable('RAFT_APP_INSIGHTS_KEY', self.appinsights_instrumentation_key)

        # If we are running in a github action (or some other unique environment)
        # we will set this value before running
        # to distinquish between the different environments.
        customLocal = os.getenv("RAFT_LOCAL")
        if customLocal is None:
            env += self.env_variable('RAFT_LOCAL', 'Developer')
        else:
            env += self.env_variable('RAFT_LOCAL', customLocal)
            self.source = customLocal
        return env

    def process_job_events_sink(self, job_events_path):
        bugs = []
        job_status = {}
        for root_folder_path, dirs, files in os.walk(job_events_path):
            for file_name in files:
                file_path = os.path.join(root_folder_path, file_name)
                status = open(file_path, 'r')
                try:
                    j = json.load(status,\
                            object_hook=RaftJsonDict.raft_json_object_hook)
                    status.close()
                    os.remove(file_path) 
                    if j['EventType'] == 'BugFound':
                        bugs.append(j) 
                    elif j['EventType'] == 'JobStatus':
                        js = job_status.get(j['Message']['AgentName']) 
                        if js:
                            if parse_utc_time(j['Message']['UtcEventTime']) >\
                                parse_utc_time(js['Message']['UtcEventTime']):
                                job_status[j['Message']['AgentName']] = j
                        else:
                            job_status[j['Message']['AgentName']] = j
                except Exception as ex:
                    print(f"FAILED TO PROCESS STATUS MESSAGE:\
                            {file_path} due to {ex}")

        if len(job_status) > 0:
            status = []
            for s in job_status:
                status.append(job_status[s]['Message'])
            self.status = status
        if len(bugs) > 0:
            self.bugs = self.bugs + bugs

    def docker_create_bridge(self, network, job_id):
        if network == 'host':
            return 'host'
        elif network == 'bridge':
            bridge = 'raft-' + job_id.replace('-', '')
            docker(f'network create --driver bridge {bridge}')
            return bridge
        else:
            raise RaftLocalException('Unhandled docker network driver: ' + network)

    def docker_remove_bridge(self, bridge_name):
        if bridge_name != 'none' and bridge_name != 'host':
            docker(f'network rm {bridge_name}')

    def docker_stop_containers(self, container_names):
        if len(container_names) > 0:
            docker(f'container stop -t 0 {" ".join(container_names)}')

    def docker_remove_containers(self, container_names):
        if len(container_names) > 0:
            docker(f'container rm {" ".join(container_names)}')

    def docker_run_cmd(self, container, container_name, mounts, ports,\
            environment_variables, shell, run_cmd, bridge_name):
        docker_run_cmd = 'run -d -t --no-healthcheck --privileged --user="root"'
        if shell and run_cmd:
            docker_run_cmd += ' --entrypoint=""'
            docker_run_cmd += ' --workdir="/"'
        if container_name:
            docker_run_cmd += f' --name {container_name}'
        if bridge_name:
            docker_run_cmd += f' --network {bridge_name}'
        if mounts:
            docker_run_cmd += f' {mounts}'
        if ports and bridge_name != 'host':
            docker_run_cmd += f' {ports}'
        if environment_variables:
            docker_run_cmd += f' {environment_variables}'
        if container:
            docker_run_cmd += f' {container}'
        if shell and run_cmd:
            docker_run_cmd += f' {run_cmd}'
        return docker_run_cmd


    def start_agent_utils(self, bridge_name, job_id, job_events, secrets):
        config = self.container_utils['agent-utilities']
        std_out = docker('pull ' + config['container'])
        print(std_out)

        env = self.env_variable( 'ASPNETCORE_URLS', f'http://*:{config["port"]}')
        for s in secrets:
            with open(os.path.join(self.secrets_path, s), 'r') as secret_file:
                secret = secret_file.read()
                env += self.env_variable(f'RAFT_{s}', secret.strip())

        container_name = f'raft-agent-utilities-{job_id}'
        mounts = self.mount_read_only((os.path.join(script_dir, "raft-tools")), "/raft-tools")
        mounts += self.mount_read_write(job_events, '/raft-events-sink')

        cmd = self.docker_run_cmd(
                container=config['container'],
                container_name=container_name,
                mounts=mounts,
                ports=None,
                environment_variables=env,
                shell=None,
                run_cmd=None,
                bridge_name=bridge_name)

        print(f"Running docker with command : {cmd}")
        out = docker(cmd)
        print(out)
        if bridge_name == 'host':
            return container_name, 'localhost', config['port']
        else:
            return container_name, container_name, config['port']

    def start_test_targets(self, job_config, job_id, work_dir,\
            job_dir, bridge_name):
        task_index = 0
        test_services_startup_delay = 0
        testTargets = job_config.config.get('testTargets')
        test_target_container_names = []
        post_run_wait = 0
        if testTargets:
            services = testTargets.get('services')
            if services:
                for service in services:
                    std_out = docker('pull ' + service['container'])
                    print(std_out)

                for service in services:
                    d = service.get('ExpectedDurationUntilReady')
                    if d:
                        test_services_startup_delay =\
                            max(test_services_startup_delay, time_span_to_seconds(d))

                for service in services:
                    env = self.common_environment_variables(job_id, work_dir)
                    env += self.env_variable('RAFT_TASK_INDEX', task_index)
                    env += self.env_variable('RAFT_CONTAINER_NAME',\
                                            f'{job_id}_{task_index}')

                    shell = service['shell']

                    run_cmd = ''
                    if service.get('isIdling'):
                        args = map(lambda a: f'"{a}"', service['idle']['shellArguments'])
                        run_cmd = f"{shell} {' '.join(args)}"
                        startup_delay = 0
                    else:
                        startup_delay = test_services_startup_delay
                        if service.get('run'):
                            args = map(lambda a: f'"{a}"', service['run']['shellArguments'])
                            run_cmd = f"{shell} {' '.join(args)}"

                    post_run_cmd = ''
                    if service.get('PostRun'):
                        args = map(lambda a: f'"{a}"', service['postrun']['shellArguments'])
                        post_run_cmd = f"{shell} {' '.join(args)}"
                        post_run_seconds =\
                            time_span_to_seconds(service['postrun']['ExpectedRunDuration'])
                        post_run_wait = max(post_run_seconds, post_run_wait)

                    # create work folder and mount it
                    task_dir = os.path.join(job_dir, service['outputFolder'])
                    os.mkdir(task_dir)

                    if run_cmd:
                        with open(os.path.join(task_dir, 'task-run.sh'), 'w') as tc:
                            tc.write(run_cmd)
                            run_cmd=f"{shell} {work_dir}/task-run.sh"

                    if post_run_cmd:
                        with open(os.path.join(task_dir, 'task-post-run.sh'), 'w') as tc:
                            tc.write(post_run_cmd)
                            post_run_cmd=f"{shell} {work_dir}/task-post-run.sh"

                    mounts = self.mount_read_write(task_dir, work_dir)

                    env += self.env_variable('RAFT_RUN_CMD', run_cmd)
                    env += self.env_variable('RAFT_POST_RUN_COMMAND', post_run_cmd)
                    env += self.env_variable('RAFT_CONTAINER_SHELL', shell)
                    env += self.env_variable('RAFT_STARTUP_DELAY', startup_delay)

                    service_environment_variables = service.get('environmentVariables')
                    if (service_environment_variables):
                        for e in service_environment_variables:
                            env += self.env_variable(e, service_environment_variables[e])

                    expose_ports = None
                    #when using bridge networking - no need to expose ports,
                    #since those are accessible from within the network to other
                    #containers
                    #expose_ports = ''
                    #ports = service.get('Ports')
                    #if ports:
                    #    for port in ports:
                    #        expose_ports += f'--publish {port}:{port}/tcp '

                    if not run_cmd:
                        run_cmd = None
                        shell = None

                    container_name = f'raft-service-{job_id}-{task_index}'
                    cmd = self.docker_run_cmd(
                            container=service['container'],
                            container_name=container_name,
                            mounts=mounts,
                            ports=expose_ports,
                            environment_variables=env,
                            shell=shell,
                            run_cmd=run_cmd,
                            bridge_name=bridge_name)
                    test_target_container_names.append(container_name)
                    print(f"Running docker with command : {cmd}")
                    out = docker(cmd)
                    print(out)
                    task_index += 1

        return task_index, test_services_startup_delay,\
                test_target_container_names, post_run_wait


    def secrets_to_import(self, job_config):
        secrets = []
        testTasks = job_config.config.get('testTasks')
        if testTasks.get('tasks'):
            for testTask in testTasks['tasks']:
                if testTask.get('keyVaultSecrets'):
                    for s in testTask['keyVaultSecrets']:
                        secrets.append(s)
        return secrets
        
    def start_test_tasks(self, job_config, task_index,\
            test_services_startup_delay, job_id, work_dir,\
            job_dir, job_events, bridge_name, agent_utilities_url):

        testTasks = job_config.config.get('testTasks')
        if testTasks.get('tasks'):
            for testTask in testTasks['tasks']:
                # Record in telemetry that we are using a particular tool
                self.logger.info("Created", extra=self.log_telemetry("Task: " + testTask['toolName'], "task", 1))
                config = self.tools[testTask['toolName']]
                std_out = docker('pull ' + config['container'])
                print(std_out)

        test_tasks_container_names = []
        if testTasks.get('tasks'):
            target_config = testTasks.get('targetConfiguration')
            if target_config:
                if target_config.get('localRun'):
                    testTasks['targetConfiguration'] = target_config['localRun']

            for testTask in testTasks['tasks']:
                config = self.tools[testTask['toolName']]
                env = self.common_environment_variables(job_id, work_dir)

                if (config.get('environmentVariables')):
                    for e in config['environmentVariables']:
                        env += self.env_variable(e, config['environmentVariables'][e])

                env += self.env_variable('RAFT_AGENT_UTILITIES_URL', agent_utilities_url)
                env += self.env_variable('RAFT_TASK_INDEX', task_index)
                env += self.env_variable('RAFT_CONTAINER_NAME', f'{job_id}_{task_index}')

                shell = config['shell']

                args = map(lambda a: f'"{a}"', config['run']['shellArguments'])
                cmd = f"{shell} {' '.join(args)}"

                if testTask.get('isIdling'):
                    args = map(lambda a: f'"{a}"', config['idle']['shellArguments'])
                    run_cmd = f"{shell} {' '.join(args)}"
                    startup_delay = 0
                else:
                    run_cmd = cmd
                    startup_delay = test_services_startup_delay

                task_dir = os.path.join(job_dir, testTask['outputFolder'])
                os.mkdir(task_dir)
                task_events = os.path.join(job_events, testTask['outputFolder'])
                os.mkdir(task_events)

                with open(os.path.join(task_dir, 'task-run.sh'), 'w') as tc:
                    tc.write(run_cmd)
                    run_cmd = f"{shell} {work_dir}/task-run.sh"

                env += self.env_variable('RAFT_STARTUP_DELAY', startup_delay)
                env += self.env_variable('RAFT_RUN_CMD', run_cmd)
                env += self.env_variable('RAFT_TOOL_RUN_DIRECTORY', self.tool_paths[testTask['toolName']])
                env += self.env_variable('RAFT_POST_RUN_COMMAND', '')
                env += self.env_variable('RAFT_CONTAINER_SHELL', shell)

                if testTask.get('keyVaultSecrets'):
                    for s in testTask['keyVaultSecrets']:
                        with open(os.path.join(self.secrets_path, s), 'r') as secret_file:
                            secret = secret_file.read()
                            env += self.env_variable(f'RAFT_{s}', secret.strip())
                # create work folder and mount it

                # create task_config json, and save it to task_dir
                with open(os.path.join(task_dir, 'task-config.json'), 'w') as tc:
                    if not(testTask.get('targetConfiguration')):
                        testTask['targetConfiguration'] = testTasks['targetConfiguration']

                    if not(testTask.get('Duration')) and testTasks.get('Duration'):
                        testTask['Duration'] = testTasks['Duration']

                    json.dump(testTask, tc, indent=4)

                mounts = self.mount_read_write(task_dir, work_dir)
                mounts += self.mount_read_write(task_events, '/raft-events-sink')
                mounts += self.mount_read_only((os.path.join(script_dir, "raft-tools")), "/raft-tools")

                if job_config.config.get("readOnlyFileShareMounts"):
                    for v in job_config.config.get("readOnlyFileShareMounts"):
                        mounts += self.mount_read_only(os.path.join(self.storage, v['FileShareName']), v['MountPath'])

                if job_config.config.get("readWriteFileShareMounts"):
                    for v in job_config.config.get("readWriteFileShareMounts"):
                        mounts += self.mount_read_write(os.path.join(self.storage, v['FileShareName']), v['MountPath'])

                container_name = f'raft-{testTask["toolName"]}-{job_id}-{task_index}'

                # add command to execute
                cmd = self.docker_run_cmd(
                        container=config['container'],
                        container_name=container_name,
                        mounts=mounts,
                        ports=None,
                        environment_variables=env,
                        shell=shell,
                        run_cmd=run_cmd,
                        bridge_name=bridge_name
                    )
                test_tasks_container_names.append(container_name)
                print(f"Running docker with command : {cmd}")
                out = docker(cmd)
                print(out)
                task_index += 1
        else:
            raise Exception("Test tasks are missing from job config")

        return test_tasks_container_names


    def check_containers_exited(self, containers):
        if len(containers) == 0:
            return True, True, []
        else:
            container_info = docker('container inspect ' + ' '.join(containers))
            infos = json.loads(container_info)
            all_exited = True
            any_exited = False
            for j in infos:
                if j['State']['Running']:
                    all_exited = False
                else:
                    any_exited = True
            return all_exited, any_exited, infos
                
    def print_logs(self, containers):
        for c in containers:
            print(f"-------------------------- LOGS for [{c}] -------------------")
            print()
            print()
            stdout = docker(f'logs {c} --tail 64')
            print(stdout)

    def wait_for_container_termination(self, containers, service_containers,\
        raft_utilities,\
        job_events_path, duration, metadata, job_status_webhook_url,\
        bug_found_webhook_url):
        saved_duration = duration
        print('Waiting for containers: ' + '; '.join(containers))
        wait_seconds = 5
        while(True):
            if service_containers and len(service_containers) > 0:
                _, service_any_exited, _ = self.check_containers_exited(service_containers)
                if service_any_exited:
                    self.print_logs(service_containers)
                    raise RaftLocalException("At least one service container exited\
                                            before the end of the job run")

            if raft_utilities and len(raft_utilities) > 0:
                _, raft_utilities_any_exited, _ = self.check_containers_exited(raft_utilities)
                if raft_utilities_any_exited:
                    self.print_logs(raft_utilities)
                    raise RaftLocalException("At least one RAFT utilities container exited\
                                            before the end of the job run")

            all_exited, _, infos = self.check_containers_exited(containers)
            if all_exited:
                # Some status and bugs are not processed once the tasks finish
                # so process them now
                self.process_job_events_sink(job_events_path)
                print_status(self.status)

                # Trigger bug found webhook for all the bugs we found.
                # Since self.bugs is a cumulative list of bugs found, just trigger
                # the webhooks once at the end of the run so there aren't multiple triggers
                # happening.
                if bug_found_webhook_url:
                    for bug in self.bugs:
                        trigger_webhook(bug_found_webhook_url, [bug], metadata)

                exit_infos = []
                for j in infos:
                    exit_infos.append(
                            {
                                'Name': j['Name'],
                                'Status': j['State']['Status'],
                                'ExitCode': j['State']['ExitCode'],
                                'ErrorMessage': j['State']['Error']
                            })
                return exit_infos
            else:
                self.process_job_events_sink(job_events_path)
                print_status(self.status)

                # Trigger job status webhook
                if job_status_webhook_url:
                    for k in self.status:
                        trigger_webhook(job_status_webhook_url, [{'Message' : k}], metadata)

                time.sleep(wait_seconds)
                if duration:
                    duration = duration - wait_seconds
                    if duration <= 0:
                        print(f'Job run exceeded duration of {saved_duration} seconds. Exiting...')
                        return None

    def job_status(self, job_id):
        job_events_path = os.path.join(self.events_sink, job_id)
        self.process_job_events_sink(job_events_path)
        return self.status

    def post_run(self, containers):
        container_info = docker('container inspect ' + ' '.join(containers))
        infos = json.loads(container_info)

        container_info = {}
        for info in infos:
            if info['State']['Status'] == "running":
                for e in info['Config']['Env']:
                    if e.startswith("RAFT_POST_RUN_COMMAND="):
                        container_info[info['Name']] = e[len("RAFT_POST_RUN_COMMAND="):]
                        break

        for container in containers:
            pr = container_info.get("/" + container)
            if pr:
                stdout = docker(f'container exec -t --user="root" --privileged {container} ' + pr)
                print(stdout)


    def new_job(self, job_config, job_status_webhook_url=None, bug_found_webhook_url=None):
        job_id = f'{uuid.uuid4()}'
        print(f'creating job {job_id}')

        if job_config.config.get('rootFileShare'):
            rootFileShare = os.path.join(self.storage, job_config.config['rootFileShare'])
            if not os.path.exists(rootFileShare):
                os.mkdir(rootFileShare)
            job_dir = os.path.join(rootFileShare, job_id)
        else:
            job_dir = os.path.join(self.storage,  job_id)

        job_events = os.path.join(self.events_sink, job_id)
        os.mkdir(job_events)

        os.mkdir(job_dir)
        print(f"------------------------  Job results: {job_dir}")
        work_dir = '/work_dir_' + job_id
        test_task_container_names = []
        test_target_container_names = []
        agent_utils = None
        try:
            bridge_name = self.docker_create_bridge(self.network, job_id)
            agent_utils, agent_utils_endpoint, agent_utils_port = self.start_agent_utils(bridge_name, job_id,\
                job_events, self.secrets_to_import(job_config))

            task_index, test_services_startup_delay, test_target_container_names, post_run_wait =\
                self.start_test_targets(job_config, job_id,\
                work_dir, job_dir, bridge_name)

            test_task_container_names =\
                self.start_test_tasks(job_config, task_index,\
                test_services_startup_delay, job_id, work_dir,\
                job_dir, job_events, bridge_name, f'http://{agent_utils_endpoint}:{agent_utils_port}')

            duration = None
            if job_config.config.get('duration'):
                duration = time_span_to_seconds(job_config.config.get('duration'))

            metadata = None
            if 'webhook' in job_config.config:
                if 'metadata' in job_config.config['webhook']:
                    metadata = job_config.config['webhook']['metadata']

            # Record in telemetry we've created a job
            self.logger.info("Created", extra=self.log_telemetry("Job", "job", 1))

            stats = self.wait_for_container_termination(test_task_container_names,\
                        test_target_container_names, [agent_utils],\
                        job_events, duration, metadata,\
                        job_status_webhook_url, bug_found_webhook_url)
            if stats:
                print(stats)

            # Log the completion telemetry here so if there is a failure it's not logged. 
            self.logger.info("Completed", extra=self.log_telemetry("Job", "job", 1))
            # iterate through the tools and mark them as completed. 
            testTasks = job_config.config.get('testTasks')
            if testTasks.get('tasks'):
                for testTask in testTasks['tasks']:
                    # Record in telemetry that we are using a particular tool
                    self.logger.info("Completed", extra=self.log_telemetry("Task: " + testTask['toolName'], "task", 1))

        finally:
            if len(test_task_container_names) > 0:
                self.docker_stop_containers(test_task_container_names)

            if len(test_target_container_names) > 0:
                self.post_run(test_target_container_names)
                print(f'Waiting for Post-Run command to finish {post_run_wait} seconds')
                time.sleep(post_run_wait)

            try:
                self.docker_stop_containers(test_target_container_names)

            except Exception as ex:
                print(f'Failed to stop test target containers due to {ex}')

            try:
                if agent_utils:
                    self.docker_stop_containers([agent_utils])
            except Exception as ex:
                print(f'Failed to stop agent utilities due to {ex}')

            self.log_bugs_per_tool()

            print("Job finished, cleaning up job containers")
            print(f"------------------------  Job results: {job_dir}")
            try:
                self.docker_remove_containers(test_task_container_names)
            except Exception as ex:
                print(f'Failed to remove test task containers due to : {ex}')

            try:
                self.docker_remove_containers(test_target_container_names)
            except Exception as ex:
                print(f'Failed to remove test target containers due to: {ex}')

            try:
                self.docker_remove_bridge(bridge_name)
            except Exception as ex:
                print(f'Failed to remove bridge {bridge_name} due to {ex}')

        return {'jobId' : job_id}

    def poll(self, job_id):
        '''
        No implementation required since new_job is synchronous
        Having this to keep consistent with RAFT Azure CLI
        '''
        pass


def run(args):
    def ArgumentRequired(name):
        print(f'The {name} parameter is required')
        quit()

    job_action = args.get('job-action')
    local_action = args.get('local-action')

    if local_action == 'init':
        storage, secrets, event_sink = init_local()
        print(f'Created results storage folder: {storage}')
        print(f'Created secrets folder: {secrets}')
        print(f'Created events_sink folder: {event_sink}')

    if job_action == 'create':
        cli = RaftLocalCLI(network=args.get('network'), telemetry=args.get('no_telemetry'))
        json_config_path = args.get('file')
        if json_config_path is None:
            ArgumentRequired('--file')

        substitutionDictionary = {}
        substitutionParameter = args.get('substitute')
        if substitutionParameter:
            substitutionDictionary = json.loads(
                                        substitutionParameter,
                                        object_hook=json_hook)

        job_config = (
            RaftJobConfig(file_path=json_config_path, substitutions=substitutionDictionary))

        print(job_config.config)
        duration = args.get('duration')
        if duration:
            job_config.config['duration'] = duration

        cli.new_job(job_config, args.get('jobStatusWebhookUrl'), args.get('bugFoundWebhookUrl'))
        

if __name__ == "__main__":
    parser = argparse.ArgumentParser(
        description=f'RAFT-Local CLI {get_version()}',
        formatter_class=argparse.RawTextHelpFormatter)

    sub_parser = parser.add_subparsers()
    init_parser = sub_parser.add_parser('local')
    init_parser.add_argument(
        'local-action',
        choices=['init'],
        help=textwrap.dedent('''\
Create folder structure required for local runs
        '''))

    job_parser = sub_parser.add_parser(
        'job',
        formatter_class=argparse.RawTextHelpFormatter)

    job_parser.add_argument(
        'job-action',
        choices=['create'],
        help=textwrap.dedent('''\
create  - Create a new job
        --file is required
        '''))

    job_parser.add_argument(
        '--file',
        help=textwrap.dedent('''\
File path to the job definition file.
Required for 'create' and 'update' commands'''))

    job_parser.add_argument(
        '--duration',
        help='The duration in hours that a job should run')

    job_parser.add_argument(
        '--substitute',
        help=textwrap.dedent('''\
Dictionary of values to find and replace in the --file.
Should be in the form {"find1":"replace1", "find2":"replace2"}
This parameter is only valid with the create and update commands
    '''))

    job_parser.add_argument(
        '--bugFoundWebhookUrl',
        help=textwrap.dedent('''\
Post to the Webhook on bug found
        '''))

    job_parser.add_argument(
        '--jobStatusWebhookUrl',
        help=textwrap.dedent('''\
Post to the Webhook on job status change
        '''))

    job_parser.add_argument(
        '--network',
        choices=['host', 'bridge'],
        default='host',
        help=textwrap.dedent('''\
Select docker network driver. If not set then 'Host' is used.

host - Use localhost networking.
Works on Linux for accessing locahost service running on a dev box.
On Windows you can use WSL2 (https://docs.microsoft.com/en-us/windows/wsl/compare-versions).

bridge - create a network-bridge with a random name.
This allows running of multiple jobs in parallel on the same device.
        '''))

    job_parser.add_argument(
        '--no-telemetry',
        action='store_false',
        help=textwrap.dedent('''\
Use this flag to turn off anonymous telemetry
        '''))

    args = parser.parse_args()
    run(vars(args))
