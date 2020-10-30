# Swagger

The RAFT REST API is represented in a UI that can be viewed at your https://\<raft-service-endpoint\>/swagger. 
This UI also will display the schema's for the various supported methods. 

Listed below is a static view of the swagger methods. 

<!--
This HTML was generated from https://editor.swagger.io.
Once the editor has the swagger doc, use the "Generate Client" menu item to save to html (not html2).
You will only need the index.html file that is produced.
 1. For the HTML to display correctly in markdown you must remove lines that start with 4 blank characters.
 2. remove all the "Up" references. 
 3. Remove "nicknames" i.e. (<span class="nickname">jobsV1JobIdPost</span>)
 4. There are a number of XML comments that use " to get quotes to show correctly in the UI. Clean these up to display correctly. 
-->

<h2>Access</h2>

<h2><a name="__Methods">Methods</a></h2>
[ Jump to <a href="#__Models">Models</a> ]

<h3>Table of Contents </h3>
<div class="method-summary"></div>
<h4><a href="#Info">Info</a></h4>
<ul>
<li><a href="#infoGet"><code><span class="http-method">get</span> /info</code></a></li>
<li><a href="#rootGet"><code><span class="http-method">get</span> /</code></a></li>
</ul>
<h4><a href="#Jobs">Jobs</a></h4>
<ul>
<li><a href="#jobsV1Get"><code><span class="http-method">get</span> /jobs/v1</code></a></li>
<li><a href="#jobsV1JobIdDelete"><code><span class="http-method">delete</span> /jobs/v1/{jobId}</code></a></li>
<li><a href="#jobsV1JobIdGet"><code><span class="http-method">get</span> /jobs/v1/{jobId}</code></a></li>
<li><a href="#jobsV1JobIdPost"><code><span class="http-method">post</span> /jobs/v1/{jobId}</code></a></li>
<li><a href="#jobsV1Post"><code><span class="http-method">post</span> /jobs/v1</code></a></li>
</ul>
<h4><a href="#Webhooks">Webhooks</a></h4>
<ul>
<li><a href="#webhooksV1EventsGet"><code><span class="http-method">get</span> /webhooks/v1/events</code></a></li>
<li><a href="#webhooksV1Get"><code><span class="http-method">get</span> /webhooks/v1</code></a></li>
<li><a href="#webhooksV1Post"><code><span class="http-method">post</span> /webhooks/v1</code></a></li>
<li><a href="#webhooksV1Put"><code><span class="http-method">put</span> /webhooks/v1</code></a></li>
<li><a href="#webhooksV1TestWebhookNameEventNamePut"><code><span class="http-method">put</span> /webhooks/v1/test/{webhookName}/{eventName}</code></a></li>
<li><a href="#webhooksV1WebhookNameEventNameDelete"><code><span class="http-method">delete</span> /webhooks/v1/{webhookName}/{eventName}</code></a></li>
</ul>

<h1><a name="Info">Info</a></h1>
<div class="method"><a name="infoGet"></a>
  <div class="method-path">
  
  <pre class="get"><code class="huge"><span class="http-method">get</span> /info</code></pre></div>
  <div class="method-summary">Returns information about the system. </div>
  <div class="method-notes">This is an unauthenticated method.
Sample response:
{
"version": "1.0.0.0",
"serviceStartTime": "2020-07-02T15:28:57.0093727+00:00"
}</div>








<!--Todo: process Response Object and its headers, schema, examples -->



<h3 class="field-label">Responses</h3>
<h4 class="field-label">200</h4>
Returns success.
  <a href="#"></a>
</div> <!-- method -->
<hr/>
<div class="method"><a name="rootGet"></a>
  <div class="method-path">
  
  <pre class="get"><code class="huge"><span class="http-method">get</span> /</code></pre></div>
  <div class="method-summary">Test to see if service is up </div>
  <div class="method-notes">This is an unauthenticated method which returns no data.</div>








  <!--Todo: process Response Object and its headers, schema, examples -->



  <h3 class="field-label">Responses</h3>
  <h4 class="field-label">200</h4>
  Returns success if the service is running.
      <a href="#"></a>
</div> <!-- method -->
<hr/>
<h1><a name="Jobs">Jobs</a></h1>
<div class="method"><a name="jobsV1Get"></a>
  <div class="method-path">
  
  <pre class="get"><code class="huge"><span class="http-method">get</span> /jobs/v1</code></pre></div>
  <div class="method-summary">Returns in an array status for all jobs over the last 24 hours. </div>
  <div class="method-notes">The default timespan is over the last 24 hours. Use the query string timeSpanFilter&#x3D;"TimeSpan" to specify a different interval.
Use a time format that can be parsed as a TimeSpan data type.
If no data is found the result will be an empty array.</div>





<h3 class="field-label">Query parameters</h3>
<div class="field-items">
  <div class="param">timeSpanFilter (optional)</div>
  
        <div class="param-desc"><span class="param-type">Query Parameter</span> &mdash; A string which is interpreted as a TimeSpan format: date-span</div>    </div>  <!-- field-items -->



<!--Todo: process Response Object and its headers, schema, examples -->



 <h3 class="field-label">Responses</h3>
 <h4 class="field-label">200</h4>
 Returns the job status data
