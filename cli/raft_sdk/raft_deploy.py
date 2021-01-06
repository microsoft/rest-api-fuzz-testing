# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import json
import hashlib
import os
import pathlib
import sys
import string
import subprocess
import time
import uuid

from subprocess import PIPE

import requests
from .raft_common import RaftApiException, RestApiClient, RaftDefinitions

script_dir = os.path.dirname(os.path.abspath(__file__))
tmp_dir = os.path.join(script_dir, '.tmp')
if not os.path.isdir(tmp_dir):
    os.mkdir(tmp_dir)

dos2unix_file_types = [".sh", ".bash"]


class RaftAzCliException(Exception):
    def __init__(self, error_message, args):
        self.error_message = error_message
        self.az_args = args

    def __str__(self):
        return (f"args: {self.az_args}"
                f"{os.linesep}"
                f"std error: {self.error_message}")


def az(args):
    r = subprocess.run("az " + args, shell=True, stdout=PIPE, stderr=PIPE)
    stdout = r.stdout.decode()
    stderr = r.stderr.decode()

    if stderr and not stdout:
        raise RaftAzCliException(stderr, r.args)
    else:
        return stdout


def az_json(args):
    return json.loads(az(args))


def azure_function_keys(
        subscription_id, resource_group, function_app, function):
    uri = (f"/subscriptions/{subscription_id}"
           f"/resourceGroups/{resource_group}"
           f"/providers/Microsoft.Web/sites/{function_app}"
           f"/functions/{function}/listkeys?api-version=2018-11-01")
    return az_json(f'rest --method post --uri {uri} --output json')


