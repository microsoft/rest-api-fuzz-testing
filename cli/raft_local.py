# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.
import argparse
import textwrap
import os
import subprocess
import uuid
import pathlib
import json
import datetime
import time
from subprocess import PIPE
from raft_sdk.raft_common import RaftDefinitions, RaftJsonDict, get_version
from raft_sdk.raft_service import RaftJobConfig, RaftJobError

script_dir = os.path.dirname(os.path.abspath(__file__))
json_hook = RaftJsonDict.raft_json_object_hook


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

def init_local(work_directory):
    secrets_path = os.path.join(work_directory, 'secrets')
    if not os.path.exists(secrets_path):
        os.mkdir(secrets_path)

    storage = os.path.join(work_directory, 'storage')
    if not os.path.exists(storage):
        os.mkdir(storage)

    return storage, secrets_path

class RaftLocalCLI():
    def __init__(self, work_directory):
        self.work_directory = work_directory
        self.tools, self.tool_paths = (
            init_tools(os.path.join(script_dir, 'raft-tools', 'tools')))

        if not os.path.exists(self.work_directory):
            os.mkdir(self.work_directory)
        self.storage, self.secrets_path = init_local(work_directory)

    # generate container name
    def container_name(self, job_id, tool_name):
        return tool_name + '-' + job_id

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
        env += self.env_variable('RAFT_APP_INSIGHTS_KEY', '00000000-0000-0000-0000-000000000000')
        env += self.env_variable('RAFT_SITE_HASH', '0')
        env += self.env_variable('RAFT_SB_OUT_SAS', 'dummy_sas')
        env += self.env_variable('RAFT_LOCAL', '1')
        return env

    def docker_create_bridge(self, job_id):
        # docker(f'network create --driver bridge {job_id}')
        # return job_id
        # use host bridge since we are trying to replicate behaviour of
        # Azure Container Instances and Azure Container Instances communicate to each
        # other over local host.
        return 'host'

    # since host bridge is persistent - this is not needed
    # def docker_remove_bridge(self, bridge_name):
    #    docker(f'network rm {bridge_name}')

    def docker_stop_containers(self, container_names):
        if len(container_names) > 0:
            docker(f'container stop -t 0 {" ".join(container_names)}')

    def docker_remove_containers(self, container_names):
        if len(container_names) > 0:
            docker(f'container rm {" ".join(container_names)}')

    def docker_run_cmd(self, container, container_name, mounts, ports, environment_variables, shell, run_cmd, bridge_name):
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
        # ports config is not needed if 'host' bridge is used
        # if ports:
        #    docker_run_cmd += f' {ports}'
        if environment_variables:
            docker_run_cmd += f' {environment_variables}'
        if container:
            docker_run_cmd += f' {container}'
        if shell and run_cmd:
            docker_run_cmd += f' {run_cmd}'
        return docker_run_cmd

    # convert string time-span to seconds
    def time_span_to_seconds(self, time_span):
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

    def start_test_targets(self, job_config, job_id, work_dir, job_dir, bridge_name):
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
                        test_services_startup_delay = self.time_span_to_seconds(d)

                for service in services:
                    env = self.common_environment_variables(job_id, work_dir)
                    env += self.env_variable('RAFT_TASK_INDEX', task_index)
                    env += self.env_variable('RAFT_CONTAINER_NAME', f'{job_id}_{task_index}')

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
                        post_run_seconds = self.time_span_to_seconds(service['postrun']['ExpectedRunDuration'])
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


                    expose_ports = ''
                    ports = service.get('Ports')
                    if ports:
                        for port in ports:
                            expose_ports += f'--publish 127.0.0.1:{port}:{port}/tcp '

                    if not run_cmd:
                        run_cmd = None
                        shell = None

                    container_name = f'service-{job_id}-{task_index}'
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

        return task_index, test_services_startup_delay, test_target_container_names, post_run_wait

    def start_test_tasks(self, job_config, task_index, test_services_startup_delay, job_id, work_dir, job_dir, bridge_name):
        testTasks = job_config.config.get('testTasks')
        if testTasks.get('tasks'):
            for tt in testTasks['tasks']:
                config = self.tools[tt['toolName']]
                std_out = docker('pull ' + config['container'])
                print(std_out)

        test_tasks_container_names = []
        if testTasks.get('tasks'):
            target_config = testTasks.get('targetConfiguration')
            if target_config:
                if target_config.get('localRun'):
                    testTasks['targetConfiguration'] = target_config['localRun']

            for tt in testTasks['tasks']:
                config = self.tools[tt['toolName']]
                env = self.common_environment_variables(job_id, work_dir)

                if (config.get('environmentVariables')):
                    for e in config['environmentVariables']:
                        env += self.env_variable(e, config['environmentVariables'][e])

                env += self.env_variable('RAFT_TASK_INDEX', task_index)
                env += self.env_variable('RAFT_CONTAINER_NAME', f'{job_id}_{task_index}')

                shell = config['shell']

                args = map(lambda a: f'"{a}"', config['run']['shellArguments'])
                cmd = f"{shell} {' '.join(args)}"

                if tt.get('isIdling'):
                    args = map(lambda a: f'"{a}"', config['idle']['shellArguments'])
                    run_cmd = f"{shell} {' '.join(args)}"
                    startup_delay = 0
                else:
                    run_cmd = cmd
                    startup_delay = test_services_startup_delay

                task_dir = os.path.join(job_dir, tt['outputFolder'])
                os.mkdir(task_dir)

                with open(os.path.join(task_dir, 'task-run.sh'), 'w') as tc:
                    tc.write(run_cmd)
                    run_cmd = f"{shell} {work_dir}/task-run.sh"

                env += self.env_variable('RAFT_STARTUP_DELAY', startup_delay)
                env += self.env_variable('RAFT_RUN_CMD', run_cmd)
                env += self.env_variable('RAFT_TOOL_RUN_DIRECTORY', self.tool_paths[tt['toolName']])
                env += self.env_variable('RAFT_POST_RUN_COMMAND', '')
                env += self.env_variable('RAFT_CONTAINER_SHELL', shell)

                if tt.get('keyVaultSecrets'):
                    for s in tt['keyVaultSecrets']:
                        with open(os.path.join(self.secrets_path, s), 'r') as secret_file:
                            secret = secret_file.read()
                            env += self.env_variable(f'RAFT_{s}', secret.strip())
                # create work folder and mount it

                # create task_config json, and save it to task_dir
                with open(os.path.join(task_dir, 'task-config.json'), 'w') as tc:
                    if not(tt.get('targetConfiguration')):
                        tt['targetConfiguration'] = testTasks['targetConfiguration']

                    if not(tt.get('Duration')) and testTasks.get('Duration'):
                        tt['Duration'] = testTasks['Duration']

                    json.dump(tt, tc, indent=4)

                mounts = self.mount_read_write(task_dir, work_dir)
                mounts += self.mount_read_only((os.path.join(script_dir, "raft-tools")), "/raft-tools")

                if job_config.config.get("readonlyFileShareMounts"):
                    for v in job_config.config.get("readonlyFileShareMounts"):
                        mounts += self.mount_read_only(v['FileShareName'], v['MountPath'])

                if job_config.config.get("readWriteFileShareMounts"):
                    for v in job_config.config.get("readWriteFileShareMounts"):
                        mounts += self.mount_read_write(v['FileShareName'], v['MountPath'])

                container_name = f'{tt["toolName"]}-{job_id}-{task_index}'

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

    def wait_for_container_termination(self, containers, duration):
        saved_duration = duration
        print('Waiting for containers: ' + '; '.join(containers))
        while(len(containers) > 0):
            container_info = docker('container inspect ' + ' '.join(containers))
            infos = json.loads(container_info)
            all_exited = True
            for j in infos:
                if j['State']['Running']:
                    all_exited = False
                    break

            if all_exited:
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
                print('Waiting for tool containers to exit')
                time.sleep(10.0)
                if duration:
                    duration = duration - 10
                    if duration <= 0:
                        print(f'Job run exceeded duration of {saved_duration} seconds. Exiting...')
                        return None

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

    def new_job(self, job_config):
        job_id = f'{uuid.uuid4()}'
        print(f'creating job {job_id}')

        if job_config.config.get('rootFileShare'):
            rootFileShare = os.path.join(self.storage, job_config.config['rootFileShare'])
            if not os.path.exists(rootFileShare):
                os.mkdir(rootFileShare)
            job_dir = os.path.join(rootFileShare, job_id)
        else:
            job_dir = os.path.join(self.storage,  job_id)

        os.mkdir(job_dir)
        work_dir = '/work_dir_' + job_id

        bridge_name = self.docker_create_bridge(job_id)
        task_index, test_services_startup_delay, test_target_container_names, post_run_wait = (
            self.start_test_targets(job_config, job_id, work_dir, job_dir, bridge_name))
        if len(test_target_container_names) == 0:
            # no bridge needed, since there are not "services under test" deployed
            # and therefore we are testing something deployed externally
            bridge_name = None

        test_task_container_names = self.start_test_tasks(job_config, task_index, test_services_startup_delay, job_id, work_dir, job_dir, bridge_name)

        duration = None
        if job_config.config.get('duration'):
            duration = self.time_span_to_seconds(job_config.config.get('duration'))

        stats = self.wait_for_container_termination(test_task_container_names, duration)
        if stats:
            print(stats)
        else:
            self.docker_stop_containers(test_task_container_names)

        if len(test_target_container_names) > 0:
            self.post_run(test_target_container_names)
            print(f'Waiting for Post-Run command to finish {post_run_wait} seconds')
            time.sleep(post_run_wait)

        self.docker_stop_containers(test_target_container_names)

        for c in test_task_container_names:
            print(f"-------------------------- LOGS for [{c}] -------------------")
            print()
            print()
            stdout = docker(f'logs {c} --tail 32')
            print(stdout)

        print("Job finished, cleaning up job conatiners")
        self.docker_remove_containers(test_task_container_names)
        self.docker_remove_containers(test_target_container_names)


def run(args):
    def ArgumentRequired(name):
        print(f'The {name} parameter is required')
        quit()

    job_action = args.get('job-action')
    local_action = args.get('local-action')

    work_dir = os.path.join(script_dir, 'local')
    if local_action == 'init':
        storage, secrets = init_local(work_dir)
        print(f'Created results storage folder: {storage}')
        print(f'Created secrets folder: {secrets}')

    if job_action == 'create':
        cli = RaftLocalCLI(work_dir)
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

        cli.new_job(job_config)


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

    args = parser.parse_args()
    run(vars(args))
