using NzbDrone.Common.Http;

namespace NzbDrone.Common.Cloud
{
    public interface IReadarrCloudRequestBuilder
    {
        IHttpRequestBuilderFactory Services { get; }
        IHttpRequestBuilderFactory Metadata { get; }
    }

    public class ReadarrCloudRequestBuilder : IReadarrCloudRequestBuilder
    {
        public ReadarrCloudRequestBuilder()
        {
            //TODO: Create Update Endpoint
            Services = new HttpRequestBuilder("https://readarr.servarr.com/v1/")
                .CreateFactory();

            // Updated to use rreading-glasses service (api.bookinfo.pro) as the default metadata source
            // This provides a working alternative to the broken bookinfo.club service
            Metadata = new HttpRequestBuilder("https://api.bookinfo.pro/v1/{route}")
                .CreateFactory();
        }

        public IHttpRequestBuilderFactory Services { get; }

        public IHttpRequestBuilderFactory Metadata { get; }
    }
}