<a href="#"></a>
</div> <!-- method -->
<hr/>
<div class="method"><a name="jobsV1JobIdDelete"></a>
  <div class="method-path">
  
  <pre class="delete"><code class="huge"><span class="http-method">delete</span> /jobs/v1/{jobId}</code></pre></div>
  <div class="method-summary">Deletes a job </div>
  <div class="method-notes"></div>

  <h3 class="field-label">Path parameters</h3>
  <div class="field-items">
    <div class="param">jobId (required)</div>
    
          <div class="param-desc"><span class="param-type">Path Parameter</span> &mdash; The id which refers to an existing job </div>    </div>  <!-- field-items -->







  <!--Todo: process Response Object and its headers, schema, examples -->


  <h3 class="field-label">Produces</h3>
  This API call produces the following media types according to the <span class="header">Accept</span> request header;
  the media type will be conveyed by the <span class="header">Content-Type</span> response header.
  <ul>
    <li><code>application/json</code></li>
  </ul>

  <h3 class="field-label">Responses</h3>
  <h4 class="field-label">200</h4>
  Returns the newly created jobId
      <a href="#"></a>
  <h4 class="field-label">404</h4>
  If the job was not found
      <a href="#inline_response_400">inline_response_400</a>
  </div> <!-- method -->
  <hr/>
  <div class="method"><a name="jobsV1JobIdGet"></a>
 <div class="method-path">
 
 <pre class="get"><code class="huge"><span class="http-method">get</span> /jobs/v1/{jobId}</code></pre></div>
 <div class="method-summary">Returns status for the specified job. </div>
 <div class="method-notes"></div>

 <h3 class="field-label">Path parameters</h3>
 <div class="field-items">
   <div class="param">jobId (required)</div>
   
         <div class="param-desc"><span class="param-type">Path Parameter</span> &mdash;  </div>    </div>  <!-- field-items -->







 <!--Todo: process Response Object and its headers, schema, examples -->



 <h3 class="field-label">Responses</h3>
 <h4 class="field-label">200</h4>
 Returns the job status data
     <a href="#"></a>
 <h4 class="field-label">404</h4>
 If the job was not found
     <a href="#"></a>
  </div> <!-- method -->
  <hr/>
  <div class="method"><a name="jobsV1JobIdPost"></a>
    <div class="method-path">
    
    <pre class="post"><code class="huge"><span class="http-method">post</span> /jobs/v1/{jobId}</code></pre></div>
    <div class="method-summary">Repost a job definition to an existing job. </div>
    <div class="method-notes">The existing job must have been created with isIdling set to true.</div>

 <h3 class="field-label">Path parameters</h3>
 <div class="field-items">
 <div class="param">jobId (required)</div>
   
 <div class="param-desc"><span class="param-type">Path Parameter</span> &mdash; The id which refers to an existing job </div>    </div>  <!-- field-items -->

 <h3 class="field-label">Consumes</h3>
 This API call consumes the following media types via the <span class="header">Content-Type</span> request header:
 <ul>
   <li><code>application/json-patch+json</code></li>
   <li><code>application/json</code></li>
   <li><code>text/json</code></li>
   <li><code>application/*+json</code></li>
 </ul>

 <h3 class="field-label">Request body</h3>
 <div class="field-items">
   <div class="param">body <a href="#JobDefinition">JobDefinition</a> (optional)</div>
   
         <div class="param-desc"><span class="param-type">Body Parameter</span> &mdash; The new job definition to run </div>
             </div>  <!-- field-items -->





 <!--Todo: process Response Object and its headers, schema, examples -->


 <h3 class="field-label">Produces</h3>
 This API call produces the following media types according to the <span class="header">Accept</span> request header;
 the media type will be conveyed by the <span class="header">Content-Type</span> response header.
 <ul>
   <li><code>application/json</code></li>
 </ul>

 <h3 class="field-label">Responses</h3>
 <h4 class="field-label">200</h4>
 Returns the newly created jobId
     <a href="#"></a>
 <h4 class="field-label">400</h4>
 If there was an error in the request
        <a href="#inline_response_400">inline_response_400</a>
  </div> <!-- method -->
  <hr/>
  <div class="method"><a name="jobsV1Post"></a>
    <div class="method-path">
    
    <pre class="post"><code class="huge"><span class="http-method">post</span> /jobs/v1</code></pre></div>
    <div class="method-summary">Submit a job definition. </div>
    <div class="method-notes"></div>


 <h3 class="field-label">Consumes</h3>
 This API call consumes the following media types via the <span class="header">Content-Type</span> request header:
 <ul>
   <li><code>application/json-patch+json</code></li>
   <li><code>application/json</code></li>
   <li><code>text/json</code></li>
   <li><code>application/*+json</code></li>
 </ul>

 <h3 class="field-label">Request body</h3>
 <div class="field-items">
   <div class="param">body <a href="#JobDefinition">JobDefinition</a> (optional)</div>
   
         <div class="param-desc"><span class="param-type">Body Parameter</span> &mdash; The new job definition to run </div>
             </div>  <!-- field-items -->





 <!--Todo: process Response Object and its headers, schema, examples -->


 <h3 class="field-label">Produces</h3>
 This API call produces the following media types according to the <span class="header">Accept</span> request header;
 the media type will be conveyed by the <span class="header">Content-Type</span> response header.
 <ul>
   <li><code>application/json</code></li>
 </ul>

 <h3 class="field-label">Responses</h3>
 <h4 class="field-label">200</h4>
 Returns the newly created jobId
     <a href="#"></a>
 <h4 class="field-label">400</h4>
 If there was an error in the request
     <a href="#inline_response_400">inline_response_400</a>
  </div> <!-- method -->
  <hr/>
  <h1><a name="Webhooks">Webhooks</a></h1>
  <div class="method"><a name="webhooksV1EventsGet"></a>
 <div class="method-path">
 
 <pre class="get"><code class="huge"><span class="http-method">get</span> /webhooks/v1/events</code></pre></div>
 <div class="method-summary">List all the supported event names. </div>
 <div class="method-notes"></div>








 <!--Todo: process Response Object and its headers, schema, examples -->



 <h3 class="field-label">Responses</h3>
 <h4 class="field-label">200</h4>
 Success
     <a href="#"></a>
  </div> <!-- method -->
  <hr/>
  <div class="method"><a name="webhooksV1Get"></a>
    <div class="method-path">
    
    <pre class="get"><code class="huge"><span class="http-method">get</span> /webhooks/v1</code></pre></div>
    <div class="method-summary">List the webhooks associated with the tag. Optionally provide an event in the query string to just show that one event. </div>
    <div class="method-notes">Sample response:
{
"WebhookName" : "fooTag"
"Event" : "BugFound"
"TargetUrl" : "https://mywebhookreceiver"
}</div>





 <h3 class="field-label">Query parameters</h3>
 <div class="field-items">
<div class="param">name (optional)</div>

      <div class="param-desc"><span class="param-type">Query Parameter</span> &mdash; Name of the webhook "tag" </div>      <div class="param">event (optional)</div>

      <div class="param-desc"><span class="param-type">Query Parameter</span> &mdash; Optional query string identifying the event </div>    </div>  <!-- field-items -->



 <!--Todo: process Response Object and its headers, schema, examples -->



 <h3 class="field-label">Responses</h3>
 <h4 class="field-label">200</h4>
 Returns success.
     <a href="#"></a>
 <h4 class="field-label">400</h4>
 If the event name in the query string is not a supported value
     <a href="#"></a>
 <h4 class="field-label">404</h4>
 If the webhook tag is not found
     <a href="#"></a>
  </div> <!-- method -->
  <hr/>
  <div class="method"><a name="webhooksV1Post"></a>
 <div class="method-path">
 
 <pre class="post"><code class="huge"><span class="http-method">post</span> /webhooks/v1</code></pre></div>
 <div class="method-summary">Associates a webhook "tag" with an event and target URL </div>
 <div class="method-notes">Sample response:
{
"WebhookName" : "fooTag"
"Event" : "BugFound"
"TargetUrl" : "https://mywebhookreceiver"
}</div>


  <h3 class="field-label">Consumes</h3>
  This API call consumes the following media types via the <span class="header">Content-Type</span> request header:
  <ul>
    <li><code>application/json-patch+json</code></li>
    <li><code>application/json</code></li>
    <li><code>text/json</code></li>
    <li><code>application/*+json</code></li>
  </ul>

  <h3 class="field-label">Request body</h3>
  <div class="field-items">
   <div class="param">body <a href="#WebHook">WebHook</a> (optional)</div>
   
         <div class="param-desc"><span class="param-type">Body Parameter</span> &mdash; A WebHook data type </div>
             </div>  <!-- field-items -->





 <!--Todo: process Response Object and its headers, schema, examples -->



  <h3 class="field-label">Responses</h3>
  <h4 class="field-label">200</h4>
  Returns success.
      <a href="#"></a>
  <h4 class="field-label">400</h4>
  Returns bad request if an exception occurs. The exception text will give the reason for the failure.
      <a href="#"></a>
</div> <!-- method -->
  <hr/>
  <div class="method"><a name="webhooksV1Put"></a>
    <div class="method-path">
    
    <pre class="put"><code class="huge"><span class="http-method">put</span> /webhooks/v1</code></pre></div>
    <div class="method-summary">Associates a webhook "tag" with an event and target URL </div>
    <div class="method-notes">Sample response:
{
"WebhookName" : "fooTag"
"Event" : "BugFound"
"TargetUrl" : "https://mywebhookreceiver"
}</div>


 <h3 class="field-label">Consumes</h3>
 This API call consumes the following media types via the <span class="header">Content-Type</span> request header:
 <ul>
   <li><code>application/json-patch+json</code></li>
   <li><code>application/json</code></li>
   <li><code>text/json</code></li>
   <li><code>application/*+json</code></li>
 </ul>

 <h3 class="field-label">Request body</h3>
 <div class="field-items">
   <div class="param">body <a href="#WebHook">WebHook</a> (optional)</div>
   
         <div class="param-desc"><span class="param-type">Body Parameter</span> &mdash; A WebHook data type </div>
             </div>  <!-- field-items -->





 <!--Todo: process Response Object and its headers, schema, examples -->



 <h3 class="field-label">Responses</h3>
 <h4 class="field-label">200</h4>
 Returns success.
     <a href="#"></a>
 <h4 class="field-label">400</h4>
 Returns bad request if an exception occurs. The exception text will give the reason for the failure.
     <a href="#"></a>
  </div> <!-- method -->
  <hr/>
  <div class="method"><a name="webhooksV1TestWebhookNameEventNamePut"></a>
    <div class="method-path">
    
    <pre class="put"><code class="huge"><span class="http-method">put</span> /webhooks/v1/test/{webhookName}/{eventName}</code></pre></div>
    <div class="method-summary"> </div>
    <div class="method-notes"></div>

<h3 class="field-label">Path parameters</h3>
<div class="field-items">
  <div class="param">webhookName (required)</div>
  
        <div class="param-desc"><span class="param-type">Path Parameter</span> &mdash;  </div>      <div class="param">eventName (required)</div>
  
        <div class="param-desc"><span class="param-type">Path Parameter</span> &mdash;  </div>    </div>  <!-- field-items -->







<!--Todo: process Response Object and its headers, schema, examples -->



<h3 class="field-label">Responses</h3>
<h4 class="field-label">200</h4>
Success
    <a href="#"></a>
  </div> <!-- method -->
  <hr/>
  <div class="method"><a name="webhooksV1WebhookNameEventNameDelete"></a>
 <div class="method-path">
 
 <pre class="delete"><code class="huge"><span class="http-method">delete</span> /webhooks/v1/{webhookName}/{eventName}</code></pre></div>
 <div class="method-summary">Delete the webhook for a specific event </div>
 <div class="method-notes">If the name or event are not found, no error is returned.</div>

 <h3 class="field-label">Path parameters</h3>
 <div class="field-items">
   <div class="param">webhookName (required)</div>
   
         <div class="param-desc"><span class="param-type">Path Parameter</span> &mdash; Name of the webhook tag </div>      <div class="param">eventName (required)</div>
   
         <div class="param-desc"><span class="param-type">Path Parameter</span> &mdash; Name of the event </div>    </div>  <!-- field-items -->







 <!--Todo: process Response Object and its headers, schema, examples -->



 <h3 class="field-label">Responses</h3>
 <h4 class="field-label">200</h4>
 Returns success.
     <a href="#"></a>
 <h4 class="field-label">400</h4>
 If the event name is not a supported value.
     <a href="#"></a>
  </div> <!-- method -->
  <hr/>

  <h2><a name="__Models">Models</a></h2>
  [ Jump to <a href="#__Methods">Methods</a> ]

  <h3>Table of Contents</h3>
  <ol>
    <li><a href="#AgentConfiguration"><code>AgentConfiguration</code></a></li>
    <li><a href="#AuthorizationToken"><code>AuthorizationToken</code></a></li>
    <li><a href="#AzureADConfig"><code>AzureADConfig</code></a></li>
    <li><a href="#CommandLine"><code>CommandLine</code></a></li>
    <li><a href="#CompileConfiguration"><code>CompileConfiguration</code></a></li>
    <li><a href="#CustomDictionary"><code>CustomDictionary</code></a></li>
    <li><a href="#FilePath"><code>FilePath</code></a></li>
    <li><a href="#FileShareMount"><code>FileShareMount</code></a></li>
    <li><a href="#JobDefinition"><code>JobDefinition</code></a></li>
    <li><a href="#MSALConfig"><code>MSALConfig</code></a></li>
    <li><a href="#ProblemDetails"><code>ProblemDetails</code></a></li>
    <li><a href="#RESTler"><code>RESTler</code></a></li>
    <li><a href="#RaftTask"><code>RaftTask</code></a></li>
    <li><a href="#ReplayConfiguration"><code>ReplayConfiguration</code></a></li>
    <li><a href="#RunConfiguration"><code>RunConfiguration</code></a></li>
    <li><a href="#TargetEndpointConfiguration"><code>TargetEndpointConfiguration</code></a></li>
    <li><a href="#TxtToken"><code>TxtToken</code></a></li>
    <li><a href="#Url"><code>Url</code></a></li>
    <li><a href="#ValidationProblemDetails"><code>ValidationProblemDetails</code></a></li>
    <li><a href="#WebHook"><code>WebHook</code></a></li>
    <li><a href="#ZAP"><code>ZAP</code> - ZAP</a></li>
    <li><a href="#inline_response_400"><code>inline_response_400</code></a></li>
  </ol>

  <div class="model">
    <h3><a name="AgentConfiguration"><code>AgentConfiguration</code></a> </h3>
    <div class='model-description'>Configure behaviour of RESTler agent</div>
    <div class="field-items">
      <div class="param">resultsAnalyzerReportTimeSpanInterval (optional)</div><div class="param-desc"><span class="param-type"><a href="#string">String</a></span> How often to run result analyzer against RESTler logs. Default is every 1 minute.
If not set then result analyzer will run only once after RESTler task is over. format: date-span</div>
    </div>  <!-- field-items -->
  </div>
  <div class="model">
    <h3><a name="AuthorizationToken"><code>AuthorizationToken</code></a> </h3>
    <div class='model-description'>Method of retrieving authorization token for the service under test</div>
    <div class="field-items">
      <div class="param">refreshIntervalSeconds (optional)</div><div class="param-desc"><span class="param-type"><a href="#integer">Integer</a></span> Token Refresh Interval (default every 5 minutes) format: int32</div>
<div class="param">tokenRefreshCommand (optional)</div><div class="param-desc"><span class="param-type"><a href="#"></a></span> Token refresh command </div>
      <div class="param-desc"><span class="param-type">oneOf:</span>
              <a href="#AzureADConfig"><code>AzureADConfig</code></a>&nbsp;&nbsp;&nbsp;
              <a href="#MSALConfig"><code>MSALConfig</code></a>&nbsp;&nbsp;&nbsp;
              <a href="#TxtToken"><code>TxtToken</code></a>&nbsp;&nbsp;&nbsp;
              <a href="#CommandLine"><code>CommandLine</code></a>&nbsp;&nbsp;&nbsp;
      </div>
    </div>  <!-- field-items -->
  </div>
  <div class="model">
    <h3><a name="AzureADConfig"><code>AzureADConfig</code></a> </h3>
    <div class='model-description'>Get authorization token from Azure AD using Service Principal</div>
    <div class="field-items">
      <div class="param">$type </div><div class="param-desc"><span class="param-type"><a href="#string">String</a></span>  </div>
<div class="param">secretName (optional)</div><div class="param-desc"><span class="param-type"><a href="#string">String</a></span> Key vault secret name that contains the MSAL configuration in JSON format
{ "tenant" : "&lt;tenantId&gt;", "client" : "&lt;clientid&gt;", "secret" : "&lt;secret&gt;" } </div>
    </div>  <!-- field-items -->
  </div>
  <div class="model">
    <h3><a name="CommandLine"><code>CommandLine</code></a> </h3>
    <div class='model-description'>User specified command line to execute for acquiring authorization token</div>
    <div class="field-items">
      <div class="param">$type </div><div class="param-desc"><span class="param-type"><a href="#string">String</a></span>  </div>
<div class="param">command (optional)</div><div class="param-desc"><span class="param-type"><a href="#string">String</a></span> Command line to execute </div>
    </div>  <!-- field-items -->
  </div>
  <div class="model">
    <h3><a name="CompileConfiguration"><code>CompileConfiguration</code></a> </h3>
    <div class='model-description'>User-specified RESTler compiler configuration</div>
    <div class="field-items">
      <div class="param">compileJsonGrammar (optional)</div><div class="param-desc"><span class="param-type"><a href="#string">String</a></span> Relative path to a JSON grammar to use for compilation relative to compile folder path
If set then JSON grammar used for compilation instead of Swagger </div>
<div class="param">compileFolderPath (optional)</div><div class="param-desc"><span class="param-type"><a href="#string">String</a></span> Grammar is produced by compile step prior. The compile step
file share is mounted and set here. Agent will not modify
this share. Agent will make a copy of all needed files to it&#x27;s work directory
and re-run compile with data passed through this folder. </div>
<div class="param">readOnlyFuzz (optional)</div><div class="param-desc"><span class="param-type"><a href="#boolean">Boolean</a></span> When true, only fuzz the GET requests </div>
<div class="param">allowGetProducers (optional)</div><div class="param-desc"><span class="param-type"><a href="#boolean">Boolean</a></span> Allows GET requests to be considered.
This option is present for debugging, and should be
set to &#x27;false&#x27; by default.
In limited cases when GET is a valid producer, the user
should add an annotation for it. </div>
<div class="param">staticToken (optional)</div><div class="param-desc"><span class="param-type"><a href="#string">String</a></span> Token that can be used for authenticating with service under test </div>
<div class="param">useRefreshableToken (optional)</div><div class="param-desc"><span class="param-type"><a href="#boolean">Boolean</a></span> Use refreshable token for authenticating with service under test </div>
<div class="param">mutationsSeed (optional)</div><div class="param-desc"><span class="param-type"><a href="#long">Long</a></span> Use the seed to generate random value for empty/null customDictitonary fields
if not set then default hard-coded RESTler values are used for populating customDictionary fields format: int64</div>
<div class="param">customDictionary (optional)</div><div class="param-desc"><span class="param-type"><a href="#CustomDictionary">CustomDictionary</a></span>  </div>
    </div>  <!-- field-items -->
  </div>
  <div class="model">
    <h3><a name="CustomDictionary"><code>CustomDictionary</code></a> </h3>
    
    <div class="field-items">
      <div class="param">fuzzableString (optional)</div><div class="param-desc"><span class="param-type"><a href="#string">array[String]</a></span> List of string values used as fuzzing inputs. If null then values are auto-generated </div>
<div class="param">fuzzableInt (optional)</div><div class="param-desc"><span class="param-type"><a href="#string">array[String]</a></span> List of int values used as fuzzing inputs. If null then values are auto-generated </div>
<div class="param">fuzzableNumber (optional)</div><div class="param-desc"><span class="param-type"><a href="#string">array[String]</a></span> List of number values used as fuzzing inputs. If null then values are auto-generated </div>
<div class="param">fuzzableBool (optional)</div><div class="param-desc"><span class="param-type"><a href="#string">array[String]</a></span> List of bool values used as fuzzing inputs. If null then values are auto-generated </div>
<div class="param">fuzzableDatetime (optional)</div><div class="param-desc"><span class="param-type"><a href="#string">array[String]</a></span> List of date-time values used as fuzzing inputs. If null then values are auto-generated </div>
<div class="param">fuzzableObject (optional)</div><div class="param-desc"><span class="param-type"><a href="#string">array[String]</a></span> List of string encoded JSON objects values used as fuzzing inputs </div>
<div class="param">fuzzableUuid4 (optional)</div><div class="param-desc"><span class="param-type"><a href="#string">array[String]</a></span> List of UUID4 values used as fuzzing inputs </div>
<div class="param">customPayload (optional)</div><div class="param-desc"><span class="param-type"><a href="#array">map[String, array[String]]</a></span> Map of values to use as replacement of parameters defined in Swagger specifications. For example
if { "userId" : ["a", "b"] } is specified then {userId} in URL path /users/{userId} will be replaced
by "a" or by "b" </div>
<div class="param">customPayloadUuid4Suffix (optional)</div><div class="param-desc"><span class="param-type"><a href="#string">map[String, String]</a></span> Map of values to use as replacement of parameters defined in swagger. The values will
have a random suffix added. For example {"publicIpAddressName": "publicIpAddrName-"} will produce publicIpAddrName-f286a0a069 for
publicIpAddressName parameter defined in Swagger specifications. </div>
<div class="param">customPayloadHeader (optional)</div><div class="param-desc"><span class="param-type"><a href="#array">map[String, array[String]]</a></span> User specified custom headers to pass in every request </div>
<div class="param">shadowValues (optional)</div><div class="param-desc"><span class="param-type"><a href="#map">map[String, map[String, array[String]]]</a></span> RESTler documentation will have more info on this </div>
    </div>  <!-- field-items -->
  </div>
  <div class="model">
    <h3><a name="FilePath"><code>FilePath</code></a> </h3>
    <div class='model-description'>File path to Swagger specification</div>
    <div class="field-items">
      <div class="param">$type </div><div class="param-desc"><span class="param-type"><a href="#string">String</a></span>  </div>
<div class="param">value (optional)</div><div class="param-desc"><span class="param-type"><a href="#string">String</a></span> File path </div>
    </div>  <!-- field-items -->
  </div>
  <div class="model">
    <h3><a name="FileShareMount"><code>FileShareMount</code></a> </h3>
    <div class='model-description'>Mount file share from RAFT storage account to container running a payload.</div>
    <div class="field-items">
      <div class="param">fileShareName (optional)</div><div class="param-desc"><span class="param-type"><a href="#string">String</a></span> Any fileShare name from the RAFT storage account </div>
<div class="param">mountPath (optional)</div><div class="param-desc"><span class="param-type"><a href="#string">String</a></span> Directory name under which file share is mounted on the container. For example "/my-job-config" </div>
    </div>  <!-- field-items -->
  </div>
  <div class="model">
    <h3><a name="JobDefinition"><code>JobDefinition</code></a> </h3>
    <div class='model-description'>RAFT job run definition</div>
    <div class="field-items">
      <div class="param">swagger (optional)</div><div class="param-desc"><span class="param-type"><a href="#"></a></span> Swagger specification location for the job run </div>
      <div class="param-desc"><span class="param-type">oneOf:</span>
              <a href="#Url"><code>Url</code></a>&nbsp;&nbsp;&nbsp;
              <a href="#FilePath"><code>FilePath</code></a>&nbsp;&nbsp;&nbsp;
      </div>
<div class="param">namePrefix (optional)</div><div class="param-desc"><span class="param-type"><a href="#string">String</a></span> String used as a prefix added to service generated job ID.
Prefix can contain only lowercase letters, numbers, and hyphens, and must begin with a letter or a number.
Prefix cannot contain two consecutive hyphens.
Can be up to 27 characters long </div>
<div class="param">tasks (optional)</div><div class="param-desc"><span class="param-type"><a href="#RaftTask">array[RaftTask]</a></span> RAFT Task definitions </div>
<div class="param">duration (optional)</div><div class="param-desc"><span class="param-type"><a href="#string">String</a></span> Duration of the job; if not set, then job runs till completion (or forever).
For RESTler jobs - time limit is only useful for Fuzz task format: date-span</div>
<div class="param">host (optional)</div><div class="param-desc"><span class="param-type"><a href="#string">String</a></span> Override the Host for each request. </div>
<div class="param">webhook (optional)</div><div class="param-desc"><span class="param-type"><a href="#string">String</a></span> Webhook to associate with the job </div>
<div class="param">metadata (optional)</div><div class="param-desc"><span class="param-type"><a href="#string">map[String, String]</a></span> Arbitrary key/value pairs that will be returned in webhooks </div>
<div class="param">rootFileShare (optional)</div><div class="param-desc"><span class="param-type"><a href="#string">String</a></span> If set, then place all job run results into this file share </div>
<div class="param">readOnlyFileSharesMounts (optional)</div><div class="param-desc"><span class="param-type"><a href="#FileShareMount">array[FileShareMount]</a></span> File shares to mount from RAFT deployment storage account as read-only directories. </div>
<div class="param">readWriteFileSharesMounts (optional)</div><div class="param-desc"><span class="param-type"><a href="#FileShareMount">array[FileShareMount]</a></span> File shares to mount from RAFT deployment storage account as read-write directories. </div>
    </div>  <!-- field-items -->
  </div>
  <div class="model">
    <h3><a name="MSALConfig"><code>MSALConfig</code></a> </h3>
    <div class='model-description'>Get authorization token using MSAL. Uses .default for scope.</div>
    <div class="field-items">
      <div class="param">$type </div><div class="param-desc"><span class="param-type"><a href="#string">String</a></span>  </div>
<div class="param">secretName (optional)</div><div class="param-desc"><span class="param-type"><a href="#string">String</a></span> Key vault secret name that contains the MSAL configuration in JSON format
{ "tenant" : "&lt;tenantId&gt;", "client" : "&lt;clientid&gt;", "secret" : "&lt;secret&gt;" } </div>
    </div>  <!-- field-items -->
  </div>
  <div class="model">
    <h3><a name="ProblemDetails"><code>ProblemDetails</code></a> </h3>
    
    <div class="field-items">
          </div>  <!-- field-items -->
  </div>
  <div class="model">
    <h3><a name="RESTler"><code>RESTler</code></a> </h3>
    <div class='model-description'>RESTler payload</div>
    <div class="field-items">
      <div class="param">task (optional)</div><div class="param-desc"><span class="param-type"><a href="#string">String</a></span> Can be compile, fuzz, test, replay </div>
<div class="param">compileConfiguration (optional)</div><div class="param-desc"><span class="param-type"><a href="#CompileConfiguration">CompileConfiguration</a></span>  </div>
<div class="param">runConfiguration (optional)</div><div class="param-desc"><span class="param-type"><a href="#RunConfiguration">RunConfiguration</a></span>  </div>
<div class="param">replayConfiguration (optional)</div><div class="param-desc"><span class="param-type"><a href="#ReplayConfiguration">ReplayConfiguration</a></span>  </div>
<div class="param">agentConfiguration (optional)</div><div class="param-desc"><span class="param-type"><a href="#AgentConfiguration">AgentConfiguration</a></span>  </div>
    </div>  <!-- field-items -->
  </div>
  <div class="model">
    <h3><a name="RaftTask"><code>RaftTask</code></a> </h3>
    <div class='model-description'>RAFT task to run.</div>
    <div class="field-items">
      <div class="param">toolName (optional)</div><div class="param-desc"><span class="param-type"><a href="#string">String</a></span> Tool defined by folder name located in cli/raft-tools/tools/{ToolName} </div>
<div class="param">nameSuffix (optional)</div><div class="param-desc"><span class="param-type"><a href="#string">String</a></span> User assigned string appended to agent name, and agent generated results folder.
Must not contain: /*?:|&amp;lt;&amp;gt;" </div>
<div class="param">swagger (optional)</div><div class="param-desc"><span class="param-type"><a href="#"></a></span> Override swagger specification location </div>
      <div class="param-desc"><span class="param-type">oneOf:</span>
              <a href="#Url"><code>Url</code></a>&nbsp;&nbsp;&nbsp;
              <a href="#FilePath"><code>FilePath</code></a>&nbsp;&nbsp;&nbsp;
      </div>
<div class="param">host (optional)</div><div class="param-desc"><span class="param-type"><a href="#string">String</a></span> Override the Host for each request. </div>
<div class="param">isIdling (optional)</div><div class="param-desc"><span class="param-type"><a href="#boolean">Boolean</a></span> Is this container idled and waiting for user to connect </div>
<div class="param">duration (optional)</div><div class="param-desc"><span class="param-type"><a href="#string">String</a></span> Duration of the task; if not set, then job level duration is used.
For RESTler jobs - time limit is only useful for Fuzz task format: date-span</div>
<div class="param">authTokenConfiguration (optional)</div><div class="param-desc"><span class="param-type"><a href="#AuthorizationToken">AuthorizationToken</a></span>  </div>
<div class="param">keyVaultSecrets (optional)</div><div class="param-desc"><span class="param-type"><a href="#string">array[String]</a></span> List of names of secrets in Keyvault used in configuring
authentication credentials
Key Vault secret name must start with alphabetic character and
followed by a string of alphanumeric characters (for example &#x27;MyName123&#x27;).
Secret name can be upto 127 characters long </div>
<div class="param">taskConfiguration (optional)</div><div class="param-desc"><span class="param-type"><a href="#"></a></span>  </div>
      <div class="param-desc"><span class="param-type">anyOf:</span>
              <a href="#RESTler"><code>RESTler</code></a>&nbsp;&nbsp;&nbsp;
      </div>
    </div>  <!-- field-items -->
  </div>
  <div class="model">
    <h3><a name="ReplayConfiguration"><code>ReplayConfiguration</code></a> </h3>
    <div class='model-description'>RESTler configuration for replaying request sequences that triggered a reproducable bug</div>
    <div class="field-items">
      <div class="param">replayBugBucketsPaths (optional)</div><div class="param-desc"><span class="param-type"><a href="#string">array[String]</a></span> List of paths to RESTler folder runs to replay (names of folders are assigned when mounted readonly/readwrite file share mounts).
If path is a folder, then all bug buckets replayed in the folder.
If path is a bug_bucket file - then only that file is replayed.
If empty - then replay all bugs under RunConfiguration.previousStepOutputFolderPath. </div>
<div class="param">runConfiguration (optional)</div><div class="param-desc"><span class="param-type"><a href="#RunConfiguration">RunConfiguration</a></span>  </div>
    </div>  <!-- field-items -->
  </div>
  <div class="model">
    <h3><a name="RunConfiguration"><code>RunConfiguration</code></a> </h3>
    <div class='model-description'>RESTler job Test, Fuzz or Replay configuration</div>
    <div class="field-items">
      <div class="param">grammarPy (optional)</div><div class="param-desc"><span class="param-type"><a href="#string">String</a></span> Path to grammar py relative to compile folder path. If not set then default "grammar.py" grammar is assumed </div>
<div class="param">previousStepOutputFolderPath (optional)</div><div class="param-desc"><span class="param-type"><a href="#string">String</a></span> For Test or Fuzz tasks: Grammar is produced by compile step. The compile step
file share is mounted and set here. Agent will not modify
this share. Agent will make a copy of all needed files to it&#x27;s work directory.
For Replay task: path to RESTler Fuzz or Test run that contains bug buckets to replay </div>
<div class="param">targetEndpointConfiguration (optional)</div><div class="param-desc"><span class="param-type"><a href="#TargetEndpointConfiguration">TargetEndpointConfiguration</a></span>  </div>
<div class="param">producerTimingDelay (optional)</div><div class="param-desc"><span class="param-type"><a href="#integer">Integer</a></span> The delay in seconds after invoking an API that creates a new resource format: int32</div>
<div class="param">useSsl (optional)</div><div class="param-desc"><span class="param-type"><a href="#boolean">Boolean</a></span> Use SSL when connecting to the server </div>
<div class="param">pathRegex (optional)</div><div class="param-desc"><span class="param-type"><a href="#string">String</a></span> Path regex for filtering tested endpoints </div>
    </div>  <!-- field-items -->
  </div>
  <div class="model">
    <h3><a name="TargetEndpointConfiguration"><code>TargetEndpointConfiguration</code></a> </h3>
    <div class='model-description'>Configuration of the endpoint under test</div>
    <div class="field-items">
      <div class="param">ip (optional)</div><div class="param-desc"><span class="param-type"><a href="#string">String</a></span> The IP of the endpoint being fuzzed </div>
<div class="param">port (optional)</div><div class="param-desc"><span class="param-type"><a href="#integer">Integer</a></span> The port of the endpoint being fuzzed. Defaults to 443. format: int32</div>
    </div>  <!-- field-items -->
  </div>
  <div class="model">
    <h3><a name="TxtToken"><code>TxtToken</code></a> </h3>
    <div class='model-description'>Use static authorization token defined in a text file</div>
    <div class="field-items">
      <div class="param">$type </div><div class="param-desc"><span class="param-type"><a href="#string">String</a></span>  </div>
<div class="param">secretName (optional)</div><div class="param-desc"><span class="param-type"><a href="#string">String</a></span> Key vault secret name that contains the token as the secret value </div>
    </div>  <!-- field-items -->
  </div>
  <div class="model">
    <h3><a name="Url"><code>Url</code></a> </h3>
    <div class='model-description'>URL to Swagger specification</div>
    <div class="field-items">
      <div class="param">$type </div><div class="param-desc"><span class="param-type"><a href="#string">String</a></span>  </div>
<div class="param">value (optional)</div><div class="param-desc"><span class="param-type"><a href="#string">String</a></span> URL </div>
    </div>  <!-- field-items -->
  </div>
  <div class="model">
    <h3><a name="ValidationProblemDetails"><code>ValidationProblemDetails</code></a> </h3>
    
    <div class="field-items">
          </div>  <!-- field-items -->
  </div>
  <div class="model">
    <h3><a name="WebHook"><code>WebHook</code></a> </h3>
    
    <div class="field-items">
      <div class="param">webhookName (optional)</div><div class="param-desc"><span class="param-type"><a href="#string">String</a></span>  </div>
<div class="param">event (optional)</div><div class="param-desc"><span class="param-type"><a href="#string">String</a></span>  </div>
<div class="param">targetUrl (optional)</div><div class="param-desc"><span class="param-type"><a href="#string">String</a></span>  format: uri</div>
    </div>  <!-- field-items -->
  </div>
  <div class="model">
    <h3><a name="ZAP"><code>ZAP</code> - ZAP</a> </h3>
    
    <div class="field-items">
          </div>  <!-- field-items -->
  </div>
  <div class="model">
    <h3><a name="inline_response_400"><code>inline_response_400</code></a> </h3>
    
    <div class="field-items">
          </div>  <!-- field-items -->
  </div>
  </body>
</html>
