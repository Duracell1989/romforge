using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace RomForge.Core.IntegrationTests.Helpers
{
    internal sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly byte[] _content;

        internal FakeHttpMessageHandler(HttpStatusCode statusCode, byte[] content)
        {
            _statusCode = statusCode;
            _content = content;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            HttpResponseMessage response = new HttpResponseMessage(_statusCode) { Content = new ByteArrayContent(_content) };
            return Task.FromResult(response);
        }
    }
}