class RaftServiceCLI():
    def __init__(self, context, defaults_path, secret=None):
        self.defaults_path = defaults_path
        self.context = context
        self.definitions = RaftDefinitions(self.context)

        if self.context['metricsOptIn'] is True:
            self.metrics_app_insights_key = (
                    '9d67f59d-4f44-475c-9363-d0ae7ea61e95')
        else:
            self.metrics_app_insights_key = ''
        self.site_hash = self.hash(
            self.definitions.subscription + self.definitions.deployment)

        if not secret:
            self.is_logged_in()

        az(f'account set --subscription {self.definitions.subscription}')

    def is_logged_in(self):
        az('ad signed-in-user show')

    def hash(self, txt):
        h = hashlib.md5()
        h.update(txt.encode('utf-8'))
        return h.hexdigest()

    def init_service_bus(self):
        sb = self.definitions.service_bus
        rg = self.definitions.resource_group
        print(f'Creating service bus {sb}')
        az('servicebus namespace create'
           f' --resource-group {rg}'
           f' --name {sb}'
           f' --location {self.context["region"]}'
           ' --sku Standard')

        print("Creating READ authorization rule")
        az('servicebus namespace authorization-rule create'
           f' --resource-group {rg}'
           f' --namespace-name {sb}'
           ' --name "Read" --rights Listen')
        read_sb_sas = az_json(
            'servicebus namespace'
            ' authorization-rule keys list'
            f' --resource-group {rg}'
            f' --namespace-name {sb}'
            ' --name "Read"')

        print("Creating SEND authorization rule")
        az('servicebus namespace authorization-rule create'
           f' --resource-group {rg}'
           f' --namespace-name {sb}'
           ' --name "Send" --rights Send')

        send_sb_sas = az_json(
            'servicebus namespace'
            ' authorization-rule keys list'
            f' --resource-group {rg}'
            f' --namespace-name {sb}'
            ' --name "Send"')

        print("Creating queue queues.CreateQueue")
        az('servicebus queue create'
           f' --resource-group {rg}'
           f' --namespace-name {sb}'
           f' --name {self.definitions.queues["create_queue"]}'
           ' --enable-session true')

        print("Creating queue queues.RESTler.DeleteQueue")
        az('servicebus queue create'
           f' --resource-group {rg}'
           f' --namespace-name {sb}'
           f' --name {self.definitions.queues["delete_queue"]}'
           ' --enable-session true')

        print("Creating topic queues.RESTler.JobEvents")
        az('servicebus topic create'
           f' --resource-group {rg}'
           f' --namespace-name {sb}'
           f' --name {self.definitions.queues["job_events"]}'
           ' --enable-ordering true')

        print("Creating SEND JobEvents authorization rule")
        az('servicebus topic authorization-rule create'
           f' --resource-group {rg}'
           f' --namespace-name {sb}'
           f' --topic-name {self.definitions.queues["job_events"]}'
           ' --name "Send-Events" --rights Send')

        send_events_sb_sas = az_json(
            'servicebus topic'
            ' authorization-rule keys list'
            f' --resource-group {rg}'
            f' --namespace-name {sb}'
            f' --topic-name {self.definitions.queues["job_events"]}'
            ' --name "Send-Events"')

        print("Creating job status events handler topic subscription")
        az('servicebus topic subscription create'
           f' --resource-group {rg}'
           f' --namespace-name {sb}'
           ' --topic-name'
           f' {self.definitions.queues["job_events"]}'
           ' --name "jobstatus-handler"')

        ''' Does not work due to bug in az CLI:
        https://github.com/Azure/azure-cli/issues/12369
        Log "Creating filter rule for job status events"
        az servicebus topic subscription rule create
            --resource-group $global:raft:resourceGroup
            --namespace-name $global:raft:serviceBus
            --topic-name $global:raft:queues.JobEvents
            --subscription-name "jobstatus-handler"
            --name "JobStatus"

        Log "Set filter properties for job status events"
        az servicebus topic subscription rule update
            --resource-group $global:raft:resourceGroup
            --namespace-name $global:raft:serviceBus
            --topic-name $global:raft:queues.JobEvents
            --subscription-name "jobstatus-handler"
            --name "JobStatus"
            --set "correlationFilter.properties.jobstatus=true"
        '''

        print("Creating web hooks handler topic subscription")
        az('servicebus topic subscription create'
           f' --resource-group {rg}'
           f' --namespace-name {sb}'
           ' --topic-name'
           f' {self.definitions.queues["job_events"]}'
           ' --name "webhooks-handler"')

        '''
        Does not work due to bug in az CLI:
        https://github.com/Azure/azure-cli/issues/12369
        Log "Set filter properties for job status events"
        az servicebus topic subscription rule create
            --resource-group $global:raft:resourceGroup
            --namespace-name $global:raft:serviceBus
            --topic-name $global:raft:queues.JobEvents
            --subscription-name "webhooks-handler"
            --name "Webhooks"

        az servicebus topic subscription rule update
            --resource-group $global:raft:resourceGroup
            --namespace-name $global:raft:serviceBus
            --topic-name $global:raft:queues.JobEvents
            --subscription-name "jobstatus-handler"
            --name "Webhooks" `
            --set "correlationFilter.properties.webhooks=true"
        '''
        return {
            'read_sb_sas': read_sb_sas['primaryConnectionString'],
            'send_sb_sas': send_sb_sas['primaryConnectionString'],
            'send_events_sb_sas': send_events_sb_sas['primaryConnectionString']
        }

    def init_key_vault(self):
        print(f'Initializing Key Vault {self.definitions.key_vault}')
        az_json('keyvault create'
                f' --location {self.context["region"]}'
                f' --enable-rbac-authorization true'
                f' --name {self.definitions.key_vault}'
                ' --resource-group'
                f' {self.definitions.resource_group}')

    def assign_keyvault_roles(self, sp_app_id):
        print('Assigning Key Vault roles')

        scope = (f'/subscriptions/{self.definitions.subscription}'
                 f'/resourceGroups/{self.definitions.resource_group}'
                 '/providers/Microsoft.KeyVault'
                 f'/vaults/{self.definitions.key_vault}')

        account = az_json('ad signed-in-user show')
        if 'userPrincipalName' in account:
            az('role assignment create'
               f' --assignee {account["userPrincipalName"]}'
               ' --role "Key Vault Secrets Officer (preview)"'
               f' --scope "{scope}"')

        az('role assignment create'
           f' --assignee {sp_app_id}'
           ' --role "Key Vault Secrets User (preview)"'
           f' --scope "{scope}"')

    def assign_resource_group_roles(self, sp_app_id):
        print('Assigning Resource Group roles')
        try:
            scope = (f'/subscriptions/{self.definitions.subscription}'
                     f'/resourceGroups/{self.definitions.resource_group}')
            az('role assignment create'
               f' --assignee {sp_app_id}'
               ' --role contributor'
               f' --scope "{scope}"')
        except RaftAzCliException as ex:
            not_owner = "does not have authorization to perform action"
            try_again = "does not exist in the directory"
            if not_owner in ex.error_message:
                raise Exception('You must be owner of the'
                                ' subscription in order to'
                                ' deploy the service')
            if try_again in ex.error_message:
                print('Service Principal is not in AD yet. Trying again...')
                time.sleep(3.0)
                self.assign_resource_group_roles(sp_app_id)

    def init_app_insights(self):
        if self.context['useAppInsights']:
            print(f'Initializing AppInsights {self.definitions.app_insights}')

            app_insights_installed = False
            for extension in az_json('extension list'):
                if extension['name'] == 'application-insights':
                    app_insights_installed = True

            if not app_insights_installed:
                p = ("The installed extension"
                     " 'application-insights' is in preview.")
                try:
                    print('Installing application insights extension')
                    az('extension add --name application-insights')
                except RaftAzCliException as ex:
                    if ex.error_message.strip() == p:
                        pass
                    else:
                        raise ex

            ai = az_json('monitor app-insights component create'
                         ' --resource-group'
                         f' {self.definitions.resource_group}'
                         f' --app {self.definitions.app_insights}'
                         f' --location {self.context["region"]}')
            return {
                'app_id': ai['appId'],
                'instrumentation_key': ai['instrumentationKey']
            }
        else:
            print('Skipping AppInsights deployment')
            return {
                'app_id': '',
                'instrumentation_key': ''
            }

    def init_app_service_plan(self, sku):
        print(f"Creating app service plan {self.definitions.asp}")
        az('appservice plan create'
           f' --name {self.definitions.asp}'
           f' --resource-group {self.definitions.resource_group}'
           f' --is-linux --location {self.context["region"]}'
           f' --number-of-workers 2 --sku {sku}')

    def storage_account_connection_string(self, storage_account):
        connection_string = az_json(
            'storage account show-connection-string'
            f' --resource-group {self.definitions.resource_group}'
            f' --name "{storage_account}"'
            ' --query connectionString')
        return connection_string

    def create_storage_connection_string_with_sas(self, storage_account):
        print('Storage connection string with SAS'
              f'{storage_account}')

        connection_string = self.storage_account_connection_string(
                                storage_account)

        sas_url = az_json(
            'storage account generate-sas'
            ' --permissions acdlpruw --resource-types sco'
            ' --expiry 2050-01-01'
            ' --services t --https-only'
            f' --account-name "{storage_account}"'
            f' --connection-string "{connection_string}"')

        return (f"TableEndpoint=https://{storage_account}"
                f".table.core.windows.net/;SharedAccessSignature={sas_url}")

    def container_image_name(self, registry_image_name):
        return (
             f"{self.context['registry']}"
             "/restapifuzztesting"
             f"/{registry_image_name}:{self.definitions.container_tag}")

    def init_web_app_service(
            self, service_name,
            registry_image_name, registry_username, registry_password):
        print('Creating')
        print(f'    {registry_image_name}')

        container_image_name = self.container_image_name(registry_image_name)

        if registry_username and registry_password:
            app_service = az(
                'webapp create'
                f' --name {service_name}'
                f' --resource-group {self.definitions.resource_group}'
                f' --plan {self.definitions.asp}'
                f' --docker-registry-server-user {registry_username}'
                f' --docker-registry-server-password {registry_password}'
                f' --deployment-container-image-name {container_image_name}')
        else:
            app_service = az(
                'webapp create'
                f' --name {service_name}'
                f' --resource-group {self.definitions.resource_group}'
                f' --plan {self.definitions.asp}'
                f' --deployment-container-image-name {container_image_name}')

        print('    client-affinity & https')

        az('webapp update'
           f' --name {service_name}'
           f' --resource-group {self.definitions.resource_group}'
           ' --client-affinity-enabled false --https-only true')

    def init_api_app_service(
            self, service_bus, app_insights,
            storage_connection_string_with_sas, sp,
            container_registry_username, container_registry_password,
            utils_file_share):

        self.init_web_app_service(
            service_name=self.definitions.api_service_webapp,
            registry_image_name='apiservice',
            registry_username=container_registry_username,
            registry_password=container_registry_password)

        rg = self.definitions.resource_group
        event_domain = self.definitions.event_domain
        domain = az_json('eventgrid domain show'
                         f' --resource-group {rg} --name {event_domain}')
        keys = az_json('eventgrid domain key list'
                       f' --resource-group {rg} --name {event_domain}')

        endpoint = domain['endpoint']
        key = keys['key1']

        print('    application settings')

        web_app_settings = [
                {
                    'name': "RAFT_METRICS_APP_INSIGHTS_KEY",
                    'slotSetting': False,
                    'value': self.metrics_app_insights_key
                },
                {
                    'name': "RAFT_SITE_HASH",
                    'slotSetting': False,
                    'value': self.site_hash
                },
                {
                    'name': "RAFT_SERVICEBUS_SEND_CONNECTION_STRING",
                    'slotSetting': False,
                    'value': service_bus['send_sb_sas']
                },
                {
                    'name': ("RAFT_SERVICEBUS_AGENT_"
                             "SEND_EVENTS_CONNECTION_STRING"),
                    'slotSetting': False,
                    'value': service_bus['send_events_sb_sas']
                },
                {
                    'name': "RAFT_STORAGE_TABLE_CONNECTION_STRING",
                    'slotSetting': False,
                    'value': storage_connection_string_with_sas
                },
                {
                    'name': "APPINSIGHTS_INSTRUMENTATIONKEY",
                    'slotSetting': False,
                    'value': app_insights['instrumentation_key']
                },
                {
                    'name': "RAFT_SERVICE_PRINCIPAL_CLIENT_ID",
                    'slotSetting': False,
                    'value': sp['appId']
                },
                {
                    'name': "RAFT_SERVICE_PRINCIPAL_CLIENT_SECRET",
                    'slotSetting': False,
                    'value': sp['password']
                },
                {
                    'name': "RAFT_SERVICE_PRINCIPAL_TENANT_ID",
                    'slotSetting': False,
                    'value': sp['tenant']
                },
                {
                    'name': "RAFT_RESOURCE_GROUP_NAME",
                    'slotSetting': False,
                    'value': rg
                },
                {
                    'name': "RAFT_EVENT_DOMAIN",
                    'slotSetting': False,
                    'value': event_domain
                },
                {
                    'name': "RAFT_UTILS_FILESHARE",
                    'slotSetting': False,
                    'value': utils_file_share
                },
                {
                    'name': "RAFT_UTILS_SAS",
                    'slotSetting': False,
                    'value':  self.storage_account_connection_string(
                                self.definitions.storage_utils)
                },
                {
                    'name': "RAFT_SERVICE_PRINCIPAL_SUBSCRIPTION_ID",
                    'slotSetting': False,
                    'value': self.definitions.subscription
                }
            ]

        settings_path = os.path.join(script_dir, 'webAppSettings.json')
        with open(settings_path, 'w') as settings:
            json.dump(web_app_settings, settings)

        az('webapp config appsettings set'
           f' --resource-group {rg}'
           f' --name {self.definitions.api_service_webapp}'
           f' --settings "@{settings_path}"')
        os.remove(settings_path)

        print("    enabling deployment updates"
              " of web-app from Container Gallery")
        az('webapp deployment container config --enable-cd true'
           f' --name {self.definitions.api_service_webapp}'
           f' --resource-group {rg}')

    def init_storage_account(self, storage_account):
        print(f'Creating Storage Account: {storage_account}')
        az('storage account create'
           f' --name {storage_account}'
           f' --resource-group {self.definitions.resource_group}'
           f' --https-only true --location {self.context["region"]}')

    def init_file_storage(self, storage_account):
        print(f'Creating FileShare Storage Account: {storage_account}')
        az('storage account create'
           f' --name {storage_account}'
           f' --resource-group {self.definitions.resource_group}'
           # Uncomment to add more performant storage account.
           # Useful if RAFT runs lots of jobs in parallel
           # ' --sku Premium_ZRS --kind FileStorage'
           f' --https-only true --location {self.context["region"]}')

    def init_service_principal(
            self, name, subscription_id,
            resource_group, assign_roles):
        print('Creating')
        print(f'    service principal {name}')

        all_sps = az_json(f'ad sp list --display-name {name}')

        if len(all_sps) > 0:
            existing_sp = all_sps[0]
        else:
            existing_sp = None

        if existing_sp:
            app_id = existing_sp['appId']
            print(f'    using Existing Service Principal with AppID: {app_id}')

        else:
            # localhost is needed as a reply-url
            # if the user is not connected via a VPN connection
            login_url = (
                'https://login.microsoftonline.com'
                '/common/oauth2/nativeclient')

            while(True):
                try:
                    app = az_json(
                        'ad app create'
                        f' --display-name "{name}"'
                        ' --native-app true --reply-urls'
                        f' "{login_url}"'
                        ' "http://localhost"')
                    break
                except RaftAzCliException as ex:
                    print(f"Got: {ex.error_message}")
                    print("Trying again...")
                    time.sleep(3.0)

            while not existing_sp:
                print('    waiting for app registration to appear'
                      f' in AD with App ID: {app["appId"]}')
                try:
                    existing_sp = az(f'ad app show --id {app["appId"]}')
                except Exception:
                    time.sleep(5)

            sp = az_json(f'ad sp create --id {app["appId"]}')
            print(f'    service Principal AppID: {app["appId"]}')
            app_id = app['appId']
            new_sp = []
            while len(new_sp) <= 0:
                print('    waiting for service principal to appear'
                      f' in AD with App ID: {app["appId"]}')
                try:
                    new_sp = az_json(f'ad sp list --display-name {name}')
                except Exception:
                    time.sleep(5)

        for assign in assign_roles:
            assign(app_id)

        az(f'ad sp update --id {app_id} --set appRoleAssignmentRequired=false')
        sp = az_json(f'ad sp credential reset --append --name {app_id}')

        # Add command will write an informational message to stderr.
        # The output to stderr should not be treated as error in this case.
        try:
            print("    adding Read permission")
            user_read_permission = "e1fe6dd8-ba31-4d61-89e7-88639da4683d"
            read = az('ad app permission add'
                      ' --api 00000003-0000-0000-c000-000000000000'
                      f' --id {app_id}'
                      f' --api-permissions "{user_read_permission}=Scope"')
        except RaftAzCliException as ex:
            if ex.error_message.startswith(
                                    'Invoking "az ad app permission grant'
                                    f' --id {app_id}'
                                    ' --api'
                                    ' 00000003-0000-0000-c000-000000000000"'
                                    ' is needed to make the change effective'):

                pass
            else:
                raise ex

        # The grant command fails, because of some bug.
        # The command actually succeeds and the command is needed.
        # So we don't use the az-z function because
        # we want to continue even though it fails.
        try:
            print('    granting permissions')
            az('ad app permission grant'
               f' --id {app_id}'
               ' --api 00000003-0000-0000-c000-000000000000')
        except RaftAzCliException as ex:
            if ex.error_message.startswith("Operation failed"
                                           " with status: 'Bad Request'."):
                pass
            elif existing_sp:
                pass
            else:
                raise ex

        return sp

    def init_test_infra(
            self, service_bus, app_insights,
            storage_connection_string_with_sas,
            sp, container_registry_username, container_registry_password):

        print('Creating Storage Account: '
              f'{self.definitions.test_infra_storage}')
        az('storage account create'
           f' --name {self.definitions.test_infra_storage}'
           f' --resource-group {self.definitions.resource_group}'
           f' --https-only true --location {self.context["region"]}')

        print('Creating')
        print(f'    function resource {self.definitions.test_infra}')
        rg = self.definitions.resource_group

        storage_account = self.definitions.test_infra_storage

        if self.context['useAppInsights']:
            ai = f' --app-insights {self.definitions.app_insights}'
        else:
            ai = ' --disable-app-insights true'

        az_function = az('functionapp create'
                         f' --name {self.definitions.test_infra}'
                         f' --storage-account {storage_account}'
                         f' --resource-group {rg}'
                         f' --plan "{self.definitions.asp}"'
                         ' --functions-version 3'
                         ' --os-type Linux --runtime dotnet'
                         f' {ai}')

        connection_string = az_json(
            'storage account show-connection-string'
            f' --resource-group {rg}'
            f' --name {storage_account}'
            ' --query connectionString'
            ' --output json')

        test_infra_settings = [
                {
                    'name': "AzureWebJobsStorage",
                    'slotSetting': False,
                    'value': connection_string
                },
                {
                    'name': "RAFT_APPINSIGHTS",
                    'slotSetting': False,
                    'value': app_insights['instrumentation_key']
                },
                {
                    'name': "RAFT_STORAGE_TABLE_CONNECTION_STRING",
                    'slotSetting': False,
                    'value': storage_connection_string_with_sas
                }
        ]

        settings_path = os.path.join(script_dir, 'testInfraSettings.json')
        with open(settings_path, 'w') as settings:
            json.dump(test_infra_settings, settings)

        print("    settings")
        az('functionapp config appsettings set'
           f' --name {self.definitions.test_infra}'
           f' --resource-group {rg}'
           f' --settings "@{settings_path}"')

        os.remove(settings_path)

        print("    updating container registry settings")
        container_image_name = self.container_image_name('test-infra')

        if container_registry_password and container_registry_username:
            functionAppConfig = az(
                'functionapp config container set'
                ' --docker-registry-server-url'
                f' "{self.context["registry"]}"'
                f' --docker-registry-server-user {container_registry_username}'
                ' --docker-registry-server-password'
                f' {container_registry_password}'
                f' --name {self.definitions.test_infra}'
                f' --resource-group {rg}'
                f' --docker-custom-image-name "{container_image_name}"')
        else:
            functionAppConfig = az(
                'functionapp config container set'
                ' --docker-registry-server-url'
                f' "{self.context["registry"]}"'
                f' --name {self.definitions.test_infra}'
                f' --resource-group {rg}'
                f' --docker-custom-image-name "{container_image_name}"')

        print("    Enabling deployment updates of test-infra"
              "Azure Function from Container Gallery")
        az('functionapp deployment container config --enable-cd true'
           f' --name {self.definitions.test_infra}'
           f' --resource-group {rg}')

    def init_orchestrator(
            self, service_bus, app_insights,
            storage_connection_string_with_sas,
            sp, container_registry_username, container_registry_password,
            utils_file_share):
        print('Creating')
        print(f'    function resource {self.definitions.orchestrator}')
        rg = self.definitions.resource_group
        storage_account = self.definitions.storage_utils

        if self.context['useAppInsights']:
            ai = f' --app-insights {self.definitions.app_insights}'
        else:
            ai = ' --disable-app-insights true'

        az_function = az('functionapp create'
                         f' --name {self.definitions.orchestrator}'
                         f' --storage-account {storage_account}'
                         f' --resource-group {rg}'
                         f' --plan "{self.definitions.asp}"'
                         ' --functions-version 3'
                         ' --os-type Linux --runtime dotnet'
                         f' {ai}')

        connection_string = az_json(
            'storage account show-connection-string'
            f' --resource-group {rg}'
            f' --name {storage_account}'
            ' --query connectionString'
            ' --output json')

        domain = az_json(
            'eventgrid domain show'
            f' --resource-group {rg}'
            f' --name {self.definitions.event_domain}')

        keys = az_json(
            'eventgrid domain key list'
            f' --resource-group {rg}'
            f' --name {self.definitions.event_domain}')

        endpoint = domain['endpoint']
        key = keys['key1']

        orchestrator_settings = [
                {
                    'name': "AzureWebJobsStorage",
                    'slotSetting': False,
                    'value': connection_string
                },
                {
                    'name': "AzureWebJobsServiceBus",
                    'slotSetting': False,
                    'value': service_bus['read_sb_sas']
                },
                {
                    'name': "RAFT_KEY_VAULT",
                    'slotSetting': False,
                    'value': self.definitions.key_vault
                },
                {
                    'name': "RAFT_SERVICE_PRINCIPAL_CLIENT_ID",
                    'slotSetting': False,
                    'value': sp['appId']
                },
                {
                    'name': "RAFT_SERVICE_PRINCIPAL_CLIENT_SECRET",
                    'slotSetting': False,
                    'value': sp['password']
                },
                {
                    'name': "RAFT_SERVICE_PRINCIPAL_TENANT_ID",
                    'slotSetting': False,
                    'value': sp['tenant']
                },
                {
                    'name': "RAFT_SERVICE_PRINCIPAL_SUBSCRIPTION_ID",
                    'slotSetting': False,
                    'value': self.definitions.subscription
                },
                {
                    'name': "RAFT_CONTAINER_RUN_RESOURCE_GROUP",
                    'slotSetting': False,
                    'value': self.definitions.resource_group
                },
                {
                    'name': "RAFT_UTILS_STORAGE",
                    'slotSetting': False,
                    'value': self.definitions.storage_utils
                },
                {
                    'name': "RAFT_RESULTS_STORAGE",
                    'slotSetting': False,
                    'value': self.definitions.storage_results
                },
                {
                    'name': "RAFT_UTILS_FILESHARE",
                    'slotSetting': False,
                    'value': utils_file_share
                },
                {
                    'name': "RAFT_REPORT_JOB_STATUS",
                    'slotSetting': False,
                    'value': service_bus['send_sb_sas']
                },
                {
                    'name': ("RAFT_SERVICEBUS_AGENT_"
                             "SEND_EVENTS_CONNECTION_STRING"),
                    'slotSetting': False,
                    'value': service_bus['send_events_sb_sas']
                },
                {
                    'name': "RAFT_APPINSIGHTS",
                    'slotSetting': False,
                    'value': app_insights['instrumentation_key']
                },
                {
                    'name': "RAFT_METRICS_APP_INSIGHTS_KEY",
                    'slotSetting': False,
                    'value': self.metrics_app_insights_key
                },
                {
                    'name': "RAFT_SITE_HASH",
                    'slotSetting': False,
                    'value': self.site_hash
                },
                {
                    'name': "RAFT_EVENT_GRID_ENDPOINT",
                    'slotSetting': False,
                    'value': endpoint
                },
                {
                    'name': "RAFT_EVENT_GRID_KEY",
                    'slotSetting': False,
                    'value': key
                },
                {
                    'name': "RAFT_STORAGE_TABLE_CONNECTION_STRING",
                    'slotSetting': False,
                    'value': storage_connection_string_with_sas
                }
            ]

        settings_path = os.path.join(script_dir, 'orchestratorSettings.json')
        with open(settings_path, 'w') as settings:
            json.dump(orchestrator_settings, settings)

        print("    settings")
        az('functionapp config appsettings set'
           f' --name {self.definitions.orchestrator}'
           f' --resource-group {rg}'
           f' --settings "@{settings_path}"')

        os.remove(settings_path)

        print("    updating container registry settings")
        container_image_name = self.container_image_name('orchestrator')

        if container_registry_username and container_registry_password:
            functionAppConfig = az(
                'functionapp config container set'
                ' --docker-registry-server-url'
                f' "{self.context["registry"]}"'
                f' --docker-registry-server-user {container_registry_username}'
                ' --docker-registry-server-password'
                f' {container_registry_password}'
                f' --name {self.definitions.orchestrator}'
                f' --resource-group {rg}'
                f' --docker-custom-image-name "{container_image_name}"')
        else:
            functionAppConfig = az(
                'functionapp config container set'
                ' --docker-registry-server-url'
                f' "{self.context["registry"]}"'
                f' --name {self.definitions.orchestrator}'
                f' --resource-group {rg}'
                f' --docker-custom-image-name "{container_image_name}"')

        print("    Enabling deployment updates of orchestrator"
              "Azure Function from Container Gallery")
        az('functionapp deployment container config --enable-cd true'
           f' --name {self.definitions.orchestrator}'
           f' --resource-group {rg}')

    def init_event_grid_domain(self):
        print('Creating')
        event_domain = self.definitions.event_domain
        print(f'    event domain {event_domain}')
        az('eventgrid domain create'
           f' --location {self.context["region"]}'
           f' --name {event_domain}'
           f' --resource-group {self.definitions.resource_group}')

    def add_resource_providers(self):
        print('Enabling container providers')
        az("provider register"
           " --namespace Microsoft.ContainerRegistry"
           f" --subscription {self.definitions.subscription} --wait")

        az("provider register"
           " --namespace Microsoft.ContainerInstance"
           f" --subscription {self.definitions.subscription} --wait")

    def update_deployment_context(self):
        print(f'Updating deployment context: {self.defaults_path}')
        with open(self.defaults_path, 'w') as d:
            defaults = self.context.copy()
            defaults.pop('secret')
            json.dump(defaults, d, indent=4)

    def test_az_version(self):
        supported_versions = ['2.15.0', '2.16.0', '2.17.1']
        # az sometimes reports the version with an asterisk.
        # Perhaps when a new version of the CLI is available.
        tested_az_cli_versions = (
            supported_versions +
            list(map(lambda x: f"{x} *", supported_versions)))
        current_version = az('--version')

        for line in current_version.splitlines():
            if 'azure-cli' in line:
                v = line[len('azure-cli'):].strip()
                if v not in tested_az_cli_versions:
                    print("---------------------------------------------")
                    print("WARNING: RUNNING DEPLOYMENT WITH VERSION OF"
                          f" AZ-CLI {v}. DEPLOYING SERVICE WAS NOT TESTED"
                          " WITH THIS VERSION.")
                    print(f"IF DEPLOYMENT FAILS. PLEASE USE ONE OF THE"
                          f" FOLLOWING AZ-CLI VERSIONS {supported_versions}")
                    print("---------------------------------------------")
                else:
                    break

    def dos2unix(self, file_path):
        print(f'Converting dos2unix {file_path}')
        dos_file_path = file_path + ".dos"
        os.rename(file_path, dos_file_path)

        with open(dos_file_path, 'rb') as dos_file:
            file_contents = dos_file.read()

        with open(file_path, 'wb') as unix_file:
            for line in file_contents.splitlines():
                unix_file.write(line + b'\n')
        os.remove(dos_file_path)

    def convert_dir_dos2unix(self, folder_path):
        for root_folder_path, dirs, files in os.walk(folder_path):
            for file_name in files:
                file_path = os.path.join(root_folder_path, file_name)
                if pathlib.Path(file_path).suffix in dos2unix_file_types:
                    self.dos2unix(file_path)

    def upload_utils(self, file_share, custom_tools=None):
        utils = os.path.join(f'{script_dir}', '..', 'raft-tools')
        self.convert_dir_dos2unix(utils)

        connection_string = self.storage_account_connection_string(
                                self.definitions.storage_utils)

        exists = az_json('storage share exists'
                         f' --name {file_share}'
                         f' --connection-string "{connection_string}"')

        if exists['exists'] is True:
            print(f'Uploading tools to an existing share {file_share}')
        else:
            print(f'Creating new file share {file_share}')
            share = az_json('storage share create'
                            f' --name {file_share}'
                            f' --connection-string "{connection_string}"')

        upload = az('storage file upload-batch'
                    f' --connection-string "{connection_string}"'
                    f' --destination {file_share}'
                    f' --source {utils}'
                    f' --pattern "*"'
                    ' --validate-content')

        if custom_tools:
            print(f'Uploading custom tools {custom_tools}')
            az('storage file upload-batch'
               f' --connection-string "{connection_string}"'
               f' --destination {file_share}'
               f' --source {custom_tools}'
               f' --pattern "*"'
               ' --validate-content')

        print('Updating orchestrator utils file share')
        az('functionapp config appsettings set'
            f' --name {self.definitions.orchestrator}'
            f' --resource-group {self.definitions.resource_group}'
            f' --settings "RAFT_UTILS_FILESHARE={file_share}"')

        print('Updating webapp utils file share')
        az('functionapp config appsettings set'
            f' --name {self.definitions.api_service_webapp}'
            f' --resource-group {self.definitions.resource_group}'
            f' --settings "RAFT_UTILS_FILESHARE={file_share}"')
        return

    def deploy(self, sku, skip_sp_deployment):
        if skip_sp_deployment and not (
                self.context.get('clientId') and
                self.context.get('tenantId') and
                self.context.get('secret')):
            raise Exception('Only can skip Service Principal'
                            'deployment when redeploying existing'
                            'service and passing secret'
                            'as deployment parameter')

        self.test_az_version()

        # if opt-out-from-metrics is not present, then assume that user
        # is opt-in and patch the defaults.json with that
        print(f'Creating deployment with hash {self.site_hash}')
        az("group create"
           f" --name {self.definitions.resource_group}"
           f" --location {self.context['region']}")
        print(f"Deployment Resource Group: {self.definitions.resource_group}")

        self.init_key_vault()

        service_principal = {}
        if skip_sp_deployment:
            print('Skipping Service Principal deployment...')
            service_principal['appId'] = self.context['clientId']
            service_principal['tenant'] = self.context['tenantId']
            service_principal['password'] = self.context['secret']
        else:
            service_principal = self.init_service_principal(
                                    self.definitions.deployment + "-raft",
                                    self.definitions.subscription,
                                    self.definitions.resource_group,
                                    [self.assign_resource_group_roles,
                                     self.assign_keyvault_roles])

            # add service principal information to the keyvault
            auth = {
                'client': service_principal['appId'],
                'tenant': service_principal['tenant'],
                'secret': service_principal['password']
            }
            sp_path = os.path.join(tmp_dir, 'sp.json')
            with open(sp_path, 'w') as sp_json:
                json.dump(auth, sp_json)

            az('keyvault secret set'
                f' --description "{self.definitions.deployment}'
                ' Service Principal authentication credentials"'
                f" --file {sp_path}"
                ' --name RaftServicePrincipal'
                f' --vault-name {self.definitions.key_vault}')

            try:
                os.remove(sp_path)
            except OSError:
                pass

        if self.context.get('isPrivateRegistry'):
            if 'getToken' in self.context:
                print('Getting container registry token')
                token_name = f"token-{self.site_hash}"
                response = requests.get(
                    f"{self.context['getToken']}&name={token_name}")
                if response.ok:
                    content = json.loads(response.text)
                    container_registry_username = content['tokenName']
                    container_registry_password = content['password']
                else:
                    raise RaftApiException(response.text, response.status_code)
            else:
                container_registry_username = service_principal['appId']
                container_registry_password = service_principal['password']
        else:
            container_registry_username = None
            container_registry_password = None

        sb_connection_strings = self.init_service_bus()
        app_insights = self.init_app_insights()

        self.init_storage_account(self.definitions.storage_utils)
        storage_connection_string_with_sas = (
            self.create_storage_connection_string_with_sas(
                self.definitions.storage_utils))
        self.init_file_storage(self.definitions.storage_results)

        self.init_event_grid_domain()

        self.init_app_service_plan(sku)
        tools_file_share = f'{uuid.uuid4()}'

        self.init_api_app_service(
            sb_connection_strings,
            app_insights,
            storage_connection_string_with_sas,
            service_principal,
            container_registry_username,
            container_registry_password,
            tools_file_share)

        self.init_orchestrator(
            sb_connection_strings,
            app_insights,
            storage_connection_string_with_sas,
            service_principal,
            container_registry_username,
            container_registry_password,
            tools_file_share)

        self.upload_utils(tools_file_share)

        if self.context.get('isDevelop') and not skip_sp_deployment:
            self.init_test_infra(
                sb_connection_strings,
                app_insights,
                storage_connection_string_with_sas,
                service_principal,
                container_registry_username,
                container_registry_password)

        self.add_resource_providers()

        self.context['clientId'] = service_principal['appId']
        self.context['tenantId'] = service_principal['tenant']
        self.context['secret'] = service_principal['password']

        self.update_deployment_context()
        print('Waiting for service to start'
              f' {self.definitions.api_service_webapp}')
        self.wait_for_service_to_start()
        print('Service started')

    def restart(self):
        try:
            pre_restart_info = self.service_info()
        except RaftApiException:
            pre_restart_info = None

        print(f'Restarting {self.definitions.orchestrator}')
        az('functionapp restart'
           f' --name {self.definitions.orchestrator}'
           f' --resource-group {self.definitions.resource_group}')

        print(f'Restarting {self.definitions.api_service_webapp}')
        az('webapp restart'
           f' --name {self.definitions.api_service_webapp}'
           f' --resource-group {self.definitions.resource_group}')

        if self.context.get('isDevelop'):
            print(f'Restarting {self.definitions.test_infra}')
            az('functionapp restart'
               f' --name {self.definitions.test_infra}'
               f' --resource-group {self.definitions.resource_group}')

        sys.stdout.write('Waiting for service to start')
        self.wait_for_service_to_start(pre_restart_info)
        print()
        print('Done')

    def service_info(self):
        raft_api = RestApiClient(
                    self.definitions.endpoint,
                    self.context['clientId'],
                    self.context['tenantId'],
                    self.context.get('secret'))
        info = raft_api.get('/info')
        if info.ok:
            return json.loads(info.text)
        else:
            raise RaftApiException(info.text, info.status_code)

    def wait_for_service_to_start(self, old_info=None):
        new_info = old_info
        while True:
            try:
                new_info = self.service_info()
                if old_info is None:
                    return

                new_t = new_info['serviceStartTime']
                old_t = old_info['serviceStartTime']
                if new_t != old_t:
                    return
            except RaftApiException as ex:
                print('Failed while waiting for service'
                      f' to start due to {ex.message}')
                raise ex
            except Exception:
                new_info = old_info
                time.sleep(3)
                sys.stdout.write('.')
                sys.stdout.flush()
