# FAQ

**I see 404's for GET /robots933456.txt in my Application Insights log**
</br>We deploy containers to the webapp. See this [explanation](https://docs.microsoft.com/en-us/azure/app-service/containers/configure-custom-container#robots933456-in-logs).

**Why use file shares?**
</br>File shares can be mounted to the containers running tools. This provides an easy way to share data.

The use of file shares makes it easy to upload your custom swagger files, and authentication configurations files. 
You can also view real time log changes and results in the files share during the job run, 
and mount these file shares to your computer for a better user experience. 

To mount the file share, use the connect button on the azure portal.

![Connect File Share Image](images/mount_file_share.jpg)
