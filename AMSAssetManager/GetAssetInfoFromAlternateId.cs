using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.WindowsAzure.MediaServices.Client;
using System.Collections.Generic;
using Newtonsoft.Json;
using System.Net.Http.Formatting;

namespace AMSAssetManager
{
    public static class GetAssetInfoFromAlternateIdClass
    {
        static public CloudMediaContext _context = null;

        private static readonly string _AMSAADTenantDomain = Environment.GetEnvironmentVariable("AMSAADTenantDomain");
        private static readonly string _AMSRESTAPIEndpoint = Environment.GetEnvironmentVariable("AMSRESTAPIEndpoint");
        private static readonly string _AMSClientId = Environment.GetEnvironmentVariable("AMSClientId");
        private static readonly string _AMSClientSecret = Environment.GetEnvironmentVariable("AMSClientSecret");

        const string RouteGetAssetInfoFromAlternateId = "GetAssetInfoFromAlternateId/{movieId}";

        const string strContextError = "Unable to create AMS context, check credentials on the Function App";
        const string strAssetsNotFoundError = "No assets with this movidId found.";
        const string strLocatorNotFoundError = "One or more assets do not have a valid locator.";
        [FunctionName("GetAssetInfoFromAlternateId")]
        public static async Task<HttpResponseMessage> GetAssetInfoFromAlternateId([HttpTrigger(AuthorizationLevel.Function, "get",  Route = RouteGetAssetInfoFromAlternateId)]HttpRequestMessage req, string movieId, TraceWriter log)
        {
            log.Info("C# HTTP trigger function processed a request.");
            try
            {
               _context = _context ??  GetCloudMediaContextAADPrincipal(log);
            }
            catch
            {
                log.Info(strContextError);
                return req.CreateErrorResponse(HttpStatusCode.Forbidden, strContextError);
            }
            List<IAsset> assetsWithId = GetAssestsWithAlternateId(movieId);
            if(assetsWithId.Count == 0)
            {
                log.Info(strAssetsNotFoundError);
                return req.CreateErrorResponse(HttpStatusCode.NotFound, strAssetsNotFoundError);
            }
            IStreamingEndpoint defaultOrigin = _context.StreamingEndpoints.Where(se => se.Name == "default").FirstOrDefault();
            List<string> manifestUrls = new List<string>();
            foreach (IAsset asset in assetsWithId)
            {

                ILocator locator = asset.Locators.OrderBy(o => o.Id).FirstOrDefault();
                if (locator == null)
                {
                    log.Info(strLocatorNotFoundError);
                    return req.CreateErrorResponse(HttpStatusCode.NotFound, strLocatorNotFoundError);
                }

                Uri publishedIsmUrl = GetValidOnDemandURI(defaultOrigin, asset, locator);
                manifestUrls.Add(publishedIsmUrl.AbsoluteUri);
            }
            return req.CreateResponse(HttpStatusCode.OK, manifestUrls, JsonMediaTypeFormatter.DefaultMediaType); 
        }
        private static CloudMediaContext GetCloudMediaContextAADPrincipal(TraceWriter log)
        {
            try
            {
                AzureAdTokenCredentials tokenCredentials = new AzureAdTokenCredentials(_AMSAADTenantDomain,
                          new AzureAdClientSymmetricKey(_AMSClientId, _AMSClientSecret),
                          AzureEnvironments.AzureCloudEnvironment);

                AzureAdTokenProvider tokenProvider = new AzureAdTokenProvider(tokenCredentials);

                CloudMediaContext context = new CloudMediaContext(new Uri(_AMSRESTAPIEndpoint), tokenProvider);
                return context;
            }
            catch (Exception e)
            {

                log.Info(strContextError);
                return null;
            }
        }
        private static List<IAsset> GetAssestsWithAlternateId(string alternateID)
        {
            return _context.Assets.Where(a => a.AlternateId == alternateID).OrderBy(o => o.Name).ToList();
        }
        public static Uri GetValidOnDemandURI(IStreamingEndpoint origin, IAsset asset, ILocator locator)
        {
            var ismFile = asset.AssetFiles.AsEnumerable().Where(f => f.Name.EndsWith(".ism")).OrderByDescending(f => f.IsPrimary).FirstOrDefault();
            string strPublishUrl = $"https://{origin.HostName}/{locator.ContentAccessComponent}/{ismFile.Name}/manifest";
            return new Uri(strPublishUrl);
        }
    }
}
