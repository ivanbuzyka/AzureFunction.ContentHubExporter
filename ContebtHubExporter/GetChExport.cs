using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace ContebtHubExporter
{
    public static class GetChExport
    {
        [FunctionName("GetChExport")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            log.LogInformation("C# HTTP trigger function processed a request.");
            
            // what this code is doing
            // 0. receive params (as JSON from body): (params - Hostname, X-Auth-Token, "to_export" options):
            // 1. call /api/package/export
            // 2. call location header from #1
            // 3. call download_location.href from #2 (response body), make sure to follow redirects
            // 4. download and return downloaded content to response 
            
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            dynamic data = JsonConvert.DeserializeObject(requestBody);
            var hostname = data?.hostname;
            var xAuthToken = (data?.xAuthToken).ToString();
            var toExport = data?.toExport;
            log.LogInformation("Extracted and initialized parameters");

            var exportPayload = JsonConvert.SerializeObject(new { to_export = toExport, include_system_owned = false });
            log.LogInformation("Prepared json payload: ");
            log.LogInformation(exportPayload);

            byte[] packageFile = null;

            using (var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true, MaxAutomaticRedirections = 7 }))
            {
                client.DefaultRequestHeaders.Add("X-Auth-Token", xAuthToken);
                var content = new StringContent(exportPayload, Encoding.UTF8, "application/json");
                var response = await client.PostAsync($"{hostname}/api/package/export", content);
                var downloadOrderLocation = response.Headers.Location;
                log.LogInformation($"Sent first request, location header is: {downloadOrderLocation}");

                // check for "status": "Completed" before trying to download
                
                // increase retriesCount if needed to wait longer for package be ready
                var retriesCount = 20;
                var packageDownloadLink = string.Empty;
                for (int i = 0; i < retriesCount; i++)
                {
                    log.LogInformation($"Trying to get package link from download order: attempt #{i}");
                    
                    // wait and then check again
                    await Task.Delay(2000);
                    var downloadOrderResponse = await client.GetAsync(downloadOrderLocation);
                    var downloadOrderResult = await downloadOrderResponse.Content.ReadAsStringAsync();

                    dynamic downloadOrderResultObject = JsonConvert.DeserializeObject(downloadOrderResult);
                    string downloadStatus = downloadOrderResultObject.status;
                    if (downloadStatus.Equals("Completed", System.StringComparison.OrdinalIgnoreCase))
                    {
                        log.LogInformation($"Trying to get package link from download order: COMPLETED on attempt #{i}");
                        packageDownloadLink = downloadOrderResultObject.download_location.href;
                        break;
                    }
                }

                log.LogInformation($"got link to package file: {packageDownloadLink}");

                packageFile = await client.GetByteArrayAsync(packageDownloadLink);
                log.LogInformation("downloaded package file to byte array");
            }

            return new FileContentResult(packageFile, "application/octet-stream");
        }
    }
}

